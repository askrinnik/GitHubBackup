**Кратко:** очистка по FR-7.4 уже есть, но она не вынесена в сервис. Метод `BackupOrchestrator.EmptyTemporaryFolder` вызывается из `RunLockedAsync` уже после `runLock.TryAcquireAsync`. В dry-run он не вызывается, это требование FR-9.5. Тесты на это уже есть в `BackupOrchestratorTests`. Поэтому ниже не новая логика, а вынос существующей в сервис. PRD менять не нужно: FR-7.4 описывает именно такое поведение.

Файлы я не менял и ничего не запускал. Код ниже не компилировался.

## Типы и файлы

```
src/GitHubBackup.Core/Runs/
  ITemporaryFolderCleaner.cs        new
  TemporaryFolderCleaner.cs         new (the body of EmptyTemporaryFolder moves here)
  TemporaryFolderCleanerLog.cs      new (log events 2, 3, 4, 10 move here)
  BackupOrchestrator.cs             changed
  BackupOrchestratorLog.cs          changed (the four moved events removed)
  ../CoreServiceCollectionExtensions.cs   changed (one registration line)
src/GitHubBackup.Core.Tests/Runs/
  TemporaryFolderCleanerTests.cs    new
  BackupOrchestratorTests.cs        changed
```

Сервис лежит в `Core`, потому что `Core` уже ходит на диск через `IFileSystem`. Папка `.tmp` приходит от `IRepositoryPathResolver.GetTemporaryArchiveFolder`. Сервис не знает ни про `sourcesRoot`, ни про блокировку, ни про dry-run. Эти решения остаются в оркестраторе.

### `ITemporaryFolderCleaner.cs`

```csharp
namespace GitHubBackup.Core.Runs;

/// <summary>
/// Removes the files that interrupted runs left in the folder of temporary archives.
/// </summary>
public interface ITemporaryFolderCleaner
{
    /// <summary>
    /// Deletes every file and folder inside <paramref name="folder"/>; the folder itself stays.
    /// </summary>
    /// <remarks>
    /// The caller holds the run lock, so no other run writes to the folder. An entry that cannot be deleted is logged
    /// and left for the next run; the method never throws for an I/O failure.
    /// </remarks>
    /// <param name="folder">The folder of temporary archives.</param>
    void Clean(string folder);
}
```

### `TemporaryFolderCleaner.cs`

```csharp
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Runs;

/// <inheritdoc />
/// <remarks>
/// The folder holds only archives under construction, so anything in it after the lock is taken is garbage. A folder
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
            Delete(entry);
        }
    }

    /// <summary>
    /// Deletes one file or folder; a failure is logged and the entry is left for the next run.
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

Метод синхронный и без `CancellationToken`, как сейчас. `IFileSystem` не имеет асинхронного удаления. Правило `csharp.md` про async относится к I/O с токеном, и здесь я от него отступаю. Если нужен токен, достаточно проверять `cancellationToken.ThrowIfCancellationRequested()` в цикле.

### `TemporaryFolderCleanerLog.cs`

Тексты сообщений те же, что сейчас. Меняются только имена методов и номера событий.

```csharp
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Runs;

/// <summary>
/// Source-generated log messages of <see cref="TemporaryFolderCleaner"/>.
/// </summary>
internal static partial class TemporaryFolderCleanerLog
{
    /// <summary>Logs a leftover file or folder that was deleted.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The deleted file or folder.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Deleted the leftover temporary archive {Path}")]
    public static partial void EntryDeleted(ILogger logger, string path);

    /// <summary>Logs a leftover that cannot be deleted; the next run retries.</summary>
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
    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "The temporary archive folder {Path} is a link and is not emptied")]
    public static partial void FolderIsLink(ILogger logger, string path);
}
```

Я не видел объявление класса `BackupOrchestratorLog`. Если там не `internal`, сделайте так же.

## Регистрация и вызов

**Регистрация.** Одна строка в `CoreServiceCollectionExtensions.cs`, рядом с остальными `TryAddSingleton`, перед `BackupOrchestrator` (около строки 52):

```csharp
services.TryAddSingleton<ITemporaryFolderCleaner, TemporaryFolderCleaner>();
```

`IFileSystem` уже регистрируется, потому что оркестратор от него зависит. `BackupOrchestrator` регистрируется как `TryAddSingleton<BackupOrchestrator>()`, и контейнер сам подставит новый параметр.

**Вызов.** В `BackupOrchestrator` меняются три места.

1. В первичном конструкторе `IFileSystem fileSystem` заменяется на `ITemporaryFolderCleaner temporaryFolderCleaner`. Других обращений к `fileSystem` в файле нет. Поэтому уходят также `using System.IO.Abstractions;` и `<param name="fileSystem">`. Вместо них добавляется `<param name="temporaryFolderCleaner">Empties the folder of temporary archives.</param>`.
2. В `RunLockedAsync` ветка `else` вызывает сервис:
   ```csharp
   else
   {
       temporaryFolderCleaner.Clean(temporaryFolder);
   }
   ```
   Порядок не меняется: блокировка, затем очистка, затем discovery. Ветка `dryRun` с `TemporaryFolderLeftUntouched` остаётся как есть.
3. Метод `EmptyTemporaryFolder` целиком удаляется. В `BackupOrchestratorLog` удаляются события 2, 3, 4 и 10. Остальные номера не трогаем.

## Тесты

### `TemporaryFolderCleanerTests.cs` (новый)

```csharp
using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Runs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
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
    private readonly TemporaryFolderCleaner _cleaner;

    public TemporaryFolderCleanerTests() => _cleaner = new(_fileSystem, _logger);

    [Fact]
    public void Clean_FolderMissing_DoesNothing()
    {
        _cleaner.Clean(_folder);

        _fileSystem.Directory.Exists(_folder).ShouldBeFalse();
        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public void Clean_FilesAndNestedFolders_DeletesThemAndKeepsTheFolder()
    {
        _fileSystem.AddFile(Path.Combine(_folder, "a.zip"), new("partial"));
        _fileSystem.AddFile(Path.Combine(_folder, "nested", "b.zip"), new("partial"));

        _cleaner.Clean(_folder);

        _fileSystem.Directory.Exists(_folder).ShouldBeTrue();
        _fileSystem.Directory.GetFileSystemEntries(_folder).ShouldBeEmpty();
    }

    [Fact]
    public void Clean_EntryDeleted_LogsItsPath()
    {
        var leftover = Path.Combine(_folder, "a.zip");
        _fileSystem.AddFile(leftover, new("partial"));

        _cleaner.Clean(_folder);

        var record = _logger.Collector.GetSnapshot().Single(r => r.Id.Id == _entryDeletedEventId);
        record.Level.ShouldBe(LogLevel.Information);
        record.GetStructuredStateValue("Path").ShouldBe(leftover);
    }

    [Fact]
    public void Clean_EntryCannotBeDeleted_LeavesItAndDeletesTheOthers()
    {
        var locked = Path.Combine(_folder, "locked.zip");
        var free = Path.Combine(_folder, "free.zip");
        _fileSystem.AddFile(locked, new MockFileData("partial") { AllowedFileShare = FileShare.None });
        _fileSystem.AddFile(free, new("partial"));

        _cleaner.Clean(_folder);

        _fileSystem.File.Exists(locked).ShouldBeTrue();
        _fileSystem.File.Exists(free).ShouldBeFalse();
        var record = _logger.Collector.GetSnapshot().Single(r => r.Id.Id == _entryNotDeletedEventId);
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(locked);
    }

    [Fact]
    public void Clean_FolderIsLink_LeavesItsContentAndLogsWarning()
    {
        var leftover = Path.Combine(_folder, "a.zip");
        _fileSystem.AddFile(leftover, new("partial"));
        _fileSystem.DirectoryInfo.New(_folder).Attributes |= FileAttributes.ReparsePoint;

        _cleaner.Clean(_folder);

        _fileSystem.File.Exists(leftover).ShouldBeTrue();
        var record = _logger.Collector.GetSnapshot().Single(r => r.Id.Id == _folderIsLinkEventId);
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(_folder);
    }
}
```

Я сверил тесты с названиями из `BackupOrchestratorTests` (`locked.zip`, `ReparsePoint`, `FakeLogger`), но не читал тело теста с `locked.zip`. Проверьте две вещи:

- Блокировка через `AllowedFileShare = FileShare.None` должна давать исключение при `File.Delete` в `MockFileSystem`. Если нет, скопируйте приём из существующего теста (около строки 531).
- Случай «папку нельзя перечислить» (`FolderNotListed`) здесь не покрыт: `MockFileSystem` его не воспроизводит. Его можно покрыть через NSubstitute-заглушку `IFileSystem`, если нужно.

### Изменения в `BackupOrchestratorTests.cs`

- Конструкция оркестратора получает `new TemporaryFolderCleaner(_fileSystem, NullLogger<TemporaryFolderCleaner>.Instance)` вместо `_fileSystem`. Тогда тесты порядка остаются в силе: `LockTakenAfterTokenAndBeforeTemporaryFolderCleanup`, `TemporaryFolderEmptiedBeforeDiscovery`, `LeftoversInTemporaryFolder_DeletedBeforeFirstRepository`, `LockHeld_...WithoutTemporaryFolderCleanup`, `DryRun_LeavesTemporaryFolderUntouched`.
- Тест `RunAsync_TemporaryFolderIsLink_LeavesItsContentAndContinues` теряет проверку лога события 10, потому что она переехала в `TemporaryFolderCleanerTests`. Проверка, что прогон продолжается, остаётся.
- Константа `_temporaryFolderIsLinkEventId` удаляется.
- Для явной проверки вызова можно добавить один тест с `Substitute.For<ITemporaryFolderCleaner>()`. Он проверяет, что `Clean(_temporaryFolder)` вызван один раз в обычном прогоне и не вызван в dry-run.

Тесты `Infrastructure.IntegrationTests/Runs/BackupOrchestratorTests.cs` и `Infrastructure.Tests/InfrastructureServiceCollectionExtensionsTests.cs` тоже создают оркестратор или проверяют регистрации. Их придётся поправить под новый параметр конструктора. Я их не открывал.

Сначала скажите, нужен ли такой вынос, раз поведение уже есть. Если да, заменяю код и прогоняю сборку и тесты. Коммита не будет, пока вы не скажете.
