**Ответ:** по FR-7.4 очистка `.tmp` уже работает. Она живёт в приватном методе `BackupOrchestrator.EmptyTemporaryFolder` (`BackupOrchestrator.cs:923`). Вызов стоит после захвата `runLock` и не выполняется в dry-run (FR-9.5). Поэтому ниже я выношу эту логику в отдельный сервис, а не пишу новую. PRD менять не нужно. Файлы я не правил.

Я не читал строки 1221–1468 оркестратора и конструктор тестового класса `BackupOrchestratorTests` (около строки 129). Там нужна правка для нового параметра конструктора.

## Типы и файлы

```
src/GitHubBackup.Core/Archiving/
  ITemporaryArchiveCleaner.cs        новый
  TemporaryArchiveCleaner.cs         новый
  TemporaryCleanupResult.cs          новый
  TemporaryArchiveCleanerLog.cs      новый
src/GitHubBackup.Core/Runs/
  BackupOrchestrator.cs              правка: параметр конструктора, вызов, удалить приватный метод
  BackupOrchestratorLog.cs           правка: удалить записи 2, 3, 4 и 10
src/GitHubBackup.Core/CoreServiceCollectionExtensions.cs   правка: регистрация
src/GitHubBackup.Core.Tests/Archiving/TemporaryArchiveCleanerTests.cs  новый
```

`ITemporaryArchiveCleaner.cs`:
```csharp
namespace GitHubBackup.Core.Archiving;

/// <summary>
/// Deletes the files that interrupted runs left in the folder of temporary archives.
/// </summary>
public interface ITemporaryArchiveCleaner
{
    /// <summary>
    /// Deletes every file and folder inside <paramref name="folder"/> and keeps the folder itself.
    /// </summary>
    /// <remarks>
    /// The caller holds the run lock, so no other run writes to the folder. An entry that cannot be deleted is
    /// logged and left for the next run; it does not stop the clean-up.
    /// </remarks>
    /// <param name="folder">The folder of temporary archives.</param>
    /// <param name="cancellationToken">Stops the clean-up before the next entry.</param>
    /// <returns>How many entries were deleted and how many stay.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    TemporaryCleanupResult Clean(string folder, CancellationToken cancellationToken);
}
```

`TemporaryCleanupResult.cs`:
```csharp
namespace GitHubBackup.Core.Archiving;

/// <summary>
/// Describes what <see cref="ITemporaryArchiveCleaner"/> did.
/// </summary>
/// <param name="Deleted">The number of deleted files and folders.</param>
/// <param name="Left">The number of entries that stay because they cannot be deleted.</param>
/// <param name="FolderSkipped">
/// <see langword="true"/> when the whole folder was left alone because it is a link or cannot be listed.
/// </param>
public sealed record TemporaryCleanupResult(int Deleted, int Left, bool FolderSkipped)
{
    /// <summary>The result for a folder that does not exist.</summary>
    public static TemporaryCleanupResult Nothing { get; } = new(0, 0, false);
}
```

`TemporaryArchiveCleaner.cs`:
```csharp
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Archiving;

/// <summary>
/// Implements <see cref="ITemporaryArchiveCleaner"/> over <see cref="IFileSystem"/>.
/// </summary>
/// <remarks>
/// A folder that is a junction or a symbolic link is left alone, because emptying it would delete files outside the
/// sources root.
/// </remarks>
/// <param name="fileSystem">Lists and deletes the entries.</param>
/// <param name="logger">Logs each deleted entry, each entry that stays and a skipped folder.</param>
public sealed class TemporaryArchiveCleaner(
    IFileSystem fileSystem,
    ILogger<TemporaryArchiveCleaner> logger) : ITemporaryArchiveCleaner
{
    /// <inheritdoc />
    public TemporaryCleanupResult Clean(string folder, CancellationToken cancellationToken)
    {
        if (!fileSystem.Directory.Exists(folder))
        {
            return TemporaryCleanupResult.Nothing;
        }

        if (fileSystem.DirectoryInfo.New(folder).Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            TemporaryArchiveCleanerLog.FolderIsLink(logger, folder);
            return new(0, 0, true);
        }

        string[] entries;
        try
        {
            entries = fileSystem.Directory.GetFileSystemEntries(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TemporaryArchiveCleanerLog.FolderNotListed(logger, folder, exception);
            return new(0, 0, true);
        }

        var (deleted, left) = (0, 0);
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

                TemporaryArchiveCleanerLog.EntryDeleted(logger, entry);
                deleted++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                TemporaryArchiveCleanerLog.EntryNotDeleted(logger, entry, exception);
                left++;
            }
        }

        return new(deleted, left, false);
    }
}
```

`TemporaryArchiveCleanerLog.cs` (тексты те же, что в оркестраторе; EventId свои, начиная с 1):
```csharp
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Archiving;

/// <summary>
/// Defines the log entries of <see cref="TemporaryArchiveCleaner"/>.
/// </summary>
internal static partial class TemporaryArchiveCleanerLog
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

    /// <summary>Logs that the folder cannot be listed, so it is not emptied.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The folder.</param>
    /// <param name="exception">The reason.</param>
    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Cannot list the temporary archive folder {Path}")]
    public static partial void FolderNotListed(ILogger logger, string path, Exception exception);

    /// <summary>Logs that the folder is a link, so it is not emptied.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The folder.</param>
    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "The temporary archive folder {Path} is a link and is not emptied")]
    public static partial void FolderIsLink(ILogger logger, string path);
}
```

## Регистрация и вызов

`CoreServiceCollectionExtensions.cs`: добавить строку рядом с `IArchiveNameBuilder` и дополнить `<summary>` (`<see cref="ITemporaryArchiveCleaner"/>`):
```csharp
services.TryAddSingleton<ITemporaryArchiveCleaner, TemporaryArchiveCleaner>();
```

`BackupOrchestrator.cs`:
1. Добавить параметр `ITemporaryArchiveCleaner temporaryCleaner` после `IFileSystem fileSystem`. Добавить `/// <param name="temporaryCleaner">Empties the folder of temporary archives.</param>`. У `fileSystem` в `<param>` убрать слова «Empties the folder of temporary archives»: оркестратор всё ещё использует его в `IsSameFolder`, `IsInside` и `IsReparsePoint`.
2. В `RunLockedAsync` (строки 236–239) заменить `EmptyTemporaryFolder(temporaryFolder);` на:
```csharp
temporaryCleaner.Clean(temporaryFolder, cancellationToken);
```
Условие `if (dryRun) … else` не меняется. Порядок тоже: вызов остаётся после `runLock.TryAcquireAsync` и до сброса ID и обнаружения. Это требует FR-7.4 и шаг 4 раздела 9.
3. Удалить приватный `EmptyTemporaryFolder` (строки 913–967).

`BackupOrchestratorLog.cs`: удалить `TemporaryEntryDeleted`, `TemporaryEntryNotDeleted`, `TemporaryFolderNotListed`, `TemporaryFolderIsLink`. `TemporaryFolderLeftUntouched` остаётся.

## Тесты

`src/GitHubBackup.Core.Tests/Archiving/TemporaryArchiveCleanerTests.cs`:
```csharp
using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Archiving;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace GitHubBackup.Core.Tests.Archiving;

/// <summary>
/// Tests <see cref="TemporaryArchiveCleaner"/> over a <see cref="MockFileSystem"/>.
/// </summary>
public sealed class TemporaryArchiveCleanerTests
{
    private const int _entryDeletedEventId = 1;

    private const int _entryNotDeletedEventId = 2;

    private const int _folderIsLinkEventId = 4;

    private const string _folder = @"C:\sources\.tmp";

    private readonly MockFileSystem _fileSystem = new();

    private readonly FakeLogger<TemporaryArchiveCleaner> _logger = new();

    private readonly TemporaryArchiveCleaner _cleaner;

    /// <summary>
    /// Creates the cleaner over an empty file system.
    /// </summary>
    public TemporaryArchiveCleanerTests() => _cleaner = new(_fileSystem, _logger);

    [Fact]
    public void Clean_FilesAndFolders_DeletesAllAndKeepsFolder()
    {
        _fileSystem.AddFile(Path.Combine(_folder, "a.zip"), new("partial"));
        _fileSystem.AddFile(Path.Combine(_folder, "nested", "b.zip"), new("partial"));

        var result = _cleaner.Clean(_folder, TestContext.Current.CancellationToken);

        result.ShouldBe(new(2, 0, false));
        _fileSystem.Directory.Exists(_folder).ShouldBeTrue();
        _fileSystem.Directory.GetFileSystemEntries(_folder).ShouldBeEmpty();
        Records(_entryDeletedEventId).Count.ShouldBe(2);
    }

    [Fact]
    public void Clean_FolderMissing_ReturnsNothingAndCreatesNoFolder()
    {
        var result = _cleaner.Clean(_folder, TestContext.Current.CancellationToken);

        result.ShouldBe(TemporaryCleanupResult.Nothing);
        _fileSystem.Directory.Exists(_folder).ShouldBeFalse();
    }

    [Fact]
    public void Clean_EmptyFolder_ReturnsNothing()
    {
        _fileSystem.AddDirectory(_folder);

        _cleaner.Clean(_folder, TestContext.Current.CancellationToken).ShouldBe(TemporaryCleanupResult.Nothing);
    }

    [Fact]
    public void Clean_FileHeldByAnotherProcess_LeavesItWarnsAndDeletesTheRest()
    {
        var locked = Path.Combine(_folder, "locked.zip");
        _fileSystem.AddFile(locked, new("partial") { AllowedFileShare = FileShare.None });
        _fileSystem.AddFile(Path.Combine(_folder, "free.zip"), new("partial"));

        var result = _cleaner.Clean(_folder, TestContext.Current.CancellationToken);

        result.ShouldBe(new(1, 1, false));
        _fileSystem.File.Exists(locked).ShouldBeTrue();
        var record = Records(_entryNotDeletedEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(locked);
    }

    [Fact]
    public void Clean_FolderIsLink_LeavesContentAndWarns()
    {
        var leftover = Path.Combine(_folder, "a.zip");
        _fileSystem.AddFile(leftover, new("partial"));
        _fileSystem.DirectoryInfo.New(_folder).Attributes |= FileAttributes.ReparsePoint;

        var result = _cleaner.Clean(_folder, TestContext.Current.CancellationToken);

        result.ShouldBe(new(0, 0, true));
        _fileSystem.File.Exists(leftover).ShouldBeTrue();
        var record = Records(_folderIsLinkEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(_folder);
    }

    [Fact]
    public void Clean_CancelledToken_ThrowsAndDeletesNothing()
    {
        var leftover = Path.Combine(_folder, "a.zip");
        _fileSystem.AddFile(leftover, new("partial"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Should.Throw<OperationCanceledException>(() => _cleaner.Clean(_folder, cancelled.Token));

        _fileSystem.File.Exists(leftover).ShouldBeTrue();
    }

    private List<FakeLogRecord> Records(int eventId) =>
        [.. _logger.Collector.GetSnapshot().Where(record => record.Id.Id == eventId)];
}
```

**Тесты оркестратора** (`BackupOrchestratorTests.cs`), правки:
- В `Core.Tests` подставить `ITemporaryArchiveCleaner` через NSubstitute и передать его в конструктор.
- Удалить тесты `RunAsync_LeftoversInTemporaryFolder_DeletedBeforeFirstRepository`, `RunAsync_LeftoverCannotBeDeleted_LogsWarningAndContinues` и `RunAsync_TemporaryFolderIsLink_LeavesItsContentAndContinues`. Их поведение теперь проверяет `TemporaryArchiveCleanerTests`. Вместе с ними уйдут константы `_temporaryEntryDeletedEventId` и ей подобные.
- Оставить и переписать на проверку вызова подставки такие тесты:
  - `RunAsync_NoRepositories_ReturnsSuccessAndEmptiesTemporaryFolder`: `Clean(_temporaryFolder, …)` вызван один раз.
  - `RunAsync_LockHeld_ReturnsCriticalWithoutTemporaryFolderCleanupOrDiscovery`: `Clean` не вызван.
  - `RunAsync_LockTakenAfterTokenAndBeforeTemporaryFolderCleanup`: порядок «токен → блокировка → `Clean`».
  - `RunAsync_TemporaryFolderEmptiedBeforeDiscovery`: порядок «`Clean` → обнаружение».
  - `RunAsync_DryRun_LeavesTemporaryFolderUntouched`: `Clean` не вызван.
- Интеграционный тест `Infrastructure.IntegrationTests/Runs/BackupOrchestratorTests.cs:132` (`RunAsync_LeftoverInTemporaryFolder_IsDeletedAtStart`) остаётся без изменений. Он проверяет связку с реальной файловой системой.
- В `CoreServiceCollectionExtensionsTests` добавить проверку, что `ITemporaryArchiveCleaner` разрешается.

## Что нужно от вас

- Согласны с папкой `Archiving` и с именем `TemporaryArchiveCleaner`? Альтернатива: `Runs`.
- Метод синхронный, потому что `IFileSystem` синхронный и прежний код тоже был синхронным. Токен нужен только для отмены между записями. Если хотите `Task`-вариант, скажите.
- Применить правки? Коммит я не делаю, пока вы явно не попросите.
