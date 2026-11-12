Сервис очистки уже есть, но не отдельным типом: это приватный метод `BackupOrchestrator.EmptyTemporaryFolder`. Он вызывается после `runLock.TryAcquireAsync`, а в dry-run пропускается. Он уже покрыт тестами оркестратора. Значит, новый сервис — это вынос этой логики в `ITemporaryFolderCleaner`, поведение остаётся прежним. Файлы я не менял.

Если вы хотели что-то другое, например изменить поведение, скажите. Для чистого выноса PRD менять не нужно: FR-7.4 и §9 уже описывают это поведение.

## Типы и файлы

```
src/GitHubBackup.Core/Runs/
  ITemporaryFolderCleaner.cs        new
  TemporaryFolderCleaner.cs         new
  TemporaryFolderCleanerLog.cs      new (events 2, 3, 4, 10 move here from BackupOrchestratorLog)
  BackupOrchestrator.cs             edit: remove EmptyTemporaryFolder and IFileSystem, inject the cleaner
  BackupOrchestratorLog.cs          edit: remove the four moved events
src/GitHubBackup.Core/CoreServiceCollectionExtensions.cs   edit: register the service
src/GitHubBackup.Core.Tests/Runs/
  TemporaryFolderCleanerTests.cs    new
  BackupOrchestratorTests.cs        edit: keep only the call-order tests, move the rest
```

Сервис живёт в `Core`, потому что `Core` ходит на диск только через `IFileSystem`. Реализация в `Infrastructure` не нужна.

## Код

```csharp
// ITemporaryFolderCleaner.cs
namespace GitHubBackup.Core.Runs;

/// <summary>
/// Deletes what interrupted runs left in the folder of temporary archives.
/// </summary>
/// <remarks>
/// The caller must hold the run lock: the folder may hold the archive another run is building.
/// </remarks>
public interface ITemporaryFolderCleaner
{
    /// <summary>
    /// Deletes every file and folder in <paramref name="folder"/>; an entry that cannot be deleted is logged and left
    /// for the next run.
    /// </summary>
    /// <param name="folder">The folder of temporary archives; a missing folder is not an error.</param>
    void Clean(string folder);
}
```

```csharp
// TemporaryFolderCleaner.cs
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Runs;

/// <summary>
/// Implements <see cref="ITemporaryFolderCleaner"/>.
/// </summary>
/// <remarks>
/// The folder holds only archives under construction, so anything in it after the run lock is taken is garbage. A
/// folder that is a junction or a symbolic link is left alone, because emptying it would delete files outside the
/// sources root.
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
                // An undeletable entry does not block new archives, whose temporary names are unique.
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

    /// <summary>Logs a leftover that cannot be deleted.</summary>
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

Перед правкой нужно проверить, что EventId 1–4 не пересекаются с другими классами лога, и что `BackupOrchestratorLog` не используется для этих событий в других тестах. Сейчас у событий номера 2, 3, 4 и 10 в `BackupOrchestratorLog`.

## Регистрация и вызов

В `CoreServiceCollectionExtensions.AddGitHubBackupCore` перед `BackupOrchestrator`:

```csharp
services.TryAddSingleton<ITemporaryFolderCleaner, TemporaryFolderCleaner>();
```

Также добавьте `<see cref="ITemporaryFolderCleaner"/>` в summary метода. `IFileSystem` регистрируется в `Infrastructure`, как и сейчас для оркестратора.

В `BackupOrchestrator` заменить параметр `IFileSystem fileSystem` на `ITemporaryFolderCleaner temporaryFolderCleaner` (и `<param>`), а в `RunLockedAsync` — вызов:

```csharp
else
{
    temporaryFolderCleaner.Clean(temporaryFolder);
}
```

Метод `EmptyTemporaryFolder` удаляется. Порядок не меняется: сначала блокировка запуска, потом очистка, и в dry-run очистки нет (FR-9.5). Нужно проверить, что `fileSystem` больше нигде не используется в оркестраторе; в прочитанном коде он нужен только для очистки.

## Тесты

`TemporaryFolderCleanerTests.cs` — по правилам `tests.md`: `MockFileSystem`, `FakeLogger<T>`, Shouldly, события ищутся по `EventId` из именованных констант.

```csharp
using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Runs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Shouldly;

namespace GitHubBackup.Core.Tests.Runs;

public sealed class TemporaryFolderCleanerTests
{
    private const int EntryDeletedEventId = 1;
    private const int EntryNotDeletedEventId = 2;
    private const int FolderNotListedEventId = 3;
    private const int FolderIsLinkEventId = 4;

    private const string Folder = @"C:\sources\.tmp";

    private readonly MockFileSystem _fileSystem = new();
    private readonly FakeLogger<TemporaryFolderCleaner> _logger = new();
    private readonly TemporaryFolderCleaner _cleaner;

    public TemporaryFolderCleanerTests() => _cleaner = new(_fileSystem, _logger);

    [Fact]
    public void Clean_FilesAndFolders_DeletesThemAndLogsEach()
    {
        _fileSystem.AddFile(Path.Combine(Folder, "a.zip"), new("partial"));
        _fileSystem.AddFile(Path.Combine(Folder, "nested", "b.zip"), new("partial"));

        _cleaner.Clean(Folder);

        _fileSystem.Directory.GetFileSystemEntries(Folder).ShouldBeEmpty();
        _fileSystem.Directory.Exists(Folder).ShouldBeTrue();
        Records(EntryDeletedEventId).Count.ShouldBe(2);
    }

    [Fact]
    public void Clean_FolderMissing_DoesNothing()
    {
        _cleaner.Clean(Folder);

        _fileSystem.Directory.Exists(Folder).ShouldBeFalse();
        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public void Clean_EmptyFolder_LogsNothing()
    {
        _fileSystem.AddDirectory(Folder);

        _cleaner.Clean(Folder);

        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public void Clean_FileCannotBeDeleted_LogsWarningLeavesItAndDeletesTheRest()
    {
        var locked = Path.Combine(Folder, "locked.zip");
        var other = Path.Combine(Folder, "other.zip");
        _fileSystem.AddFile(locked, new("partial") { AllowedFileShare = FileShare.None });
        _fileSystem.AddFile(other, new("partial"));

        _cleaner.Clean(Folder);

        _fileSystem.File.Exists(locked).ShouldBeTrue();
        _fileSystem.File.Exists(other).ShouldBeFalse();
        var record = Records(EntryNotDeletedEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(locked);
    }

    [Fact]
    public void Clean_FolderIsLink_LeavesContentAndLogsWarning()
    {
        var leftover = Path.Combine(Folder, "a.zip");
        _fileSystem.AddFile(leftover, new("partial"));
        _fileSystem.DirectoryInfo.New(Folder).Attributes |= FileAttributes.ReparsePoint;

        _cleaner.Clean(Folder);

        _fileSystem.File.Exists(leftover).ShouldBeTrue();
        var record = Records(FolderIsLinkEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(Folder);
    }

    [Fact]
    public void Clean_FolderCannotBeListed_LogsWarningAndDoesNotThrow()
    {
        var cleaner = new TemporaryFolderCleaner(new ListingFailsFileSystem(), _logger);

        cleaner.Clean(Folder);

        var record = Records(FolderNotListedEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(Folder);
    }

    private IReadOnlyList<FakeLogRecord> Records(int eventId) =>
        [.. _logger.Collector.GetSnapshot().Where(record => record.Id.Id == eventId)];
}
```

Нужен небольшой помощник `ListingFailsFileSystem`: подкласс `MockFileSystem` в том же файле. Его `Directory` бросает `UnauthorizedAccessException` из `GetFileSystemEntries`. Если `MockDirectory` не удаётся переопределить напрямую, проще подменить `IFileSystem` через NSubstitute. Я не проверял, как делают соседние тесты, и посмотрю это при реализации.

В `BackupOrchestratorTests` остаётся проверка порядка и вызова. `ITemporaryFolderCleaner` подменяется NSubstitute:

- **Нужно оставить:** тесты `RunAsync_LockHeld_...`, `RunAsync_LockTakenAfterTokenAndBeforeTemporaryFolderCleanup`, `RunAsync_TemporaryFolderEmptiedBeforeDiscovery`. Они проверяют `Clean` через `Received` и порядок вызовов.
- **Нужно оставить:** `RunAsync_DryRun_LeavesTemporaryFolderUntouched`. Он проверяет `DidNotReceive().Clean(...)`.
- **Нужно перенести** в `TemporaryFolderCleanerTests`: `..._LeftoversInTemporaryFolder_...`, `..._LeftoverCannotBeDeleted_...`, `..._TemporaryFolderIsLink_...`. Они проверяют логику очистки, а не оркестратор.
- **Нужно оставить без изменений:** интеграционный `RunAsync_LeftoverInTemporaryFolder_IsDeletedAtStart` в `Infrastructure.IntegrationTests`. Он должен пройти с реальным `TemporaryFolderCleaner`.

Тестов на регистрацию отдельно писать не нужно: `CoreServiceCollectionExtensionsTests` уже проверяет, что `BackupOrchestrator` создаётся из контейнера, и упадёт, если `ITemporaryFolderCleaner` не зарегистрирован. Если там перечислены регистрируемые типы, добавьте в список новый.

## Что решить до реализации

1. **Нужен ли вынос.** Если важна только функциональность, то она уже работает, и новый тип только перекладывает код. Вынос оправдан, если сервис нужен кому-то ещё, например команде очистки, или если оркестратор слишком разросся (он уже около 800 строк).
2. **Номера событий.** Я предложил 1–4 в новом классе лога. Если вы держите номера уникальными на всю сборку, оставьте 2, 3, 4 и 10.

Реализовать вынос? Коммит я делать не буду, пока вы не скажете.
