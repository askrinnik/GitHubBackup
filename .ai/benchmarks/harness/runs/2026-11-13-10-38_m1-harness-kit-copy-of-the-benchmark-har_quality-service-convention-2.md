**Ответ в двух строках.** Очистка `<sourcesRoot>\.tmp\` уже работает. Её делает приватный метод `BackupOrchestrator.EmptyTemporaryFolder` (`BackupOrchestrator.cs:432`). Он вызывается после `runLock.TryAcquireAsync` и не вызывается в dry-run (FR-9.5). Поэтому я предлагаю не писать новую логику, а **вынести этот метод в отдельный сервис** с тем же поведением. Файлы я не менял.

Если вы хотели что-то другое, например новое поведение, скажите. Тогда я пересмотрю план.

## Типы и файлы

```
src/GitHubBackup.Core/Runs/
  ITemporaryFolderCleaner.cs        (новый)
  TemporaryFolderCleaner.cs         (новый)
  TemporaryFolderCleanerLog.cs      (новый; события 2, 3, 4, 10 уходят из BackupOrchestratorLog)
  BackupOrchestrator.cs             (меняется)
  BackupOrchestratorLog.cs          (меняется)
src/GitHubBackup.Core/CoreServiceCollectionExtensions.cs   (меняется)
src/GitHubBackup.Core.Tests/Runs/
  TemporaryFolderCleanerTests.cs    (новый)
  BackupOrchestratorTests.cs        (меняется)
```

Сервис лежит в `Core`, как и вся логика запуска. Диск он трогает только через `IFileSystem`, как требуют правила слоёв.

### `ITemporaryFolderCleaner.cs`

```csharp
namespace GitHubBackup.Core.Runs;

/// <summary>
/// Deletes the files that interrupted runs left in the folder of temporary archives.
/// </summary>
public interface ITemporaryFolderCleaner
{
    /// <summary>
    /// Deletes every file and folder inside <paramref name="folder"/>; the folder itself stays.
    /// </summary>
    /// <remarks>
    /// The caller holds the run lock, which guarantees that no other run is building an archive in the folder. An
    /// entry that cannot be deleted is logged and left for the next run. A missing folder is not an error.
    /// </remarks>
    /// <param name="folder">The folder of temporary archives.</param>
    /// <param name="cancellationToken">Stops the clean-up between two entries.</param>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    void Clean(string folder, CancellationToken cancellationToken);
}
```

Метод синхронный, потому что `IFileSystem` не имеет async-API. Токен проверяется между записями, чтобы отмена (NFR-4) не ждала конца очистки.

### `TemporaryFolderCleaner.cs`

```csharp
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Runs;

/// <summary>
/// Implements <see cref="ITemporaryFolderCleaner"/> on <see cref="IFileSystem"/>.
/// </summary>
/// <remarks>
/// The folder holds only archives under construction, so anything in it when a run starts is garbage. A folder that
/// is a junction or a symbolic link is left alone, because emptying it would delete files outside the sources root.
/// </remarks>
/// <param name="fileSystem">Lists and deletes the entries.</param>
/// <param name="logger">The logger.</param>
public sealed class TemporaryFolderCleaner(
    IFileSystem fileSystem,
    ILogger<TemporaryFolderCleaner> logger) : ITemporaryFolderCleaner
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

### `TemporaryFolderCleanerLog.cs`

Тексты сообщений остаются прежними, а `EventId` в этом классе начинаются с 1.

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

    /// <summary>Logs a leftover that cannot be deleted; the next run tries again.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The file or folder.</param>
    /// <param name="exception">The reason.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Cannot delete the leftover temporary archive {Path}")]
    public static partial void EntryNotDeleted(ILogger logger, string path, Exception exception);

    /// <summary>Logs that the folder cannot be listed, so nothing is deleted.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The folder.</param>
    /// <param name="exception">The reason.</param>
    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Cannot list the temporary archive folder {Path}")]
    public static partial void FolderNotListed(ILogger logger, string path, Exception exception);

    /// <summary>Logs that the folder is a junction or a symbolic link, so it is not emptied.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The folder.</param>
    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "The temporary archive folder {Path} is a link and is not emptied")]
    public static partial void FolderIsLink(ILogger logger, string path);
}
```

## Регистрация и вызов

**Регистрация** идёт в `CoreServiceCollectionExtensions.AddGitHubBackupCore`, рядом с остальными сервисами:

```csharp
services.TryAddSingleton<ITemporaryFolderCleaner, TemporaryFolderCleaner>();
```

Также нужно добавить `<see cref="ITemporaryFolderCleaner"/>` в `<summary>` метода. `IFileSystem` уже регистрируется, потому что его требует и сейчас `BackupOrchestrator`.

**Вызов** меняется в `BackupOrchestrator`. Он больше не знает про `IFileSystem`:

```diff
-using System.IO.Abstractions;
 using GitHubBackup.Core.Archiving;
@@ class summary params
-/// <param name="fileSystem">Empties the folder of temporary archives.</param>
+/// <param name="temporaryFolderCleaner">Empties the folder of temporary archives.</param>
@@ primary constructor
-    IFileSystem fileSystem,
+    ITemporaryFolderCleaner temporaryFolderCleaner,
@@ RunLockedAsync (the else branch of the dry-run check)
-            EmptyTemporaryFolder(temporaryFolder);
+            temporaryFolderCleaner.Clean(temporaryFolder, cancellationToken);
@@
-    private void EmptyTemporaryFolder(string folder) { ... }   // whole method with its summary, lines 422-476
```

Три вещи в оркестраторе не меняются:
- Порядок «токен → блокировка → очистка → обнаружение» остаётся (шаг 4 раздела 9 PRD).
- Решение «в dry-run не чистить» (FR-9.5) и лог `TemporaryFolderLeftUntouched` остаются там. Это решение запуска, а не сервиса.
- `OperationCanceledException` из `Clean` ловит уже существующий `catch` в `RunAsync`.

В `BackupOrchestratorLog` удаляются события 2, 3, 4 и 10, а в их summary остаётся только оркестратор. Номера не сдвигаются, пропуски допустимы.

## Тесты

### Новый `TemporaryFolderCleanerTests.cs`

Тесты используют `MockFileSystem` и `FakeLogger<TemporaryFolderCleaner>`. Детальные проверки очистки переезжают сюда из оркестратора.

```csharp
using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Runs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace GitHubBackup.Core.Tests.Runs;

/// <summary>
/// Tests <see cref="TemporaryFolderCleaner"/> on an in-memory file system.
/// </summary>
public sealed class TemporaryFolderCleanerTests
{
    private const string _folder = @"C:\sources\.tmp";

    private const int _entryDeletedEventId = 1;
    private const int _entryNotDeletedEventId = 2;
    private const int _folderIsLinkEventId = 4;

    private readonly MockFileSystem _fileSystem = new();
    private readonly FakeLogger<TemporaryFolderCleaner> _logger = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public void Clean_FilesAndFolders_DeletesEachEntryAndLogsIt()
    {
        _fileSystem.AddFile(Path.Combine(_folder, "a.zip"), new("partial"));
        _fileSystem.AddFile(Path.Combine(_folder, "nested", "b.zip"), new("partial"));

        CreateCleaner().Clean(_folder, CancellationToken);

        _fileSystem.Directory.GetFileSystemEntries(_folder).ShouldBeEmpty();
        Records(_entryDeletedEventId).Count.ShouldBe(2);
    }

    [Fact]
    public void Clean_Leftovers_KeepsTheFolderItself()
    {
        _fileSystem.AddFile(Path.Combine(_folder, "a.zip"), new("partial"));

        CreateCleaner().Clean(_folder, CancellationToken);

        _fileSystem.Directory.Exists(_folder).ShouldBeTrue();
    }

    [Fact]
    public void Clean_FolderMissing_DoesNothingAndDoesNotCreateIt()
    {
        CreateCleaner().Clean(_folder, CancellationToken);

        _fileSystem.Directory.Exists(_folder).ShouldBeFalse();
        _logger.Collector.GetSnapshot().ShouldBeEmpty();
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
    public void Clean_CancelledToken_ThrowsAndDeletesNothing()
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

Тест на ошибку `GetFileSystemEntries` (событие 3) я не включил. `MockFileSystem` не умеет такой отказ, а подставной `IFileSystem` под этот один случай слишком громоздок. Это остаётся пробелом: сейчас его тоже нет.

### Изменения в `BackupOrchestratorTests.cs`

- Я не видел хелпер `CreateOrchestrator`. Предполагаю, что он создаёт оркестратор с `_fileSystem`. Заменить этот аргумент на `new TemporaryFolderCleaner(_fileSystem, NullLogger<TemporaryFolderCleaner>.Instance)`.
- Тогда остаются без правок тесты порядка и dry-run: `RunAsync_LockHeld_…` (403), `RunAsync_LockTakenAfterTokenAndBeforeTemporaryFolderCleanup` (422), `RunAsync_TemporaryFolderEmptiedBeforeDiscovery` (1082), `RunAsync_DryRun_LeavesTemporaryFolderUntouched` (1193). Они проверяют место вызова, и реальный сервис над `MockFileSystem` им подходит.
- Четыре теста деталей очистки переезжают в новый класс: `RunAsync_LeftoversInTemporaryFolder_DeletedBeforeFirstRepository`, `RunAsync_NoTemporaryFolder_ProcessesRepositories`, `RunAsync_LeftoverCannotBeDeleted_LogsWarningAndContinues`, `RunAsync_TemporaryFolderIsLink_LeavesItsContentAndContinues`. Их константы `_temporaryEntryDeletedEventId`, `_temporaryEntryNotDeletedEventId` и `_temporaryFolderIsLinkEventId` в оркестраторе удаляются.
- Добавить один тест: отмена внутри `Clean` даёт результат с `IsCancelled`. Для него подставной `ITemporaryFolderCleaner` бросает `OperationCanceledException`.
- В `CoreServiceCollectionExtensionsTests` добавить проверку, что `ITemporaryFolderCleaner` разрешается из контейнера. Я этот файл не читал, поэтому повторите там шаблон соседних проверок.

## Что нужно решить до кода

1. **PRD.** FR-7.4 и шаг 4 раздела 9 уже описывают поведение, их менять не нужно. Но таблица компонентов в §8.2 не знает про `ITemporaryFolderCleaner`. По правилам репозитория добавление строки требует новой версии PRD и записи в историю. Добавить её?
2. **Другие места создания оркестратора.** Перед правкой надо найти `new BackupOrchestrator(` (например, в `Infrastructure.IntegrationTests`) и заменить `IFileSystem` там же.
3. **Задача.** Я не знаю номер issue и не создавал ветку. По правилам репозитория ветка `<n>-<slug>` создаётся только по вашей просьбе, а коммит — только по явной команде.
