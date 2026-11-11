**Кратко:** такая очистка в коде уже есть. Это приватный метод `BackupOrchestrator.EmptyTemporaryFolder` (`BackupOrchestrator.cs:243`), и `RunAsync` вызывает его на строке 95. Тесты на него есть в `BackupOrchestratorTests`. Поэтому «добавить сервис» здесь значит вынести метод из оркестратора в отдельный сервис. Поведение при этом не меняется. Файлы я не менял.

## Блокировка запуска пока не реализована

Блокировки запуска (NFR-SYS-1) в коде нет. Поиск по `Mutex`, `RunLock` и `AlreadyRunning` ничего не нашёл. Поэтому сейчас очистка стоит после получения токена и перед обнаружением (discovery). Это порядок из §9 PRD, но без блокировки.

Новый сервис нужно вызвать сразу после захвата блокировки. Пока блокировки нет, вызов остаётся на том же месте. Захват блокировки — это отдельная задача. Если вы хотите включить её сюда, скажите. Изменений PRD не требуется: FR-7.4 и §9 уже описывают это поведение.

## Типы и файлы (`GitHubBackup.Core`, папка `Runs/`)

```
Runs/ITemporaryFolderCleaner.cs
Runs/TemporaryFolderCleaner.cs
Runs/TemporaryFolderCleanerLog.cs
```

Сервис лежит в `Core`, как `CloneRelocator`: он ходит на диск только через `IFileSystem`. Метод синхронный, потому что `IFileSystem` синхронный. Токен отмены он проверяет между записями.

```csharp
// ITemporaryFolderCleaner.cs
namespace GitHubBackup.Core.Runs;

/// <summary>
/// Clears the folder of temporary archives of the files that interrupted runs left behind.
/// </summary>
/// <remarks>
/// The caller holds the run lock, so no other run writes to the folder while it is cleared.
/// </remarks>
public interface ITemporaryFolderCleaner
{
    /// <summary>
    /// Deletes the files and folders in <paramref name="folder"/>. An entry that cannot be deleted is logged and
    /// left for the next run.
    /// </summary>
    /// <param name="folder">The folder of temporary archives, <c>&lt;sourcesRoot&gt;\.tmp</c>.</param>
    /// <param name="cancellationToken">Stops the clean-up between two entries.</param>
    /// <exception cref="OperationCanceledException">The clean-up was cancelled.</exception>
    void Clean(string folder, CancellationToken cancellationToken);
}
```

```csharp
// TemporaryFolderCleaner.cs
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Runs;

/// <inheritdoc />
/// <remarks>
/// The folder holds only archives under construction, so anything in it at the start of a run is garbage. A
/// leftover that cannot be deleted does not prevent new archives, whose temporary names are unique. A folder that is
/// a junction or a symbolic link is left alone, because emptying it would delete files outside the sources root.
/// </remarks>
/// <param name="fileSystem">Lists and deletes the entries.</param>
/// <param name="logger">The logger.</param>
public sealed class TemporaryFolderCleaner(IFileSystem fileSystem, ILogger<TemporaryFolderCleaner> logger)
    : ITemporaryFolderCleaner
{
    /// <inheritdoc />
    public void Clean(string folder, CancellationToken cancellationToken)
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
            cancellationToken.ThrowIfCancellationRequested();
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

```csharp
// TemporaryFolderCleanerLog.cs
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

    /// <summary>Logs a leftover that cannot be deleted; the next run tries again.</summary>
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

В `CoreServiceCollectionExtensions.AddGitHubBackupCore` добавляется одна строка. В `<summary>` метода добавляется `<see cref="ITemporaryFolderCleaner"/>`.

```csharp
services.TryAddSingleton<ITemporaryFolderCleaner, TemporaryFolderCleaner>();
```

`IFileSystem` регистрирует `AddGitHubBackupInfrastructure`, так что хосты ничего не меняют.

В `BackupOrchestrator`:
- В основной конструктор добавляется параметр `ITemporaryFolderCleaner temporaryFolderCleaner` и его `<param>`.
- Строка 95 меняется на `temporaryFolderCleaner.Clean(temporaryFolder, cancellationToken);`.
- Метод `EmptyTemporaryFolder` (строки 233–287) удаляется.
- Из `BackupOrchestratorLog` удаляются записи с `EventId` 2, 3, 4 и 10.
- Параметр `IFileSystem fileSystem` удаляется, если после этого он нигде не используется. Это я не проверял.

Когда появится блокировка, вызов `Clean` встанет сразу после её захвата.

## Тесты

**`CoreServiceCollectionExtensionsTests`.** Добавляется проверка `provider.GetRequiredService<ITemporaryFolderCleaner>().ShouldBeOfType<TemporaryFolderCleaner>()` рядом с проверкой оркестратора (строка 122).

**`BackupOrchestratorTests`.**
- В тестах подставляется `Substitute.For<ITemporaryFolderCleaner>()`.
- Четыре теста про `.tmp` (строки 264, 340, 358, 366, 382) переезжают в новый класс. Их `EventId`-константы удаляются.
- Остаётся один тест: `Clean` вызван с `_temporaryFolder`, и вызов произошёл до `DiscoverAsync` и до `DetectAsync`. Порядок проверяется так же, как в существующем тесте на строке 908. Ещё один тест: при ошибке конфигурации или инструментов `Clean` не вызывается (сейчас это проверяют тесты со строк 277 и 296).

**`Core.Tests/Runs/TemporaryFolderCleanerTests.cs`:**

```csharp
using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Runs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace GitHubBackup.Core.Tests.Runs;

/// <summary>
/// Tests <see cref="TemporaryFolderCleaner"/>.
/// </summary>
public sealed class TemporaryFolderCleanerTests
{
    private const string _folder = @"C:\sources\.tmp";
    private const int _entryDeletedEventId = 1;
    private const int _entryNotDeletedEventId = 2;
    private const int _folderIsLinkEventId = 4;

    private readonly MockFileSystem _fileSystem = new();
    private readonly FakeLogger<TemporaryFolderCleaner> _logger = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Clean_LeftoverFilesAndFolders_DeletesThemAndKeepsTheFolder()
    {
        _fileSystem.AddFile(Path.Combine(_folder, "a.zip"), new("partial"));
        _fileSystem.AddFile(Path.Combine(_folder, "nested", "b.zip"), new("partial"));

        Cleaner().Clean(_folder, Token);

        _fileSystem.Directory.Exists(_folder).ShouldBeTrue();
        _fileSystem.Directory.GetFileSystemEntries(_folder).ShouldBeEmpty();
        Records(_entryDeletedEventId).Count.ShouldBe(2);
    }

    [Fact]
    public void Clean_NoFolder_DoesNothing()
    {
        Cleaner().Clean(_folder, Token);

        _fileSystem.Directory.Exists(_folder).ShouldBeFalse();
        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public void Clean_EmptyFolder_LogsNothing()
    {
        _fileSystem.AddDirectory(_folder);

        Cleaner().Clean(_folder, Token);

        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public void Clean_LeftoverCannotBeDeleted_LogsWarningAndDeletesTheRest()
    {
        var locked = Path.Combine(_folder, "locked.zip");
        var other = Path.Combine(_folder, "other.zip");
        _fileSystem.AddFile(locked, new("partial") { AllowedFileShare = FileShare.None });
        _fileSystem.AddFile(other, new("partial"));

        Cleaner().Clean(_folder, Token);

        _fileSystem.File.Exists(locked).ShouldBeTrue();
        _fileSystem.File.Exists(other).ShouldBeFalse();
        var record = Records(_entryNotDeletedEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(locked);
    }

    [Fact]
    public void Clean_FolderIsLink_LeavesItsContent()
    {
        var leftover = Path.Combine(_folder, "a.zip");
        _fileSystem.AddFile(leftover, new("partial"));
        _fileSystem.DirectoryInfo.New(_folder).Attributes |= FileAttributes.ReparsePoint;

        Cleaner().Clean(_folder, Token);

        _fileSystem.File.Exists(leftover).ShouldBeTrue();
        var record = Records(_folderIsLinkEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(_folder);
    }

    [Fact]
    public void Clean_Cancelled_ThrowsAndDeletesNothing()
    {
        var leftover = Path.Combine(_folder, "a.zip");
        _fileSystem.AddFile(leftover, new("partial"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Should.Throw<OperationCanceledException>(() => Cleaner().Clean(_folder, cancellation.Token));

        _fileSystem.File.Exists(leftover).ShouldBeTrue();
    }

    private TemporaryFolderCleaner Cleaner() => new(_fileSystem, _logger);

    private List<FakeLogRecord> Records(int eventId) =>
        [.. _logger.Collector.GetSnapshot().Where(record => record.Id.Id == eventId)];
}
```

Тесты я не запускал. Для события «папку нельзя перечислить» (`FolderNotListed`) теста нет: `MockFileSystem` не умеет такой сбой без собственной обёртки. Сейчас этот путь тоже ничем не покрыт.

## Что решить

- Вынести очистку в сервис, как выше? Это чистый рефакторинг без новой функциональности. Если хотите, чтобы я реализовал, скажите об этом.
- Делать ли блокировку запуска в этой же задаче? Я бы сделал её отдельной задачей, потому что это тот же самый NFR-SYS-1 с кодом возврата `2`.
