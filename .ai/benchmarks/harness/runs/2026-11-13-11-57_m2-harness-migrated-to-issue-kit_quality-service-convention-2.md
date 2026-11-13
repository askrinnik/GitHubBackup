**Ответ:** очистка `<sourcesRoot>\.tmp\` уже работает. Это приватный метод `BackupOrchestrator.EmptyTemporaryFolder` (`BackupOrchestrator.cs:432`), и он вызывается после захвата блокировки. Отдельный второй сервис написал бы дубль. Поэтому я предлагаю **вынести этот метод в сервис** `ITemporaryFolderCleaner` без изменения поведения. Файлы я не менял и код не компилировал.

Что даёт вынос:
- оркестратор перестаёт зависеть от `IFileSystem`, он использовал его только для очистки;
- очистка получает свой тестовый класс;
- поведение остаётся прежним: пропуск ссылки (junction или symlink), warning на файлы, которые не удалось удалить, и те же тексты логов.

## Схема

```
BackupOrchestrator.RunLockedAsync
  └─ (не dry-run) ITemporaryFolderCleaner.Clean(folder)
        └─ IFileSystem + ILogger<TemporaryFolderCleaner>
```

## Новые файлы в `GitHubBackup.Core/Archiving/`

`ITemporaryFolderCleaner.cs`
```csharp
namespace GitHubBackup.Core.Archiving;

/// <summary>
/// Deletes what interrupted runs left in the folder of temporary archives.
/// </summary>
public interface ITemporaryFolderCleaner
{
    /// <summary>
    /// Deletes every file and folder inside <paramref name="folder"/>, keeping the folder itself.
    /// </summary>
    /// <remarks>
    /// The caller holds the run lock, so no other run writes to the folder. An entry that cannot be deleted is
    /// logged and left for the next run. A folder that does not exist is not created. A folder that is a junction or
    /// a symbolic link is left alone, because emptying it would delete files outside the sources root.
    /// </remarks>
    /// <param name="folder">The folder of temporary archives.</param>
    void Clean(string folder);
}
```

`TemporaryFolderCleaner.cs`
```csharp
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Archiving;

/// <summary>
/// Implements <see cref="ITemporaryFolderCleaner"/> through <see cref="IFileSystem"/>.
/// </summary>
/// <param name="fileSystem">Lists and deletes the entries of the folder.</param>
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
    /// Deletes one file or folder; a failure is logged and the entry stays for the next run, whose temporary
    /// archive names are unique and so do not collide with it.
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

`TemporaryFolderCleanerLog.cs`. Тексты сообщений взяты из `BackupOrchestratorLog`.
```csharp
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Archiving;

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

    /// <summary>Logs that the folder cannot be listed and so is not emptied.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The folder.</param>
    /// <param name="exception">The reason.</param>
    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Cannot list the temporary archive folder {Path}")]
    public static partial void FolderNotListed(ILogger logger, string path, Exception exception);

    /// <summary>Logs that the folder is a junction or a symbolic link and so is not emptied.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The folder.</param>
    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "The temporary archive folder {Path} is a link and is not emptied")]
    public static partial void FolderIsLink(ILogger logger, string path);
}
```

## Изменения в существующих файлах

**`BackupOrchestrator.cs`**
- Удалить метод `EmptyTemporaryFolder` и `using System.IO.Abstractions`.
- В первичном конструкторе заменить `IFileSystem fileSystem` на `ITemporaryFolderCleaner temporaryFolderCleaner`. То же в `<param>`: `Empties the folder of temporary archives.`
- В `RunLockedAsync` заменить вызов:
```csharp
        else
        {
            temporaryFolderCleaner.Clean(temporaryFolder);
        }
```
Порядок остаётся прежним. Блокировка берётся раньше (строка 108), в dry-run очистки нет (FR-9.5), а ветка dry-run по-прежнему пишет `TemporaryFolderLeftUntouched`.

**`BackupOrchestratorLog.cs`**
- Удалить `TemporaryEntryDeleted` (id 2), `TemporaryEntryNotDeleted` (3), `TemporaryFolderNotListed` (4) и `TemporaryFolderIsLink` (10). Остальные id не перенумеровывать.

**`CoreServiceCollectionExtensions.cs`**
```csharp
            services.TryAddSingleton<ITemporaryFolderCleaner, TemporaryFolderCleaner>();
```
Строку нужно вставить перед `BackupOrchestrator`. В `<summary>` метода нужно добавить `<see cref="ITemporaryFolderCleaner"/>`. `IFileSystem` и `ILogger<>` хосты уже регистрируют: их требуют `ChangeDetector` и оркестратор.

## Тесты

**`GitHubBackup.Core.Tests/Archiving/TemporaryFolderCleanerTests.cs`**
```csharp
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Archiving;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GitHubBackup.Core.Tests.Archiving;

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

    [Fact]
    public void Clean_FilesAndSubfolder_DeletesThemAndKeepsFolder()
    {
        _fileSystem.AddFile(Path.Combine(_folder, "a.zip"), new("partial"));
        _fileSystem.AddFile(Path.Combine(_folder, "nested", "b.zip"), new("partial"));

        Cleaner().Clean(_folder);

        _fileSystem.Directory.Exists(_folder).ShouldBeTrue();
        _fileSystem.Directory.GetFileSystemEntries(_folder).ShouldBeEmpty();
        Records(_entryDeletedEventId).Count.ShouldBe(2);
    }

    [Fact]
    public void Clean_FolderDoesNotExist_DoesNotCreateItOrLog()
    {
        Cleaner().Clean(_folder);

        _fileSystem.Directory.Exists(_folder).ShouldBeFalse();
        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public void Clean_EmptyFolder_DoesNotLog()
    {
        _fileSystem.AddDirectory(_folder);

        Cleaner().Clean(_folder);

        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public void Clean_FileCannotBeDeleted_LogsWarningAndDeletesTheRest()
    {
        var locked = Path.Combine(_folder, "locked.zip");
        var other = Path.Combine(_folder, "other.zip");
        _fileSystem.AddFile(locked, new("partial") { AllowedFileShare = FileShare.None });
        _fileSystem.AddFile(other, new("partial"));

        Cleaner().Clean(_folder);

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

        Cleaner().Clean(_folder);

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
        fileSystem.Directory.GetFileSystemEntries(_folder).Throws(new IOException("denied"));

        new TemporaryFolderCleaner(fileSystem, _logger).Clean(_folder);

        var record = Records(_folderNotListedEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(_folder);
    }

    [Fact]
    public void Clean_SiblingFolders_AreNotTouched()
    {
        var clone = Path.Combine(@"C:\sources\owner\repo", "file.txt");
        _fileSystem.AddFile(clone, new("clone"));
        _fileSystem.AddFile(Path.Combine(_folder, "a.zip"), new("partial"));

        Cleaner().Clean(_folder);

        _fileSystem.File.Exists(clone).ShouldBeTrue();
    }

    private TemporaryFolderCleaner Cleaner() => new(_fileSystem, _logger);

    private IReadOnlyList<FakeLogRecord> Records(int eventId) =>
        [.. _logger.Collector.GetSnapshot().Where(record => record.Id.Id == eventId)];
}
```

**`CoreServiceCollectionExtensionsTests.cs`**: добавить тест.
```csharp
    [Fact]
    public void AddGitHubBackupCore_TemporaryFolderCleaner_IsResolvedAsSingleton()
    {
        using var provider = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IFileSystem>(new MockFileSystem())
            .AddGitHubBackupCore()
            .BuildServiceProvider();

        var cleaner = provider.GetRequiredService<ITemporaryFolderCleaner>();

        cleaner.ShouldBeOfType<TemporaryFolderCleaner>();
        provider.GetRequiredService<ITemporaryFolderCleaner>().ShouldBeSameAs(cleaner);
    }
```
В тесте `..._BackupOrchestrator_IsRecordingDecoratorAsSingleton` ничего менять не нужно: `IFileSystem` там уже зарегистрирован.

**`BackupOrchestratorTests.cs`**: здесь нужна правка. Я не читал строки, где тест создаёт оркестратор (метод `RunAsync()` и конструктор `new BackupOrchestrator(...)`), поэтому точные правки там не показываю.
- Добавить поле `private readonly ITemporaryFolderCleaner _cleaner = Substitute.For<ITemporaryFolderCleaner>();` и передавать его вместо `_fileSystem`.
- Тесты, которые проверяют сами файловые операции, переезжают в `TemporaryFolderCleanerTests`: `LeftoversInTemporaryFolder_DeletedBeforeFirstRepository`, `NoTemporaryFolder_ProcessesRepositories`, `LeftoverCannotBeDeleted_LogsWarningAndContinues` и `TemporaryFolderIsLink_*`. Константы `_temporaryEntryDeletedEventId`, `_temporaryEntryNotDeletedEventId` и `_temporaryFolderIsLinkEventId` удаляются.
- Тесты `RunAsync_NoRepositories_ReturnsSuccessAndEmptiesTemporaryFolder`, `RunAsync_TemporaryFolderEmptiedBeforeDiscovery` и `RunAsync_LockTakenAfterTokenAndBeforeTemporaryFolderCleanup` переписываются на проверку вызова подставного объекта. Остальные тесты с `.tmp` (`LockHeld_...`, `DryRun_LeavesTemporaryFolderUntouched`) переписываются на `DidNotReceive`.
```csharp
    [Fact]
    public async Task RunAsync_BackupRun_CleansTemporaryFolderOnceBeforeDiscovery()
    {
        var calls = new List<string>();
        _cleaner.When(cleaner => cleaner.Clean(_temporaryFolder)).Do(_ => calls.Add("clean"));
        _discovery.DiscoverAsync(Arg.Any<BackupConfig>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls.Add("discover");
                return DiscoveryResult.Empty;
            });

        await RunAsync();

        calls.ShouldBe(["clean", "discover"]);
    }

    [Fact]
    public async Task RunAsync_LockHeld_DoesNotCleanTemporaryFolder()
    {
        _runLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns((IDisposable?)null);

        await RunAsync();

        _cleaner.DidNotReceiveWithAnyArgs().Clean(default!);
    }
```
Для dry-run тест остаётся прежним (проверка события 19), к нему добавляется `_cleaner.DidNotReceiveWithAnyArgs().Clean(default!)`.

## Что нужно решить

1. **PRD §8**: таблица интерфейсов (около строки 584) перечисляет сервисы, например `IArchiveNameBuilder`. Для `ITemporaryFolderCleaner` нужна строка, а это правка PRD с повышением версии и записью в историю. Поведение не меняется, поэтому PRD-first здесь не блокирует, но решение за вами.
2. **Асинхронность**: `Clean` синхронный, как и исходный метод, потому что `IFileSystem` синхронный. Правило `csharp.md` про `CancellationToken` для I/O формально тут нарушено. Если нужно, можно добавить токен и вызывать `ThrowIfCancellationRequested()` между записями. Это изменит поведение при отмене.

Применять правки? Коммит без вашей явной команды я не делаю.
