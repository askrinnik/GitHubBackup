**Файлы не менялись.** Очистка `<sourcesRoot>\.tmp\` уже работает: её делает приватный метод `BackupOrchestrator.EmptyTemporaryFolder` (`BackupOrchestrator.cs:432`). Он вызывается после `runLock.TryAcquireAsync` и не вызывается в dry-run (FR-9.5). Я предлагаю вынести эту логику в отдельный сервис, чтобы тестировать её отдельно. Поведение остаётся тем же.

## Типы и файлы

```
src/GitHubBackup.Core/Runs/
  ITemporaryFolderCleaner.cs      new
  TemporaryFolderCleaner.cs       new (logic moved from BackupOrchestrator)
  TemporaryFolderCleanerLog.cs    new (4 log entries moved from BackupOrchestratorLog)
  BackupOrchestrator.cs           changed: IFileSystem -> ITemporaryFolderCleaner, private method removed
  BackupOrchestratorLog.cs        changed: entries 2, 3, 4, 10 removed
src/GitHubBackup.Core/CoreServiceCollectionExtensions.cs   changed: one registration
src/GitHubBackup.Core.Tests/Runs/
  TemporaryFolderCleanerTests.cs  new
  BackupOrchestratorTests.cs      changed
```

Сервис живёт в `Core`: он использует только `IFileSystem` и `ILogger`, как и старый метод.

### `ITemporaryFolderCleaner.cs`

```csharp
namespace GitHubBackup.Core.Runs;

/// <summary>
/// Deletes the files and folders that interrupted runs left in the folder of temporary archives.
/// </summary>
public interface ITemporaryFolderCleaner
{
    /// <summary>
    /// Empties a folder of temporary archives and keeps the folder itself.
    /// </summary>
    /// <remarks>
    /// The caller holds the run lock: only then no other run is writing an archive into the folder. An entry that
    /// cannot be deleted is logged and left for the next run. A folder that does not exist, cannot be listed or is a
    /// junction or a symbolic link is left alone.
    /// </remarks>
    /// <param name="folder">The folder of temporary archives.</param>
    /// <param name="cancellationToken">Stops the clean-up between two entries.</param>
    /// <exception cref="OperationCanceledException">The token is cancelled.</exception>
    void Clean(string folder, CancellationToken cancellationToken);
}
```

Метод синхронный, потому что `IFileSystem` не имеет async-операций. Токен проверяется между записями.

### `TemporaryFolderCleaner.cs`

```csharp
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Runs;

/// <summary>
/// Implements <see cref="ITemporaryFolderCleaner"/> over <see cref="IFileSystem"/>.
/// </summary>
/// <remarks>
/// The folder holds only archives under construction, so anything in it at the start of a run is garbage. An entry
/// that cannot be deleted does not prevent new archives, whose temporary names are unique. A folder that is a link is
/// left alone, because emptying it would delete files outside the sources root.
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
            Delete(entry);
        }
    }

    /// <summary>
    /// Deletes one file or folder; a failure is logged and the entry stays.
    /// </summary>
    /// <param name="entry">The file or folder.</param>
    private void Delete(string entry)
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
```

### `TemporaryFolderCleanerLog.cs`

Тексты сообщений те же, что в `BackupOrchestratorLog`. Event id свои, 1–4.

```csharp
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Runs;

/// <summary>
/// Defines the log entries of <see cref="TemporaryFolderCleaner"/>.
/// </summary>
internal static partial class TemporaryFolderCleanerLog
{
    /// <summary>
    /// Logs a file or folder left by an interrupted run and deleted.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The deleted file or folder.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Deleted the leftover temporary archive {Path}")]
    public static partial void EntryDeleted(ILogger logger, string path);

    /// <summary>
    /// Logs a leftover that cannot be deleted; the next run tries again.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The file or folder.</param>
    /// <param name="exception">The reason.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Cannot delete the leftover temporary archive {Path}")]
    public static partial void EntryNotDeleted(ILogger logger, string path, Exception exception);

    /// <summary>
    /// Logs that the folder cannot be listed and is not emptied.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The folder.</param>
    /// <param name="exception">The reason.</param>
    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Cannot list the temporary archive folder {Path}")]
    public static partial void FolderNotListed(ILogger logger, string path, Exception exception);

    /// <summary>
    /// Logs that the folder is a junction or a symbolic link and is not emptied.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The folder.</param>
    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "The temporary archive folder {Path} is a link and is not emptied")]
    public static partial void FolderIsLink(ILogger logger, string path);
}
```

## Регистрация и вызов

**Регистрация.** Одна строка в `AddGitHubBackupCore()` (`CoreServiceCollectionExtensions.cs`), рядом с остальными сервисами. Нужно также добавить `<see cref="ITemporaryFolderCleaner"/>` в `<summary>` метода. `IFileSystem` регистрирует `Infrastructure`, как и сейчас для оркестратора.

```csharp
services.TryAddSingleton<ITemporaryFolderCleaner, TemporaryFolderCleaner>();
```

**Вызов в `BackupOrchestrator`.** Параметр `IFileSystem fileSystem` заменяется на `ITemporaryFolderCleaner temporaryFolderCleaner`. Строка `<param name="fileSystem">` в XML-doc заменяется на `<param name="temporaryFolderCleaner">Empties the folder of temporary archives.</param>`. Директива `using System.IO.Abstractions` уходит, потому что `fileSystem` больше нигде в классе не нужен. Метод `EmptyTemporaryFolder` (строки 422–476) удаляется, а вызов в `RunLockedAsync` становится таким:

```csharp
else
{
    temporaryFolderCleaner.Clean(temporaryFolder, cancellationToken);
}
```

Порядок не меняется: блокировка → очистка → обнаружение. Если очистку отменят до первого репозитория, `OperationCanceledException` поймает `RunAsync`, как и сейчас.

Из `BackupOrchestratorLog` удаляются записи с EventId 2, 3, 4 и 10.

## Тесты

### `TemporaryFolderCleanerTests.cs` (новый)

```csharp
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Runs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GitHubBackup.Core.Tests.Runs;

/// <summary>
/// Tests <see cref="TemporaryFolderCleaner"/> over an in-memory file system.
/// </summary>
public sealed class TemporaryFolderCleanerTests
{
    private const string _folder = @"C:\sources\.tmp";

    private const int _entryDeletedEventId = 1;
    private const int _entryNotDeletedEventId = 2;
    private const int _folderNotListedEventId = 3;
    private const int _folderIsLinkEventId = 4;

    private readonly MockFileSystem _fileSystem = new();
    private readonly FakeLogger<TemporaryFolderCleaner> _logger = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public void Clean_FilesAndSubfolder_DeletesThemAndKeepsFolder()
    {
        _fileSystem.AddFile(Path.Combine(_folder, "a.zip"), new("partial"));
        _fileSystem.AddFile(Path.Combine(_folder, "nested", "b.zip"), new("partial"));

        CreateCleaner().Clean(_folder, CancellationToken);

        _fileSystem.Directory.Exists(_folder).ShouldBeTrue();
        _fileSystem.Directory.GetFileSystemEntries(_folder).ShouldBeEmpty();
        Records(_entryDeletedEventId).Select(record => record.GetStructuredStateValue("Path")).ShouldBe(
            [Path.Combine(_folder, "a.zip"), Path.Combine(_folder, "nested")], ignoreOrder: true);
    }

    [Fact]
    public void Clean_FolderMissing_DoesNothingAndDoesNotCreateIt()
    {
        CreateCleaner().Clean(_folder, CancellationToken);

        _fileSystem.Directory.Exists(_folder).ShouldBeFalse();
        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public void Clean_EmptyFolder_LogsNothing()
    {
        _fileSystem.AddDirectory(_folder);

        CreateCleaner().Clean(_folder, CancellationToken);

        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public void Clean_FileCannotBeDeleted_LogsWarningKeepsItAndDeletesTheRest()
    {
        var locked = Path.Combine(_folder, "locked.zip");
        var other = Path.Combine(_folder, "other.zip");
        _fileSystem.AddFile(locked, new("partial") { AllowedFileShare = FileShare.None });
        _fileSystem.AddFile(other, new("partial"));

        CreateCleaner().Clean(_folder, CancellationToken);

        _fileSystem.File.Exists(locked).ShouldBeTrue();
        _fileSystem.File.Exists(other).ShouldBeFalse();
        var record = Records(_entryNotDeletedEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(locked);
    }

    [Fact]
    public void Clean_FolderIsLink_LeavesContentAndLogsWarning()
    {
        var leftover = Path.Combine(_folder, "a.zip");
        _fileSystem.AddFile(leftover, new("partial"));
        _fileSystem.DirectoryInfo.New(_folder).Attributes |= FileAttributes.ReparsePoint;

        CreateCleaner().Clean(_folder, CancellationToken);

        _fileSystem.File.Exists(leftover).ShouldBeTrue();
        var record = Records(_folderIsLinkEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(_folder);
    }

    [Fact]
    public void Clean_FolderCannotBeListed_LogsWarningAndReturns()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.Directory.Exists(_folder).Returns(true);
        fileSystem.DirectoryInfo.New(_folder).Attributes.Returns(FileAttributes.Directory);
        fileSystem.Directory.GetFileSystemEntries(_folder).Throws(new UnauthorizedAccessException("denied"));

        new TemporaryFolderCleaner(fileSystem, _logger).Clean(_folder, CancellationToken);

        var record = Records(_folderNotListedEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(_folder);
        fileSystem.File.DidNotReceiveWithAnyArgs().Delete(default!);
    }

    [Fact]
    public void Clean_Cancelled_ThrowsAndDeletesNothing()
    {
        var leftover = Path.Combine(_folder, "a.zip");
        _fileSystem.AddFile(leftover, new("partial"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Should.Throw<OperationCanceledException>(() => CreateCleaner().Clean(_folder, cancellation.Token));

        _fileSystem.File.Exists(leftover).ShouldBeTrue();
    }

    private TemporaryFolderCleaner CreateCleaner() => new(_fileSystem, _logger);

    private IReadOnlyList<FakeLogRecord> Records(int eventId) =>
        [.. _logger.Collector.GetSnapshot().Where(record => record.Id.Id == eventId)];
}
```

### Что меняется в `BackupOrchestratorTests.cs`

- Вместо `MockFileSystem` в тестах появляется `Substitute.For<ITemporaryFolderCleaner>()`. Оркестратор создаётся с ним.
- Удаляются три теста: `RunAsync_LeftoversInTemporaryFolder_DeletedBeforeFirstRepository`, `RunAsync_LeftoverCannotBeDeleted_LogsWarningAndContinues` и `RunAsync_TemporaryFolderIsLink_LeavesItsContentAndContinues`. Их проверки перенесены в `TemporaryFolderCleanerTests`. Константы `_temporaryEntryDeletedEventId`, `_temporaryEntryNotDeletedEventId` и `_temporaryFolderIsLinkEventId` тоже удаляются.
- Тесты `RunAsync_LockHeld_ReturnsCriticalWithoutTemporaryFolderCleanupOrDiscovery`, `RunAsync_LockTakenAfterTokenAndBeforeTemporaryFolderCleanup`, `RunAsync_TemporaryFolderEmptiedBeforeDiscovery`, `RunAsync_DryRun_LeavesTemporaryFolderUntouched` и `RunAsync_NoRepositories_ReturnsSuccessAndEmptiesTemporaryFolder` переписываются на проверки вызова подставного сервиса. Я их целиком не читал, поэтому точные правки покажу после чтения. Ожидаемые проверки такие:

```csharp
// normal run: called once, with the resolved folder, after the lock and before discovery
_cleaner.Received(1).Clean(_temporaryFolder, Arg.Any<CancellationToken>());

// lock held, and dry run: never called
_cleaner.DidNotReceiveWithAnyArgs().Clean(default!, default);
```

Для порядка «блокировка → очистка → обнаружение» в этих тестах `Returns`/`Do` у подставных `_runLock`, `_cleaner` и `_discovery` пишут события в общий список. Тест сравнивает этот список с ожидаемым порядком, как уже делает `RunAsync_LockTakenAfterTokenAndBeforeTemporaryFolderCleanup`.

- В `CoreServiceCollectionExtensionsTests` нужен случай, что `ITemporaryFolderCleaner` разрешается в `TemporaryFolderCleaner`. Как сейчас устроены остальные случаи, я не смотрел.
- Интеграционный тест `RunAsync_LeftoverInTemporaryFolder_IsDeletedAtStart` остаётся как есть. Он проверяет связку с реальной файловой системой.

## Что нужно решить

1. **PRD.** Новых требований нет: FR-7.4, FR-9.5 и §9 п. 4 уже описывают это поведение. В таблицу §8.2 можно добавить строку `ITemporaryFolderCleaner`. Это правка PRD, поэтому по правилу «PRD first» она нужна только с вашего одобрения (версия и строка в истории изменений).
2. **Контракт блокировки.** Сервис сам не проверяет, что блокировка взята: об этом говорит только `<remarks>`. Можно сделать проверку жёстче, например передавать в `Clean` дескриптор блокировки. Это усложняет API, поэтому я не стал.
3. **Ветка, issue и коммит.** Я их не создавал и не буду без вашего указания. Если нужно, подготовлю план `docs/plans/…` или внесу эти правки.
