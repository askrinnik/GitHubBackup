**Кратко:** очистка по FR-7.4 в коде уже есть. Это приватный метод `BackupOrchestrator.EmptyTemporaryFolder` (`src/GitHubBackup.Core/Runs/BackupOrchestrator.cs:432`). Оркестратор вызывает его в `RunLockedAsync` после `runLock.TryAcquireAsync`. Метод пропускается в dry-run (FR-9.5), что тоже по PRD. Если файл не удаётся, метод пишет предупреждение и оставляет файл до следующего запуска. Если папка — junction или symlink, метод её не трогает. Тесты тоже есть (`BackupOrchestratorTests.cs:502–557`, `1082`, `1193`).

Поэтому «добавить сервис» здесь значит **вынести этот метод в отдельный сервис**. Поведение не меняется. Ниже код выноса. Ничего не изменено, файлы не правились. Если вынос вам не нужен, FR-7.4 уже выполнен.

## Файлы

```
src/GitHubBackup.Core/Runs/
  ITemporaryFolderCleaner.cs        новый
  TemporaryFolderCleaner.cs         новый (код из EmptyTemporaryFolder)
  TemporaryFolderCleanerLog.cs      новый (4 записи лога из BackupOrchestratorLog)
  BackupOrchestrator.cs             изменён: параметр IFileSystem заменён на ITemporaryFolderCleaner
  BackupOrchestratorLog.cs          изменён: удалены записи с EventId 2, 3, 4, 10
src/GitHubBackup.Core/CoreServiceCollectionExtensions.cs   изменён: одна строка регистрации
src/GitHubBackup.Core.Tests/Runs/
  TemporaryFolderCleanerTests.cs    новый
  BackupOrchestratorTests.cs        изменён
```

Сервис живёт в `Core`. Ему нужен только `IFileSystem` из `System.IO.Abstractions`, а `Core` уже использует его в оркестраторе. `ArchitectureTests` это допускает.

## Код

`ITemporaryFolderCleaner.cs`:

```csharp
namespace GitHubBackup.Core.Runs;

/// <summary>
/// Deletes what interrupted runs left in the folder of temporary archives.
/// </summary>
public interface ITemporaryFolderCleaner
{
    /// <summary>
    /// Empties <paramref name="folder"/> without deleting the folder itself.
    /// </summary>
    /// <remarks>
    /// The caller holds the run lock, so no other run writes to the folder. An entry that cannot be deleted is
    /// logged and left for the next run; the method never throws for an I/O failure.
    /// </remarks>
    /// <param name="folder">The folder of temporary archives.</param>
    void Clean(string folder);
}
```

`TemporaryFolderCleaner.cs`. Тело перенесено из `EmptyTemporaryFolder` без изменений в логике:

```csharp
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Runs;

/// <summary>
/// Implements <see cref="ITemporaryFolderCleaner"/> through <see cref="IFileSystem"/>.
/// </summary>
/// <remarks>
/// The folder holds only archives under construction, so anything in it at the start of a run is garbage. A folder
/// that is a junction or a symbolic link is left alone, because emptying it would delete files outside the sources
/// root.
/// </remarks>
/// <param name="fileSystem">Lists and deletes the entries.</param>
/// <param name="logger">The logger.</param>
public sealed class TemporaryFolderCleaner(IFileSystem fileSystem, ILogger<TemporaryFolderCleaner> logger)
    : ITemporaryFolderCleaner
{
    /// <inheritdoc />
    public void Clean(string folder)
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

Метод синхронный, как и сейчас: у `IFileSystem` нет асинхронного удаления. Поэтому у него нет `CancellationToken`.

`TemporaryFolderCleanerLog.cs`. Тексты сообщений и уровни те же. Меняется только категория логгера, с `BackupOrchestrator` на `TemporaryFolderCleaner`:

```csharp
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Runs;

/// <summary>
/// Defines the log entries of <see cref="TemporaryFolderCleaner"/>.
/// </summary>
internal static partial class TemporaryFolderCleanerLog
{
    /// <summary>Logs a leftover that was deleted.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The deleted file or folder.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Deleted the leftover temporary archive {Path}")]
    public static partial void EntryDeleted(ILogger logger, string path);

    /// <summary>Logs a leftover that cannot be deleted and stays for the next run.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The file or folder.</param>
    /// <param name="exception">The reason.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Cannot delete the leftover temporary archive {Path}")]
    public static partial void EntryNotDeleted(ILogger logger, string path, Exception exception);

    /// <summary>Logs that the folder cannot be listed.</summary>
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

## Регистрация и вызов

`CoreServiceCollectionExtensions.cs`, рядом со строкой 52 (`BackupOrchestrator`):

```csharp
services.TryAddSingleton<ITemporaryFolderCleaner, TemporaryFolderCleaner>();
```

`IFileSystem` регистрирует `AddGitHubBackupInfrastructure` (строка 75), как и для оркестратора. Дополнительно ничего не нужно. В summary метода `AddGitHubBackupCore` надо добавить `<see cref="ITemporaryFolderCleaner"/>` в список сервисов.

`BackupOrchestrator.cs`:

```csharp
// primary constructor: replace `IFileSystem fileSystem,` with
    ITemporaryFolderCleaner temporaryFolderCleaner,

// <param> in the class summary: replace the fileSystem line with
/// <param name="temporaryFolderCleaner">Empties the folder of temporary archives.</param>

// in RunLockedAsync
        else
        {
            temporaryFolderCleaner.Clean(temporaryFolder);
        }
```

Из `BackupOrchestrator.cs` удаляются метод `EmptyTemporaryFolder` и `using System.IO.Abstractions;`. По прочитанному коду `fileSystem` в оркестраторе больше нигде не используется, но удалять `using` стоит только после сборки. Из `BackupOrchestratorLog.cs` удаляются записи с `EventId` 2, 3, 4 и 10. Запись `TemporaryFolderLeftUntouched` (EventId 205-й строки) остаётся: dry-run решает оркестратор.

Порядок «блокировка, затем очистка, затем discovery» не меняется. Комментарий над `TryAcquireAsync` («The lock is taken before…») остаётся на месте.

## Тесты

`src/GitHubBackup.Core.Tests/Runs/TemporaryFolderCleanerTests.cs`. Четыре сценария перенесены из `BackupOrchestratorTests` (502, 520, 529, 544). Добавлен один новый: папка остаётся на месте.

```csharp
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Runs;
using Microsoft.Extensions.Diagnostics.Logging.Testing; // namespace as in BackupOrchestratorTests
using Microsoft.Extensions.Logging;
using Shouldly;

namespace GitHubBackup.Core.Tests.Runs;

public sealed class TemporaryFolderCleanerTests
{
    private const string _folder = @"C:\sources\.tmp";
    private const int _entryDeletedEventId = 1;
    private const int _entryNotDeletedEventId = 2;
    private const int _folderIsLinkEventId = 4;

    private readonly MockFileSystem _fileSystem = new();
    private readonly FakeLogger<TemporaryFolderCleaner> _logger = new();

    private TemporaryFolderCleaner CreateCleaner() => new(_fileSystem, _logger);

    private FakeLogRecord SingleRecord(int eventId) =>
        _logger.Collector.GetSnapshot().Single(record => record.Id.Id == eventId);

    [Fact]
    public void Clean_FilesAndFolders_DeletesThemAndLogsEach()
    {
        _fileSystem.AddFile(Path.Combine(_folder, "a.zip"), new("partial"));
        _fileSystem.AddFile(Path.Combine(_folder, "nested", "b.zip"), new("partial"));

        CreateCleaner().Clean(_folder);

        _fileSystem.Directory.GetFileSystemEntries(_folder).ShouldBeEmpty();
        _logger.Collector.GetSnapshot().Count(record => record.Id.Id == _entryDeletedEventId).ShouldBe(2);
    }

    [Fact]
    public void Clean_FolderWithLeftovers_KeepsTheFolder()
    {
        _fileSystem.AddFile(Path.Combine(_folder, "a.zip"), new("partial"));

        CreateCleaner().Clean(_folder);

        _fileSystem.Directory.Exists(_folder).ShouldBeTrue();
    }

    [Fact]
    public void Clean_NoFolder_DoesNothing()
    {
        CreateCleaner().Clean(_folder);

        _fileSystem.Directory.Exists(_folder).ShouldBeFalse();
        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public void Clean_LeftoverCannotBeDeleted_LogsWarningAndDeletesTheOthers()
    {
        var locked = Path.Combine(_folder, "locked.zip");
        var other = Path.Combine(_folder, "other.zip");
        _fileSystem.AddFile(locked, new("partial") { AllowedFileShare = FileShare.None });
        _fileSystem.AddFile(other, new("partial"));

        CreateCleaner().Clean(_folder);

        _fileSystem.File.Exists(locked).ShouldBeTrue();
        _fileSystem.File.Exists(other).ShouldBeFalse();
        var record = SingleRecord(_entryNotDeletedEventId);
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(locked);
    }

    [Fact]
    public void Clean_FolderIsLink_LeavesItsContentAndLogsWarning()
    {
        var leftover = Path.Combine(_folder, "a.zip");
        _fileSystem.AddFile(leftover, new("partial"));
        _fileSystem.DirectoryInfo.New(_folder).Attributes |= FileAttributes.ReparsePoint;

        CreateCleaner().Clean(_folder);

        _fileSystem.File.Exists(leftover).ShouldBeTrue();
        var record = SingleRecord(_folderIsLinkEventId);
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(_folder);
    }
}
```

Точный `using` для `FakeLogger` надо скопировать из шапки `BackupOrchestratorTests.cs`: я её не читал.

**Что меняется в `BackupOrchestratorTests`:**
- Тесты 502, 520, 529 и 544 переходят в новый класс (см. выше).
- Тесты `RunAsync_LockHeld_…` (403), `RunAsync_LockTakenAfterTokenAndBeforeTemporaryFolderCleanup` (422), `RunAsync_TemporaryFolderEmptiedBeforeDiscovery` (1082) и `RunAsync_DryRun_LeavesTemporaryFolderUntouched` (1193) остаются. Они используют `ITemporaryFolderCleaner` через NSubstitute вместо проверки файлов. Например, для 1082: `_cleaner.When(c => c.Clean(_temporaryFolder)).Do(_ => order.Add("clean"))`, а в `DiscoverAsync` добавляется `"discover"`. Результат — `order.ShouldBe(["clean", "discover"])`. Для dry-run: `_cleaner.DidNotReceiveWithAnyArgs().Clean(default!)`.
- В конструкторе оркестратора в тестах `_fileSystem` заменяется на `_cleaner`. Места создания (около строк 114 и 652) я не читал.
- В `CoreServiceCollectionExtensionsTests` добавляется проверка, что `ITemporaryFolderCleaner` разрешается.

## Что не покрыто

Ветка «папку не удалось перечислить» (`FolderNotListed`) в существующих тестах не видна, и `MockFileSystem` плохо имитирует такую ошибку. Для неё нужен тест с подменой `IFileSystem` через NSubstitute, где `GetFileSystemEntries` бросает `IOException`. Могу дописать, если вынос нужен.

Вынос делать или оставить как есть? Если делать, подтвердите. Тогда я правки внесу, соберу и прогоню тесты. Коммит сделаю только по вашей прямой команде.
