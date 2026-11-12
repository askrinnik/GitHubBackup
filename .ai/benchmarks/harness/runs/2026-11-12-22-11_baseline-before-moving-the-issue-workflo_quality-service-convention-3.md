**Очистка `.tmp` уже работает.** Она сидит внутри `BackupOrchestrator`: приватный метод `EmptyTemporaryFolder` (`BackupOrchestrator.cs:432`) вызывается после захвата блокировки. Тесты на неё тоже есть. Поэтому «добавить сервис» здесь значит вынести этот метод в отдельный тип. Поведение не меняется, так что PRD менять не нужно. Ничего не компилировал и не запускал, файлы не правил.

## Что где лежит

```
src/GitHubBackup.Core/
  Runs/ITemporaryFolderCleaner.cs        new
  Runs/TemporaryFolderCleaner.cs         new (the body of EmptyTemporaryFolder)
  Runs/TemporaryFolderCleanerLog.cs      new (4 messages moved from BackupOrchestratorLog)
  Runs/BackupOrchestrator.cs             edit: inject the cleaner, drop IFileSystem and the private method
  Runs/BackupOrchestratorLog.cs          edit: remove EventIds 2, 3, 4, 10
  CoreServiceCollectionExtensions.cs     edit: one registration line
src/GitHubBackup.Core.Tests/Runs/
  TemporaryFolderCleanerTests.cs         new
  BackupOrchestratorTests.cs             edit: use a substitute cleaner
```

Сервис живёт в `Core`, потому что работает только через `IFileSystem`. Слои не нарушаются.

## Новые типы

```csharp
// ITemporaryFolderCleaner.cs
namespace GitHubBackup.Core.Runs;

/// <summary>
/// Deletes the files that interrupted runs left in the folder of temporary archives.
/// </summary>
public interface ITemporaryFolderCleaner
{
    /// <summary>
    /// Deletes every file and folder in <paramref name="folder"/>, leaving the folder itself.
    /// </summary>
    /// <remarks>
    /// The caller holds the run lock, so no other run writes to the folder. An entry that cannot be deleted is
    /// logged and left for the next run; a folder that is a junction or a symbolic link is not touched.
    /// </remarks>
    /// <param name="folder">The folder of temporary archives.</param>
    /// <param name="cancellationToken">Cancels the clean-up between two entries.</param>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
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
/// The folder holds only archives under construction, so anything in it after the lock is taken is garbage. A
/// failed deletion does not block new archives, because their temporary names are unique. Emptying a junction
/// would delete files outside the sources root, so a link is left alone.
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
    /// Deletes one file or folder and logs the result; a failure leaves the entry for the next run.
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

```csharp
// TemporaryFolderCleanerLog.cs
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Runs;

/// <summary>
/// Defines the log entries of <see cref="TemporaryFolderCleaner"/>.
/// </summary>
internal static partial class TemporaryFolderCleanerLog
{
    /// <summary>Logs that a leftover was deleted.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The deleted file or folder.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Deleted the leftover temporary archive {Path}")]
    public static partial void EntryDeleted(ILogger logger, string path);

    /// <summary>Logs that a leftover could not be deleted and stays for the next run.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The file or folder.</param>
    /// <param name="exception">The reason.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Cannot delete the leftover temporary archive {Path}")]
    public static partial void EntryNotDeleted(ILogger logger, string path, Exception exception);

    /// <summary>Logs that the folder could not be listed.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The folder.</param>
    /// <param name="exception">The reason.</param>
    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Cannot list the temporary archive folder {Path}")]
    public static partial void FolderNotListed(ILogger logger, string path, Exception exception);

    /// <summary>Logs that the folder is a junction or a symbolic link and is not emptied.</summary>
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

В `AddGitHubBackupCore()` одна строка рядом с остальными. Нужно также добавить `ITemporaryFolderCleaner` в `<summary>` метода.

```csharp
services.TryAddSingleton<ITemporaryFolderCleaner, TemporaryFolderCleaner>();
```

В `BackupOrchestrator` меняется конструктор: `IFileSystem fileSystem` заменяется на `ITemporaryFolderCleaner temporaryFolderCleaner`. Это единственное место, где оркестратор использовал `IFileSystem`, поэтому `using System.IO.Abstractions` тоже уходит. Приватный метод `EmptyTemporaryFolder` удаляется, а `<param>` в XML-doc обновляется.

```csharp
if (dryRun)
{
    BackupOrchestratorLog.DryRunStarted(logger);
    BackupOrchestratorLog.TemporaryFolderLeftUntouched(logger, temporaryFolder);
}
else
{
    temporaryFolderCleaner.Clean(temporaryFolder, cancellationToken);
}
```

Место вызова прежнее: в `RunLockedAsync`, после `runLock.TryAcquireAsync`, до discovery. Dry-run по-прежнему не чистит папку (FR-9.5), и это решение остаётся в оркестраторе.

Конструктор `BackupOrchestrator` вызывается напрямую в `Core.Tests/Runs/BackupOrchestratorTests.cs`, в `Infrastructure.IntegrationTests/Runs/BackupOrchestratorTests.cs` и, возможно, ещё где-то. Я проверил только список файлов, где встречается `RunLock`. Эти вызовы нужно поправить. `CoreServiceCollectionExtensionsTests` стоит дополнить проверкой, что `ITemporaryFolderCleaner` разрешается.

## Тесты

Новый класс `TemporaryFolderCleanerTests` в `Core.Tests/Runs`. Он использует `MockFileSystem` и `FakeLogger`, как требует `tests.md`.

```csharp
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Runs;
using Microsoft.Extensions.Diagnostics.Testing;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace GitHubBackup.Core.Tests.Runs;

/// <summary>
/// Tests <see cref="TemporaryFolderCleaner"/> with an in-memory file system.
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
    public void Clean_FolderMissing_DoesNothing()
    {
        CreateCleaner().Clean(_folder, CancellationToken);

        _fileSystem.Directory.Exists(_folder).ShouldBeFalse();
        Records(_entryDeletedEventId).ShouldBeEmpty();
    }

    [Fact]
    public void Clean_FilesAndNestedFolder_DeletesAllAndKeepsFolder()
    {
        _fileSystem.AddFile(Path.Combine(_folder, "a.zip"), new("partial"));
        _fileSystem.AddFile(Path.Combine(_folder, "nested", "b.zip"), new("partial"));

        CreateCleaner().Clean(_folder, CancellationToken);

        _fileSystem.Directory.Exists(_folder).ShouldBeTrue();
        _fileSystem.Directory.GetFileSystemEntries(_folder).ShouldBeEmpty();
        Records(_entryDeletedEventId).Count.ShouldBe(2);
    }

    [Fact]
    public void Clean_FileCannotBeDeleted_LogsWarningAndDeletesTheOthers()
    {
        var locked = Path.Combine(_folder, "locked.zip");
        var free = Path.Combine(_folder, "free.zip");
        _fileSystem.AddFile(locked, new("partial") { AllowedFileShare = FileShare.None });
        _fileSystem.AddFile(free, new("partial"));

        CreateCleaner().Clean(_folder, CancellationToken);

        _fileSystem.File.Exists(locked).ShouldBeTrue();
        _fileSystem.File.Exists(free).ShouldBeFalse();
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
        fileSystem.Directory.GetFileSystemEntries(_folder).Throws(new IOException("denied"));

        new TemporaryFolderCleaner(fileSystem, _logger).Clean(_folder, CancellationToken);

        var record = Records(_folderNotListedEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(_folder);
    }

    [Fact]
    public void Clean_TokenCancelled_ThrowsAndLeavesEntries()
    {
        var leftover = Path.Combine(_folder, "a.zip");
        _fileSystem.AddFile(leftover, new("partial"));
        using var source = new CancellationTokenSource();
        source.Cancel();

        Should.Throw<OperationCanceledException>(() => CreateCleaner().Clean(_folder, source.Token));

        _fileSystem.File.Exists(leftover).ShouldBeTrue();
    }

    private TemporaryFolderCleaner CreateCleaner() => new(_fileSystem, _logger);

    private IReadOnlyList<FakeLogRecord> Records(int eventId) =>
        [.. _logger.Collector.GetSnapshot().Where(record => record.Id.Id == eventId)];
}
```

В `BackupOrchestratorTests` вместо `_fileSystem` появляется `Substitute.For<ITemporaryFolderCleaner>()`.

- **Переезжают в `TemporaryFolderCleanerTests`:** проверки удаления, логов со вложенной папкой, заблокированного файла и ссылки. Это тесты вокруг строк 272–281, 502–557 и 1082. Оставлять их в оркестраторе нельзя: event id теперь живут в другом логгере.
- **Остаются в оркестраторе:** тесты порядка и условий вызова.

```csharp
[Fact]
public async Task RunAsync_LockAcquired_CleansTemporaryFolderAfterLockAndBeforeDiscovery()
{
    var calls = new List<string>();
    _runLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns(_ =>
    {
        calls.Add("lock");
        return _runLockHandle;
    });
    _cleaner.When(cleaner => cleaner.Clean(Arg.Any<string>(), Arg.Any<CancellationToken>()))
        .Do(_ => calls.Add("clean"));
    _discovery.DiscoverAsync(Arg.Any<BackupConfig>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(_ =>
    {
        calls.Add("discover");
        return DiscoveryResult.Empty;
    });

    await RunAsync();

    calls.ShouldBe(["lock", "clean", "discover"]);
    _cleaner.Received(1).Clean(_temporaryFolder, Arg.Any<CancellationToken>());
}
```

Ещё два теста в том же классе:
- **Блокировка занята.** `Clean` не вызывается (бывший тест на строке 403).
- **Dry-run.** `Clean` не вызывается, а запись с id 19 по-прежнему логируется (бывший тест на строке 1193).

## Что нужно решить

1. **Отмена между файлами.** Я добавил `cancellationToken.ThrowIfCancellationRequested()` в цикл, чего в старом методе не было. Правило `csharp.md` требует передавать токен вниз, а оркестратор уже превращает `OperationCanceledException` из `RunLockedAsync` в результат «отменён». Если нужен строгий перенос без изменений, уберите токен из интерфейса и тест `Clean_TokenCancelled_ThrowsAndLeavesEntries`.
2. **Метод синхронный.** `IFileSystem` не имеет асинхронного `Delete`, так что `Task` здесь был бы фиктивным. Это отступление от правила «I/O — async», и я оставил его сознательно.
3. **Риск в тесте листинга.** `Clean_FolderCannotBeListed…` строится на автоподстановках NSubstitute для `IFileSystem`. Если `DirectoryInfo.New(_folder)` вернёт разные подстановки на разных вызовах, придётся задать `IDirectoryInfo` явно. Проверить это можно только сборкой.

Если подход подходит, скажите, и я внесу правки. Коммит без явной просьбы делать не буду.
