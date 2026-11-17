**Ответ коротко.** Очистка `<sourcesRoot>\.tmp\` по FR-7.4 уже работает. Она живёт внутри `BackupOrchestrator` как приватный метод `EmptyTemporaryFolder` (`BackupOrchestrator.cs:923`) и вызывается после захвата блокировки (шаг 4 из §9). Отдельного сервиса нет. Ниже показано, как вынести эту логику в сервис. Поведение при этом не меняется. PRD менять не нужно: FR-7.4 и §9 уже описывают ровно это. Ни один файл я не менял.

## Типы и файлы

```
src/GitHubBackup.Core/Runs/
  ITemporaryFolderCleaner.cs       new: interface
  TemporaryFolderCleaner.cs        new: implementation (code moves from BackupOrchestrator)
  TemporaryFolderCleanerLog.cs     new: [LoggerMessage] methods (moved from BackupOrchestratorLog)
  BackupOrchestrator.cs            changed: calls the service, EmptyTemporaryFolder is removed
  BackupOrchestratorLog.cs         changed: TemporaryEntryDeleted, TemporaryEntryNotDeleted, TemporaryFolderNotListed, TemporaryFolderIsLink are removed
src/GitHubBackup.Core/CoreServiceCollectionExtensions.cs   changed: registration
src/GitHubBackup.Core.Tests/Runs/
  TemporaryFolderCleanerTests.cs   new
  BackupOrchestratorTests.cs       changed
```

Сервис лежит в `Core`, потому что работает только через `IFileSystem` и ничего из `Infrastructure` не требует.

### `ITemporaryFolderCleaner.cs`

```csharp
namespace GitHubBackup.Core.Runs;

/// <summary>
/// Empties the folder in which archives are built of the files that interrupted runs left behind.
/// </summary>
/// <remarks>
/// The caller holds the run lock, so no other run writes to the folder while it is emptied.
/// </remarks>
public interface ITemporaryFolderCleaner
{
    /// <summary>
    /// Deletes every file and folder inside <paramref name="folder"/>. An entry that cannot be deleted is logged and
    /// left for the next run; a missing folder is not an error.
    /// </summary>
    /// <param name="folder">The absolute path of the folder of temporary archives.</param>
    /// <param name="cancellationToken">Stops the clean-up before the next entry.</param>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    void Clean(string folder, CancellationToken cancellationToken);
}
```

Метод синхронный, как и весь код `IFileSystem` в проекте. Токен проверяется между записями, поэтому отмена доходит до оркестратора (NFR-4).

### `TemporaryFolderCleaner.cs`

```csharp
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Runs;

/// <summary>
/// Implements <see cref="ITemporaryFolderCleaner"/> on top of <see cref="IFileSystem"/>.
/// </summary>
/// <remarks>
/// The folder holds only archives under construction, so anything in it at the start of a run is garbage. An entry
/// that cannot be deleted does not prevent new archives, whose temporary names are unique. A folder that is a
/// junction or a symbolic link is left alone, because emptying it would delete files outside the sources root.
/// </remarks>
/// <param name="fileSystem">Lists and deletes the entries of the folder.</param>
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

Тексты сообщений и уровни те же, что сейчас в `BackupOrchestratorLog`. Меняются только категория логгера и номера EventId (теперь 1–4).

```csharp
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Runs;

/// <summary>
/// Log messages of <see cref="TemporaryFolderCleaner"/>.
/// </summary>
internal static partial class TemporaryFolderCleanerLog
{
    /// <summary>Logs a deleted leftover of an interrupted run.</summary>
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

    /// <summary>Logs that the folder cannot be listed; it stays as it is.</summary>
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

`CoreServiceCollectionExtensions.cs` — строка перед `BackupOrchestrator`. Дополнительно нужно добавить `<see cref="ITemporaryFolderCleaner"/>` в `<summary>` метода `AddGitHubBackupCore`:

```csharp
services.TryAddSingleton<ITemporaryFolderCleaner, TemporaryFolderCleaner>();
services.TryAddSingleton<BackupOrchestrator>();
```

Сервис не хранит состояния, поэтому он `Singleton`. Оба хоста вызывают `AddGitHubBackupCore()`, так что ничего больше регистрировать не нужно.

`BackupOrchestrator.cs`:

1. В первичный конструктор после `IFileSystem fileSystem` добавить `ITemporaryFolderCleaner temporaryFolderCleaner,`.
2. Добавить `<param name="temporaryFolderCleaner">Empties the folder of temporary archives of the files that interrupted runs left behind.</param>`. Описание `fileSystem` («Empties the folder of temporary archives») станет неверным, его нужно поправить. `fileSystem` остаётся, его использует `IsInside`.
3. В `RunLockedAsync` (строка 238) заменить вызов:
   ```csharp
   else
   {
       temporaryFolderCleaner.Clean(temporaryFolder, cancellationToken);
   }
   ```
4. Удалить метод `EmptyTemporaryFolder` (строки 913–967).

Порядок шагов сохраняется. `RunLockedAsync` вызывается уже под блокировкой, dry-run по-прежнему не очищает папку и только пишет `TemporaryFolderLeftUntouched`.

Конструктор `BackupOrchestrator` создаётся вручную в `Infrastructure.IntegrationTests/Runs/BackupOrchestratorTests.cs:1241` и в `Core.Tests`. В обоих местах нужен новый аргумент. В интеграционных тестах подойдёт `new TemporaryFolderCleaner(_fileSystem, NullLogger<TemporaryFolderCleaner>.Instance)`.

## Тесты

### Новый `Core.Tests/Runs/TemporaryFolderCleanerTests.cs`

```csharp
using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Runs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace GitHubBackup.Core.Tests.Runs;

/// <summary>
/// Tests <see cref="TemporaryFolderCleaner"/> with an in-memory file system.
/// </summary>
public sealed class TemporaryFolderCleanerTests
{
    private const string _folder = @"C:\sources\.tmp";

    private const int _entryDeletedEventId = 1;
    private const int _entryNotDeletedEventId = 2;
    private const int _folderIsLinkEventId = 4;

    private readonly MockFileSystem _fileSystem = new();
    private readonly FakeLogger<TemporaryFolderCleaner> _logger = new();
    private readonly TemporaryFolderCleaner _cleaner;

    /// <summary>
    /// Creates the cleaner over the in-memory file system.
    /// </summary>
    public TemporaryFolderCleanerTests() => _cleaner = new(_fileSystem, _logger);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public void Clean_FilesAndFolders_DeletesEveryEntryAndKeepsTheFolder()
    {
        _fileSystem.AddFile(Path.Combine(_folder, "a.zip"), new("partial"));
        _fileSystem.AddFile(Path.Combine(_folder, "nested", "b.zip"), new("partial"));

        _cleaner.Clean(_folder, CancellationToken);

        _fileSystem.Directory.Exists(_folder).ShouldBeTrue();
        _fileSystem.Directory.GetFileSystemEntries(_folder).ShouldBeEmpty();
        var records = Records(_entryDeletedEventId);
        records.Count.ShouldBe(2);
        records.ShouldAllBe(record => record.Level == LogLevel.Information);
    }

    [Fact]
    public void Clean_FolderMissing_DoesNothing()
    {
        _cleaner.Clean(_folder, CancellationToken);

        _fileSystem.Directory.Exists(_folder).ShouldBeFalse();
        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public void Clean_EmptyFolder_LogsNothing()
    {
        _fileSystem.AddDirectory(_folder);

        _cleaner.Clean(_folder, CancellationToken);

        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public void Clean_FileCannotBeDeleted_LogsWarningLeavesItAndDeletesTheRest()
    {
        var locked = Path.Combine(_folder, "locked.zip");
        var other = Path.Combine(_folder, "other.zip");
        _fileSystem.AddFile(locked, new("partial") { AllowedFileShare = FileShare.None });
        _fileSystem.AddFile(other, new("partial"));

        _cleaner.Clean(_folder, CancellationToken);

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

        _cleaner.Clean(_folder, CancellationToken);

        _fileSystem.File.Exists(leftover).ShouldBeTrue();
        var record = Records(_folderIsLinkEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(_folder);
    }

    [Fact]
    public void Clean_CancelledBeforeStart_ThrowsAndDeletesNothing()
    {
        var leftover = Path.Combine(_folder, "a.zip");
        _fileSystem.AddFile(leftover, new("partial"));
        using var source = new CancellationTokenSource();
        source.Cancel();

        Should.Throw<OperationCanceledException>(() => _cleaner.Clean(_folder, source.Token));

        _fileSystem.File.Exists(leftover).ShouldBeTrue();
    }

    private IReadOnlyList<FakeLogRecord> Records(int eventId) =>
        [.. _logger.Collector.GetSnapshot().Where(record => record.Id.Id == eventId)];
}
```

Случай «папку нельзя перечислить» (`FolderNotListed`) я не покрываю. `MockFileSystem` не умеет заставить `GetFileSystemEntries` бросить `IOException` без своего подкласса. Если нужен этот тест, напишу маленький fake.

### Изменения в `BackupOrchestratorTests.cs`

Добавить поле `private readonly ITemporaryFolderCleaner _cleaner = Substitute.For<ITemporaryFolderCleaner>();` и передать его в конструктор. Константы `_temporaryEntryDeletedEventId`, `_temporaryEntryNotDeletedEventId` и `_temporaryFolderIsLinkEventId` больше не нужны. Тесты, которые проверяют содержимое `.tmp` через `_fileSystem`, переходят на проверку вызова `_cleaner`. Это `RunAsync_NoRepositories_ReturnsSuccessAndEmptiesTemporaryFolder`, `…LockHeld…`, `…LockTakenAfterToken…`, `…LeftoversInTemporaryFolder…`, `…TemporaryFolderEmptiedBeforeDiscovery` и `…DryRun_LeavesTemporaryFolderUntouched`. Тесты `…LeftoverCannotBeDeleted…` и `…TemporaryFolderIsLink…` переезжают в `TemporaryFolderCleanerTests`. Примеры новых проверок:

```csharp
[Fact]
public async Task RunAsync_ActiveRun_CleansTemporaryFolderBeforeFirstRepository()
{
    await RunAsync();

    Received.InOrder(() =>
    {
        _cleaner.Clean(_temporaryFolder, CancellationToken);
        _detector.DetectAsync(Arg.Any<ChangeDetectionRequest>(), Arg.Any<CancellationToken>());
    });
}

[Fact]
public async Task RunAsync_LockHeld_DoesNotCleanTemporaryFolder()
{
    _runLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns((IDisposable?)null);

    await RunAsync();

    _cleaner.DidNotReceiveWithAnyArgs().Clean(default!, default);
}

[Fact]
public async Task RunAsync_DryRun_DoesNotCleanTemporaryFolder()
{
    await RunAsync(dryRun: true);

    _cleaner.DidNotReceiveWithAnyArgs().Clean(default!, default);
    SingleRecord(_temporaryFolderLeftUntouchedEventId).Level.ShouldBe(LogLevel.Information);
}
```

Сигнатуры `RunAsync(...)` и способ «блокировка занята» в существующих тестах могут выглядеть иначе (см. тест на строке 654). Примеры нужно подогнать под них.

Ещё нужны две проверки:
- Тест, что `AddGitHubBackupCore()` регистрирует `ITemporaryFolderCleaner`. Его место — рядом с существующими тестами регистрации в `Core.Tests`.
- Интеграционные тесты `Infrastructure.IntegrationTests` и CLI end-to-end должны пройти без изменений, кроме нового аргумента конструктора.

## Что учесть

- **Не вошло в рефакторинг.** Если внутри `.tmp` лежит junction или symlink на папку, то `Directory.Delete(entry, recursive: true)` в текущем коде всё равно сработает. В .NET это удаляет саму ссылку, а не цель. Я перенёс код как есть. Если хотите явную защиту (удалять такую запись без `recursive`), это отдельное небольшое изменение.
- **Документы.** Рефакторинг по правилам репозитория нужно вести через issue и план в `docs/plans/`. README и PRD не меняются. В плане `test-47` сказано, что фикстуру `quality-service-convention` придётся перенацелить, когда очистка `.tmp` будет реализована. Она уже реализована, так что `ground-truth.md` стоит отметить.
- **Проверка после правок.** Запустить по отдельности `dotnet build src/GitHubBackup.slnx -t:Rebuild -clp:ErrorsOnly`, затем `dotnet test --solution src/GitHubBackup.slnx --no-build --filter-not-trait "Category=Smoke"` и `dotnet format src/GitHubBackup.slnx --verify-no-changes`.

Если хотите, скажу, как оформить это в issue, или внесу правки после вашего go-ahead. Коммит не делаю без явной команды.
