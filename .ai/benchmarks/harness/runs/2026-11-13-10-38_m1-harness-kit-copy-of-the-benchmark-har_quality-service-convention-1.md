**Итог:** очистка `<sourcesRoot>\.tmp\` уже работает, но живёт внутри оркестратора как приватный метод `BackupOrchestrator.EmptyTemporaryFolder` (`BackupOrchestrator.cs:432`). Вызывается он после `runLock.TryAcquireAsync` и не вызывается в dry-run (FR-9.5). Поэтому задача сводится к рефакторингу: вынести метод в отдельный сервис, не меняя поведение. Файлы я не менял, и сборку с тестами не запускал. Код ниже написан по прочитанным файлам.

```
Порядок запуска (PRD §9, п. 4):
  токен → блокировка → ITemporaryFolderCleaner.Clean → обнаружение → обработка
                       (dry-run: Clean не вызывается)
```

## Типы и файлы

Все три типа лежат в `src/GitHubBackup.Core/Runs/`. Им не нужна ни одна I/O-библиотека, кроме `IFileSystem`, который `Core` уже использует.

**`ITemporaryFolderCleaner.cs`**

```csharp
namespace GitHubBackup.Core.Runs;

/// <summary>
/// Removes the files that interrupted runs left in the folder of temporary archives.
/// </summary>
public interface ITemporaryFolderCleaner
{
    /// <summary>
    /// Deletes every file and folder inside <paramref name="folder"/>, keeping the folder itself.
    /// </summary>
    /// <remarks>
    /// The caller holds the run lock, so no other run writes to the folder. An entry that cannot be deleted, or a
    /// folder that cannot be listed, is logged and left for the next run; the method never throws for these cases.
    /// </remarks>
    /// <param name="folder">The folder of temporary archives. A missing folder is not an error.</param>
    void Clean(string folder);
}
```

**`TemporaryFolderCleaner.cs`** — это тело старого метода, перенесённое без изменений логики.

```csharp
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Runs;

/// <summary>
/// Implements <see cref="ITemporaryFolderCleaner"/> over <see cref="IFileSystem"/>.
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
            Delete(entry);
        }
    }

    /// <summary>
    /// Deletes one file or folder and logs the outcome; a failure leaves the entry for the next run.
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

**`TemporaryFolderCleanerLog.cs`** — сюда переезжают четыре записи лога из `BackupOrchestratorLog`. Я оставляю те же `EventId` (2, 3, 4, 10) и тексты, чтобы журналы не изменились. В `BackupOrchestratorLog` эти четыре метода удаляются.

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
    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Deleted the leftover temporary archive {Path}")]
    public static partial void EntryDeleted(ILogger logger, string path);

    /// <summary>Logs a leftover that cannot be deleted and stays for the next run.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The file or folder.</param>
    /// <param name="exception">The reason.</param>
    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Cannot delete the leftover temporary archive {Path}")]
    public static partial void EntryNotDeleted(ILogger logger, string path, Exception exception);

    /// <summary>Logs that the folder cannot be listed and is not emptied.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The folder.</param>
    /// <param name="exception">The reason.</param>
    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Cannot list the temporary archive folder {Path}")]
    public static partial void FolderNotListed(ILogger logger, string path, Exception exception);

    /// <summary>Logs that the folder is a junction or a symbolic link and is not emptied.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The folder.</param>
    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Warning,
        Message = "The temporary archive folder {Path} is a link and is not emptied")]
    public static partial void FolderIsLink(ILogger logger, string path);
}
```

## Регистрация и вызов

**Регистрация.** В `CoreServiceCollectionExtensions.cs` добавляется одна строка рядом с остальными, перед `BackupOrchestrator`:

```csharp
services.TryAddSingleton<ITemporaryFolderCleaner, TemporaryFolderCleaner>();
```

`IFileSystem` регистрирует `Infrastructure`, как и сейчас для оркестратора. Тест `CoreServiceCollectionExtensionsTests` уже подставляет `IFileSystem`, потому что оркестратору он нужен.

**Вызов.** В `BackupOrchestrator.cs` меняются четыре места:

1. Параметр конструктора `IFileSystem fileSystem` заменяется на `ITemporaryFolderCleaner temporaryFolderCleaner` на том же месте.
2. Строка `<param name="fileSystem">` заменяется на `<param name="temporaryFolderCleaner">Empties the folder of temporary archives.</param>`.
3. В `RunLockedAsync` ветка `else` вызывает сервис вместо приватного метода:
   ```csharp
   else
   {
       temporaryFolderCleaner.Clean(temporaryFolder);
   }
   ```
4. Метод `EmptyTemporaryFolder` удаляется целиком. Больше нигде в классе `fileSystem` не используется, поэтому `using System.IO.Abstractions;` тоже удаляется.

## Тесты

**Новый класс** `src/GitHubBackup.Core.Tests/Runs/TemporaryFolderCleanerTests.cs`. В нём `MockFileSystem` и `FakeLogger<TemporaryFolderCleaner>`, а записи ищутся по `EventId`.

```csharp
public sealed class TemporaryFolderCleanerTests
{
    private const string _folder = @"C:\sources\.tmp";
    private const int _entryDeletedEventId = 2;
    private const int _entryNotDeletedEventId = 3;
    private const int _folderNotListedEventId = 4;
    private const int _folderIsLinkEventId = 10;

    private readonly MockFileSystem _fileSystem = new();
    private readonly FakeLogger<TemporaryFolderCleaner> _logger = new();

    [Fact]
    public void Clean_FilesAndFolders_DeletesThemAndKeepsTheFolder()
    {
        _fileSystem.AddFile(Path.Combine(_folder, "a.zip"), new("partial"));
        _fileSystem.AddFile(Path.Combine(_folder, "nested", "b.zip"), new("partial"));

        Create().Clean(_folder);

        _fileSystem.Directory.Exists(_folder).ShouldBeTrue();
        _fileSystem.Directory.GetFileSystemEntries(_folder).ShouldBeEmpty();
        Records(_entryDeletedEventId).Count.ShouldBe(2);
    }

    [Fact]
    public void Clean_MissingFolder_DoesNothing()
    {
        Create().Clean(_folder);

        _fileSystem.Directory.Exists(_folder).ShouldBeFalse();
        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public void Clean_EmptyFolder_KeepsItWithoutLogging()
    {
        _fileSystem.AddDirectory(_folder);

        Create().Clean(_folder);

        _fileSystem.Directory.Exists(_folder).ShouldBeTrue();
        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public void Clean_FileCannotBeDeleted_LogsWarningLeavesItAndDeletesTheRest()
    {
        var locked = Path.Combine(_folder, "locked.zip");
        _fileSystem.AddFile(locked, new("partial") { AllowedFileShare = FileShare.None });
        _fileSystem.AddFile(Path.Combine(_folder, "free.zip"), new("partial"));

        Create().Clean(_folder);

        _fileSystem.File.Exists(locked).ShouldBeTrue();
        _fileSystem.File.Exists(Path.Combine(_folder, "free.zip")).ShouldBeFalse();
        var record = Records(_entryNotDeletedEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(locked);
    }

    [Fact]
    public void Clean_FolderIsLink_LeavesItsContentAndLogsWarning()
    {
        var leftover = Path.Combine(_folder, "a.zip");
        _fileSystem.AddFile(leftover, new("partial"));
        _fileSystem.DirectoryInfo.New(_folder).Attributes |= FileAttributes.ReparsePoint;

        Create().Clean(_folder);

        _fileSystem.File.Exists(leftover).ShouldBeTrue();
        var record = Records(_folderIsLinkEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(_folder);
    }

    [Fact]
    public void Clean_FolderCannotBeListed_LogsWarningAndDoesNotThrow()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.Directory.Exists(_folder).Returns(true);
        fileSystem.DirectoryInfo.New(_folder).Attributes.Returns(FileAttributes.Directory);
        fileSystem.Directory.GetFileSystemEntries(_folder).Throws(new IOException("The disk is not ready."));

        new TemporaryFolderCleaner(fileSystem, _logger).Clean(_folder);

        var record = Records(_folderNotListedEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(_folder);
    }

    private TemporaryFolderCleaner Create() => new(_fileSystem, _logger);

    private IReadOnlyList<FakeLogRecord> Records(int eventId) =>
        [.. _logger.Collector.GetSnapshot().Where(record => record.Id.Id == eventId)];
}
```

**Тесты оркестратора** (`Core.Tests/Runs/BackupOrchestratorTests.cs`):
- Поле `MockFileSystem _fileSystem` заменяется на `ITemporaryFolderCleaner _cleaner = Substitute.For<ITemporaryFolderCleaner>()`.
- Тесты, которые проверяли файлы и записи лога удаления (около строк 272, 502, 529, 544, 1082), переезжают в `TemporaryFolderCleanerTests` или превращаются в проверки вызова сервиса. Константы `EventId` 2, 3 и 10 в этом классе удаляются.
- Ниже набросок новых тестов. Порядок «блокировка, затем очистка, затем обнаружение» проверяется списком событий.

```csharp
[Fact]
public async Task RunAsync_Backup_CleansTemporaryFolderOnce()
{
    await RunAsync();

    _cleaner.Received(1).Clean(_temporaryFolder);
}

[Fact]
public async Task RunAsync_LockHeld_DoesNotCleanTemporaryFolder()
{
    _runLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns((IDisposable?)null);

    await RunAsync();

    _cleaner.DidNotReceiveWithAnyArgs().Clean(default!);
}

[Fact]
public async Task RunAsync_DryRun_DoesNotCleanTemporaryFolder()
{
    await RunAsync(dryRun: true);

    _cleaner.DidNotReceiveWithAnyArgs().Clean(default!);
    SingleRecord(_temporaryFolderLeftUntouchedEventId).GetStructuredStateValue("Path").ShouldBe(_temporaryFolder);
}

[Theory]
[InlineData(false)]
[InlineData(true)]
public async Task RunAsync_LockIsTakenBeforeCleanupAndCleanupBeforeDiscovery(bool dryRun)
{
    List<string> order = [];
    _runLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns(_ => { order.Add("lock"); return _runLockHandle; });
    _cleaner.When(cleaner => cleaner.Clean(Arg.Any<string>())).Do(_ => order.Add("clean"));
    _discovery.DiscoverAsync(Arg.Any<BackupConfig>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
        .Returns(_ => { order.Add("discover"); return DiscoveryResult.Empty; });

    await RunAsync(dryRun);

    order.ShouldBe(dryRun ? ["lock", "discover"] : ["lock", "clean", "discover"]);
}
```

Сигнатуру `RunAsync(dryRun)` я не проверял. Помощник в этом классе может называться иначе, подгоните вызов под него.

**Интеграционный тест** (`Infrastructure.IntegrationTests/Runs/BackupOrchestratorTests.cs`, строка 633). Аргумент `_fileSystem` в конструкторе оркестратора заменяется на:

```csharp
new TemporaryFolderCleaner(_fileSystem, NullLogger<TemporaryFolderCleaner>.Instance),
```

## Что нужно решить вам

- **PRD.** Поведение не меняется, поэтому по правилу «PRD first» правка PRD не обязательна. Но в §8.2 стоит добавить строку `ITemporaryFolderCleaner | Очистка <sourcesRoot>\.tmp\ после захвата блокировки (FR-7.4)`. Это поднимет версию PRD и добавит строку в историю изменений. Добавлять ли строку?
- **Синхронность.** `Clean` синхронный и без `CancellationToken`, как и старый метод. `IFileSystem` синхронный, поэтому `async` был бы фиктивным. Если правило про токен нужно соблюсти строго, можно добавить `CancellationToken` и проверять его между записями.
- **Задача и план.** Для такого рефакторинга нужна задача по правилам репозитория, плюс файл в `docs/plans/`. Я не нашёл ни той, ни другого, и ничего не создавал.
