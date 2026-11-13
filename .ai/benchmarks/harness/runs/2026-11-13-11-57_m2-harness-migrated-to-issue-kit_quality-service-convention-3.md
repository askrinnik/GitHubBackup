**Короткий ответ:** эта очистка уже есть в коде. `BackupOrchestrator.EmptyTemporaryFolder` (`src/GitHubBackup.Core/Runs/BackupOrchestrator.cs:432`) вызывается из `RunLockedAsync` после `runLock.TryAcquireAsync`. В dry-run она не вызывается, как требует FR-9.5. Файл, который нельзя удалить, остаётся и попадает в лог как warning. Если `.tmp` — junction или symlink, папка пропускается. Тесты тоже есть: `BackupOrchestratorTests.cs:502–557`, плюс интеграционный тест `RunAsync_LeftoverInTemporaryFolder_IsDeletedAtStart`.

Поведения добавлять не нужно. Остаётся вынести этот метод в отдельный сервис. Ниже код такого выноса. Файлы я не менял.

## Типы и файлы (все в `GitHubBackup.Core`, папка `Runs/`)

**`ITemporaryFolderCleaner.cs`**

```csharp
namespace GitHubBackup.Core.Runs;

/// <summary>
/// Deletes the files that interrupted runs left in the folder of temporary archives.
/// </summary>
public interface ITemporaryFolderCleaner
{
    /// <summary>
    /// Deletes every file and folder inside <paramref name="folder"/>.
    /// </summary>
    /// <remarks>
    /// The caller holds the <see cref="IRunLock"/>, so no other run writes to the folder. An entry that cannot be
    /// deleted is logged and left for the next run. A folder that is a junction or a symbolic link is not emptied,
    /// because that would delete files outside the sources root. A missing folder is not an error.
    /// </remarks>
    /// <param name="folder">The folder of temporary archives.</param>
    void Empty(string folder);
}
```

**`TemporaryFolderCleaner.cs`**

```csharp
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Runs;

/// <summary>
/// Implements <see cref="ITemporaryFolderCleaner"/> over <see cref="IFileSystem"/>.
/// </summary>
/// <param name="fileSystem">Lists and deletes the entries of the folder.</param>
/// <param name="logger">The logger.</param>
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

**`TemporaryFolderCleanerLog.cs`** — это `internal static partial class` с четырьмя методами `[LoggerMessage]`: `EntryDeleted`, `EntryNotDeleted`, `FolderNotListed`, `FolderIsLink`. Я переношу их из `BackupOrchestratorLog` без изменений: те же тексты и те же `EventId` (3, 4, 10 и id записи об удалении, которого я не читал). Из `BackupOrchestratorLog` я удаляю `TemporaryEntryDeleted`, `TemporaryEntryNotDeleted`, `TemporaryFolderNotListed` и `TemporaryFolderIsLink`. Метод `TemporaryFolderLeftUntouched` для dry-run остаётся там.

Сервис синхронный, как и сам метод сейчас. `IFileSystem` не даёт async-API, поэтому `CancellationToken` ему не нужен.

## Регистрация и вызов

`CoreServiceCollectionExtensions.AddGitHubBackupCore`, рядом с остальными сервисами. Обновить `<summary>` метода: добавить `<see cref="ITemporaryFolderCleaner"/>`.

```csharp
services.TryAddSingleton<ITemporaryFolderCleaner, TemporaryFolderCleaner>();
```

`IFileSystem` уже регистрирует `Infrastructure`, поэтому новых зависимостей у хоста нет.

В `BackupOrchestrator`:

- В primary constructor добавляется `ITemporaryFolderCleaner temporaryFolderCleaner` с `<param>`. Параметр `IFileSystem fileSystem` убирается вместе с его `<param>`, **если** он нужен только для очистки. Я проверил только то, что его `<param>` описывает именно очистку. Остальные использования надо проверить grep-ом.
- В `RunLockedAsync` ветка `else` меняется:

```csharp
else
{
    temporaryFolderCleaner.Empty(temporaryFolder);
}
```

- Метод `EmptyTemporaryFolder` удаляется. Порядок «блокировка → очистка → discovery» не меняется.

## Тесты

**Новый файл `src/GitHubBackup.Core.Tests/Runs/TemporaryFolderCleanerTests.cs`.** Сюда переезжают три теста оркестратора про файловую систему (`LeftoversInTemporaryFolder`, `LeftoverCannotBeDeleted`, `TemporaryFolderIsLink`).

```csharp
using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Runs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Shouldly;
using Xunit;

namespace GitHubBackup.Core.Tests.Runs;

/// <summary>
/// Tests <see cref="TemporaryFolderCleaner"/> over an in-memory file system.
/// </summary>
public sealed class TemporaryFolderCleanerTests
{
    private const string _folder = @"C:\sources\.tmp";
    private const int _entryDeletedEventId = 1; // take the real value from the moved log entry
    private const int _entryNotDeletedEventId = 3;
    private const int _folderIsLinkEventId = 10;

    private readonly MockFileSystem _fileSystem = new();
    private readonly FakeLogger<TemporaryFolderCleaner> _logger = new();

    [Fact]
    public void Empty_FolderMissing_DoesNothing()
    {
        Cleaner().Empty(_folder);

        _fileSystem.Directory.Exists(_folder).ShouldBeFalse();
        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public void Empty_FilesAndNestedFolders_DeletesAllAndKeepsFolder()
    {
        _fileSystem.AddFile(Path.Combine(_folder, "a.zip"), new("partial"));
        _fileSystem.AddFile(Path.Combine(_folder, "nested", "b.zip"), new("partial"));

        Cleaner().Empty(_folder);

        _fileSystem.Directory.GetFileSystemEntries(_folder).ShouldBeEmpty();
        Records(_entryDeletedEventId).Count.ShouldBe(2);
    }

    [Fact]
    public void Empty_FileCannotBeDeleted_LogsWarningAndDeletesTheRest()
    {
        var locked = Path.Combine(_folder, "locked.zip");
        var other = Path.Combine(_folder, "other.zip");
        _fileSystem.AddFile(locked, new("partial") { AllowedFileShare = FileShare.None });
        _fileSystem.AddFile(other, new("partial"));

        Cleaner().Empty(_folder);

        _fileSystem.File.Exists(locked).ShouldBeTrue();
        _fileSystem.File.Exists(other).ShouldBeFalse();
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

        Cleaner().Empty(_folder);

        _fileSystem.File.Exists(leftover).ShouldBeTrue();
        var record = Records(_folderIsLinkEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(_folder);
    }

    private TemporaryFolderCleaner Cleaner() => new(_fileSystem, _logger);

    private IReadOnlyList<FakeLogRecord> Records(int eventId) =>
        _logger.Collector.GetSnapshot().Where(record => record.Id.Id == eventId).ToList();
}
```

Ошибку листинга (`FolderNotListed`) через `MockFileSystem` вызвать трудно. В проекте для этого есть `FaultInjectingFileSystem` в `Infrastructure.Tests`, но он в другом проекте. Поэтому я не включаю этот случай в набор. Если он нужен, пишем маленький fake внутри `Core.Tests`.

**Изменения в `BackupOrchestratorTests.cs`.** В классе появляется `private readonly ITemporaryFolderCleaner _cleaner = Substitute.For<ITemporaryFolderCleaner>();`, и он передаётся в конструктор вместо `IFileSystem` (если он больше не нужен). Тесты, которые сейчас проверяют порядок и dry-run через файловую систему, переписываются на `_cleaner`:

```csharp
[Fact]
public async Task RunAsync_Normal_EmptiesTemporaryFolderAfterLockBeforeDiscovery()
{
    var calls = new List<string>();
    _runLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns(_ =>
    {
        calls.Add("lock");
        return Task.FromResult<IDisposable?>(Substitute.For<IDisposable>());
    });
    _cleaner.When(cleaner => cleaner.Empty(_temporaryFolder)).Do(_ => calls.Add("clean"));
    _discovery.DiscoverAsync(Arg.Any<BackupConfig>(), Arg.Any<BackupRunFilter>(), Arg.Any<CancellationToken>())
        .Returns(_ =>
        {
            calls.Add("discover");
            return Task.FromResult(EmptyDiscovery());
        });

    await RunAsync();

    calls.ShouldBe(["lock", "clean", "discover"]);
}

[Fact]
public async Task RunAsync_DryRun_DoesNotEmptyTemporaryFolder()
{
    await RunAsync(dryRun: true);

    _cleaner.DidNotReceiveWithAnyArgs().Empty(default!);
}

[Fact]
public async Task RunAsync_LockHeld_DoesNotEmptyTemporaryFolder()
{
    _runLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IDisposable?>(null));

    await RunAsync();

    _cleaner.DidNotReceiveWithAnyArgs().Empty(default!);
}
```

Имена `_discovery`, `EmptyDiscovery()`, `RunAsync(dryRun:)` и точный тип результата `TryAcquireAsync` я здесь предположил. Я не читал соответствующие места тестового класса. Перед написанием надо свериться с существующими тестами `RunAsync_LockTakenAfterTokenAndBeforeTemporaryFolderCleanup` и `RunAsync_DryRun_LeavesTemporaryFolderUntouched`, которые эти тесты заменяют. Тесты `RunAsync_NoTemporaryFolder_ProcessesRepositories` и `..._NoRepositories_ReturnsSuccessAndEmptiesTemporaryFolder` теряют проверки файловой системы и упрощаются.

**Остальные тесты:**

- В `CoreServiceCollectionExtensionsTests` добавляется проверка, что `ITemporaryFolderCleaner` резолвится как `TemporaryFolderCleaner`.
- Интеграционный `BackupOrchestratorTests.RunAsync_LeftoverInTemporaryFolder_IsDeletedAtStart` остаётся как есть: он проверяет всю цепочку с настоящей файловой системой.

## Что учесть

1. **PRD.** Таблица сервисов в §8 (около строки 584 в `docs/PRD.md`) перечисляет интерфейсы вроде `IArchiveNameBuilder`. Новый `ITemporaryFolderCleaner` логично добавить туда строкой «Очистка `<sourcesRoot>\.tmp\` (FR-7.4)». По правилам репозитория это изменение PRD: версия, строка в истории, ваше одобрение. Решите, нужна ли такая строка. Если нет, код не меняется.
2. **Задача может быть уже закрыта.** FR-7.4 реализован, поэтому перед любым выносом стоит проверить, нет ли открытого issue с таким названием. Тогда его нужно закрыть, а не реализовывать заново.
3. **Ветка.** Сейчас вы на `98-move-issue-workflow-to-issue-kit`, а этот вынос к issue #98 не относится. Для него нужна отдельная ветка `<n>-<slug>`. Ветки и коммиты я без вашей команды не создаю.

Делать вынос в код?
