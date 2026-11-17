**Такая очистка уже есть, и новый сервис не нужен.** FR-7.4 реализована в `BackupOrchestrator.EmptyTemporaryFolder` (`src/GitHubBackup.Core/Runs/BackupOrchestrator.cs:923`). Сейчас она работает так:

```
TryAcquireAsync (стр. 173) → RunLockedAsync → EmptyTemporaryFolder(.tmp) (стр. 238)
```

- Очистка идёт после захвата блокировки.
- При `--dry-run` её нет, это требует FR-9.5.
- Файл, который не удалось удалить, остаётся с записью в лог, и запуск продолжается.
- Папка-ссылка (reparse point) не очищается: иначе удалились бы файлы вне `sourcesRoot`.
- Тесты уже есть в `BackupOrchestratorTests`: удаление, заблокированный файл, ссылка, dry-run, порядок относительно блокировки.

Поведение PRD менять не нужно. Остаётся один осмысленный вариант: **вынести очистку из оркестратора в отдельный сервис**. Оркестратор уже больше 2500 строк, а очистка самостоятельна и легко тестируется отдельно. Ниже код для этого варианта. Я ничего не менял, и в сборке код не проверял.

## Новые файлы (`src/GitHubBackup.Core/Runs/`)

`ITemporaryFolderCleaner.cs`
```csharp
namespace GitHubBackup.Core.Runs;

/// <summary>
/// Deletes what interrupted runs left in the folder of temporary archives.
/// </summary>
public interface ITemporaryFolderCleaner
{
    /// <summary>
    /// Deletes the files and folders in <paramref name="folder"/>.
    /// </summary>
    /// <remarks>
    /// The caller holds the run lock, so no other run writes to the folder. An entry that cannot be deleted is logged
    /// and left for the next run; a folder that is a junction or a symbolic link is left alone, because emptying it
    /// would delete files outside the sources root.
    /// </remarks>
    /// <param name="folder">The folder of temporary archives.</param>
    void Empty(string folder);
}
```

`TemporaryFolderCleaner.cs`: тело перенесено из `EmptyTemporaryFolder` без изменений в логике.
```csharp
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Runs;

/// <summary>
/// Deletes the leftovers of interrupted runs from the folder of temporary archives.
/// </summary>
/// <param name="fileSystem">Lists and deletes the entries of the folder.</param>
/// <param name="logger">Logs each deleted entry and each failure.</param>
public sealed class TemporaryFolderCleaner(IFileSystem fileSystem, ILogger<TemporaryFolderCleaner> logger)
    : ITemporaryFolderCleaner
{
    /// <inheritdoc />
    public void Empty(string folder)
    {
        if (!fileSystem.Directory.Exists(folder))
        {
            return;
        }

        if (fileSystem.DirectoryInfo.New(folder).Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            TemporaryFolderCleanerLog.FolderIsLink(logger, folder);
            return;
        }

        string[] entries;
        try
        {
            entries = fileSystem.Directory.GetFileSystemEntries(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TemporaryFolderCleanerLog.FolderNotListed(logger, folder, exception);
            return;
        }

        foreach (var entry in entries)
        {
            try
            {
                if (fileSystem.Directory.Exists(entry))
                {
                    fileSystem.Directory.Delete(entry, recursive: true);
                }
                else
                {
                    fileSystem.File.Delete(entry);
                }

                TemporaryFolderCleanerLog.EntryDeleted(logger, entry);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                TemporaryFolderCleanerLog.EntryNotDeleted(logger, entry, exception);
            }
        }
    }
}
```

Метод синхронный, как сейчас: `IFileSystem` не даёт асинхронного удаления, поэтому токена отмены нет.

`TemporaryFolderCleanerLog.cs`: те же четыре сообщения, что сейчас в `BackupOrchestratorLog`.
```csharp
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Runs;

/// <summary>
/// Defines the log entries of <see cref="TemporaryFolderCleaner"/>.
/// </summary>
internal static partial class TemporaryFolderCleanerLog
{
    /// <summary>Logs that a leftover was deleted.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The file or folder.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Deleted the leftover temporary archive {Path}")]
    public static partial void EntryDeleted(ILogger logger, string path);

    /// <summary>Logs that a leftover cannot be deleted and stays for the next run.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The file or folder.</param>
    /// <param name="exception">The reason.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Cannot delete the leftover temporary archive {Path}")]
    public static partial void EntryNotDeleted(ILogger logger, string path, Exception exception);

    /// <summary>Logs that the folder cannot be listed and is not emptied.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The folder.</param>
    /// <param name="exception">The reason.</param>
    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Cannot list the temporary archive folder {Path}")]
    public static partial void FolderNotListed(ILogger logger, string path, Exception exception);

    /// <summary>Logs that the folder is a junction or a symbolic link and is not emptied.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The folder.</param>
    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "The temporary archive folder {Path} is a link and is not emptied")]
    public static partial void FolderIsLink(ILogger logger, string path);
}
```

Текст сообщения для `EntryDeleted` я придумал. Я не видел строку `Message` у `BackupOrchestratorLog.TemporaryEntryDeleted` (`BackupOrchestratorLog.cs:26`), поэтому при переносе возьмите её оттуда. Остальные три сообщения скопированы из файла.

## Регистрация и вызов

`CoreServiceCollectionExtensions.cs`: одна строка рядом с остальными, плюс имя в `<summary>`:
```csharp
services.TryAddSingleton<ITemporaryFolderCleaner, TemporaryFolderCleaner>();
```

`BackupOrchestrator.cs`:
- В первичный конструктор добавляется `ITemporaryFolderCleaner temporaryFolderCleaner` и строка `<param>`. Параметр `fileSystem` остаётся, потому что его использует `IsInside` (стр. 908). Его `<param>` ("Empties the folder of temporary archives") нужно переписать.
- Метод `EmptyTemporaryFolder` (стр. 913–967) удаляется.
- Вызов меняется на месте, порядок «блокировка → очистка» не трогается:
```csharp
else
{
    temporaryFolderCleaner.Empty(temporaryFolder);
}
```
- Из `BackupOrchestratorLog` уходят `TemporaryEntryDeleted`, `TemporaryEntryNotDeleted`, `TemporaryFolderNotListed` и `TemporaryFolderIsLink`.
- `TemporaryFolderLeftUntouched` остаётся, потому что относится к dry-run.

## Тесты

Новый файл `src/GitHubBackup.Core.Tests/Runs/TemporaryFolderCleanerTests.cs`. Он использует те же приёмы, что и тесты оркестратора: `MockFileSystem`, `FakeLogger`, `AllowedFileShare = FileShare.None` для заблокированного файла и атрибут `ReparsePoint` для ссылки.
```csharp
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Diagnostics.Testing;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace GitHubBackup.Core.Runs;

public sealed class TemporaryFolderCleanerTests
{
    private const string _folder = @"C:\sources\.tmp";
    private const int _entryDeletedEventId = 1;
    private const int _entryNotDeletedEventId = 2;
    private const int _folderIsLinkEventId = 4;

    private readonly MockFileSystem _fileSystem = new();
    private readonly FakeLogger<TemporaryFolderCleaner> _logger = new();

    [Fact]
    public void Empty_FilesAndNestedFolder_DeletesAllAndKeepsFolder()
    {
        _fileSystem.AddFile(Path.Combine(_folder, "a.zip"), new("partial"));
        _fileSystem.AddFile(Path.Combine(_folder, "nested", "b.zip"), new("partial"));

        Create().Empty(_folder);

        _fileSystem.Directory.Exists(_folder).ShouldBeTrue();
        _fileSystem.Directory.GetFileSystemEntries(_folder).ShouldBeEmpty();
        Records(_entryDeletedEventId).Count.ShouldBe(2);
    }

    [Fact]
    public void Empty_FolderMissing_DoesNothingAndDoesNotCreateIt()
    {
        Create().Empty(_folder);

        _fileSystem.Directory.Exists(_folder).ShouldBeFalse();
        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public void Empty_LeftoverCannotBeDeleted_LogsWarningAndDeletesTheRest()
    {
        var locked = Path.Combine(_folder, "locked.zip");
        var free = Path.Combine(_folder, "free.zip");
        _fileSystem.AddFile(locked, new("partial") { AllowedFileShare = FileShare.None });
        _fileSystem.AddFile(free, new("partial"));

        Create().Empty(_folder);

        _fileSystem.File.Exists(locked).ShouldBeTrue();
        _fileSystem.File.Exists(free).ShouldBeFalse();
        var record = Records(_entryNotDeletedEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(locked);
    }

    [Fact]
    public void Empty_FolderIsLink_LeavesContentAndLogsWarning()
    {
        var leftover = Path.Combine(_folder, "a.zip");
        _fileSystem.AddFile(leftover, new("partial"));
        _fileSystem.DirectoryInfo.New(_folder).Attributes |= FileAttributes.ReparsePoint;

        Create().Empty(_folder);

        _fileSystem.File.Exists(leftover).ShouldBeTrue();
        var record = Records(_folderIsLinkEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(_folder);
    }

    private TemporaryFolderCleaner Create() => new(_fileSystem, _logger);

    private IReadOnlyList<FakeLogRecord> Records(int eventId) =>
        _logger.Collector.GetSnapshot().Where(record => record.Id.Id == eventId).ToList();
}
```

Не покрыт случай «папку нельзя перечислить» (`FolderNotListed`): `MockFileSystem` не умеет имитировать такой отказ. Сейчас этот случай тоже не покрыт.

**Тесты оркестратора** (`BackupOrchestratorTests.cs`):
- **Переезжают в новый класс:** тесты на удаление, заблокированный файл и ссылку (стр. 789–844). Вместе с ними уходят константы `_temporaryEntry*EventId` и `_temporaryFolderIsLinkEventId`.
- **Остаются в оркестраторе:** проверка порядка «очистка после блокировки» (около стр. 711) и «dry-run не очищает» (около стр. 1490). Они будут проверять вызов `ITemporaryFolderCleaner`, подставленного через NSubstitute, а не файлы на диске.
- **Конструктор:** в хелпере, который создаёт `BackupOrchestrator`, добавляется аргумент. Сам хелпер я не читал.
- **Ещё один тест:** «блокировка занята → `Empty` не вызывается».
- **Регистрация:** `CoreServiceCollectionExtensionsTests` проверяет регистрации, поэтому нужен случай для `ITemporaryFolderCleaner`.

## Что решить вам

1. **Нужен ли рефакторинг.** Я бы его сделал: поведение не меняется, PRD трогать не надо, оркестратор становится короче. Если цель была другой (например, очищать `.tmp` ещё где-то), напишите, и я пересмотрю.
2. **Номера событий лога.** При переносе они меняются с 3, 4, 10 на 1–4. Если кто-то фильтрует логи по номерам, оставьте старые.
3. **Оформление.** Это работа без issue. Ветку, коммит и PR я создам только по вашему явному указанию.
