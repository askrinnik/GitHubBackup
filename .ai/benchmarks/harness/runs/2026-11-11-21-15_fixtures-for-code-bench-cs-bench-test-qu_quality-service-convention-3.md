Я прочитал PRD, оркестратор и его тесты. Ниже код, ничего не записано. Сначала два факта, которые меняют задачу.

## Что уже есть в коде

1. **Очистка уже работает, но внутри оркестратора.** Её делает приватный метод `BackupOrchestrator.EmptyTemporaryFolder`. У него есть тесты и четыре записи лога (EventId 2, 3, 4, 10 в `BackupOrchestratorLog`). Поэтому «добавить сервис» значит **вынести** этот метод в отдельный сервис. Поведение остаётся прежним.
2. **Блокировки запуска (NFR-SYS-1) в коде нет.** В `src/**/*.cs` нет ни `NFR-SYS-1`, ни `Mutex`. Сейчас очистка идёт после получения токена и без блокировки. Если второй экземпляр программы стартует во время первого, он удалит временный архив первого. FR-7.4 допускает очистку только под блокировкой.
   - Блокировку я здесь не проектирую: это отдельная задача, и её надо завести как issue.
   - Когда блокировка появится, её берут непосредственно перед `Clean`. Место вызова ниже на это рассчитано.

PRD менять не нужно: v0.12 уже описывает это поведение. Для задачи нужен файл плана `docs/plans/<type>-<issue>-<slug>.md`.

## Файлы

```
src/GitHubBackup.Core/Archiving/ITemporaryFolderCleaner.cs        новый
src/GitHubBackup.Core/Archiving/TemporaryFolderCleaner.cs         новый
src/GitHubBackup.Core/Archiving/TemporaryFolderCleanerLog.cs      новый
src/GitHubBackup.Core/Runs/BackupOrchestrator.cs                  изменить
src/GitHubBackup.Core/Runs/BackupOrchestratorLog.cs               убрать 4 записи
src/GitHubBackup.Core/CoreServiceCollectionExtensions.cs          регистрация
src/GitHubBackup.Core.Tests/Archiving/TemporaryFolderCleanerTests.cs   новый
src/GitHubBackup.Core.Tests/Runs/BackupOrchestratorTests.cs            изменить
src/GitHubBackup.Core.Tests/CoreServiceCollectionExtensionsTests.cs    один тест
```

Сервис лежит в `Core`, как `CloneRelocator`: `Core` уже ссылается на `System.IO.Abstractions`.

Метод принимает `BackupConfig`, а не путь. Так сервис может очистить только `<sourcesRoot>\.tmp` и не может удалить произвольную папку. Метод синхронный: у `IFileSystem` нет async API. Токен отмены он проверяет перед каждой записью.

## Код

`ITemporaryFolderCleaner.cs`

```csharp
using GitHubBackup.Core.BackupConfiguration;

namespace GitHubBackup.Core.Archiving;

/// <summary>
/// Removes the files that interrupted runs left in the folder of temporary archives.
/// </summary>
public interface ITemporaryFolderCleaner
{
    /// <summary>
    /// Deletes every file and folder in <c>&lt;sourcesRoot&gt;\.tmp</c>.
    /// </summary>
    /// <remarks>
    /// The caller holds the run lock, so no other run writes to the folder. An entry that cannot be deleted, for
    /// example because a stopped 7-Zip process still holds it, is logged and left for the next run. A folder that is a
    /// junction or a symbolic link is left alone, because emptying it would delete files outside the sources root.
    /// </remarks>
    /// <param name="config">The backup configuration, which supplies the sources root.</param>
    /// <param name="cancellationToken">Cancels the clean-up between two entries.</param>
    /// <exception cref="OperationCanceledException">The clean-up is cancelled.</exception>
    void Clean(BackupConfig config, CancellationToken cancellationToken);
}
```

`TemporaryFolderCleaner.cs`

```csharp
using System.IO.Abstractions;
using GitHubBackup.Core.BackupConfiguration;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Archiving;

/// <inheritdoc />
/// <param name="fileSystem">Lists and deletes the entries of the folder.</param>
/// <param name="pathResolver">Resolves the folder of temporary archives.</param>
/// <param name="logger">The logger.</param>
public sealed class TemporaryFolderCleaner(
    IFileSystem fileSystem,
    IRepositoryPathResolver pathResolver,
    ILogger<TemporaryFolderCleaner> logger) : ITemporaryFolderCleaner
{
    /// <inheritdoc />
    public void Clean(BackupConfig config, CancellationToken cancellationToken)
    {
        var folder = pathResolver.GetTemporaryArchiveFolder(config);
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
    /// Deletes one file or folder; a failure is logged and the entry stays for the next run.
    /// </summary>
    /// <param name="entry">The file or folder in the folder of temporary archives.</param>
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

`TemporaryFolderCleanerLog.cs` — тексты сообщений остаются прежними, EventId начинаются с 1.

```csharp
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Archiving;

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

`CoreServiceCollectionExtensions.cs`: одна строка рядом с `IArchiveNameBuilder`. В `<summary>` метода добавьте `<see cref="ITemporaryFolderCleaner"/>`. Нужные `using` в файле уже есть.

```csharp
services.TryAddSingleton<ITemporaryFolderCleaner, TemporaryFolderCleaner>();
```

`IFileSystem` и `IRepositoryPathResolver` регистрирует `AddGitHubBackupInfrastructure`, поэтому в хостах менять ничего не нужно.

`BackupOrchestrator.cs`:
- Параметр `IFileSystem fileSystem` заменить на `ITemporaryFolderCleaner temporaryFolderCleaner` в том же месте. Строку `using System.IO.Abstractions;` и `<param name="fileSystem">` убрать: других обращений к `fileSystem` в классе нет.
- Метод `EmptyTemporaryFolder` удалить целиком.
- Вызов заменить. Папка `temporaryFolder` остаётся, она нужна архиватору.

```csharp
var token = await tokenProvider.GetTokenAsync(cancellationToken);
var temporaryFolder = pathResolver.GetTemporaryArchiveFolder(config);
temporaryFolderCleaner.Clean(config, cancellationToken);
```

`BackupOrchestratorLog.cs`: удалить методы с EventId 2, 3, 4 и 10. Остальные EventId не менять: на них ссылаются тесты.

## Тесты

`Core.Tests/Archiving/TemporaryFolderCleanerTests.cs`. Резолвер настоящий, файловая система в памяти.

```csharp
using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Archiving;
using GitHubBackup.Core.BackupConfiguration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace GitHubBackup.Core.Tests.Archiving;

/// <summary>
/// Tests <see cref="TemporaryFolderCleaner"/> with an in-memory file system and the real path resolver.
/// </summary>
public sealed class TemporaryFolderCleanerTests
{
    private const string _temporaryFolder = @"C:\sources\.tmp";

    private const int _entryDeletedEventId = 1;
    private const int _entryNotDeletedEventId = 2;
    private const int _folderIsLinkEventId = 4;

    private readonly MockFileSystem _fileSystem = new();
    private readonly FakeLogger<TemporaryFolderCleaner> _logger = new();
    private readonly BackupConfig _config = new() { BackupRoot = @"C:\backup", SourcesRoot = @"C:\sources" };
    private readonly TemporaryFolderCleaner _cleaner;

    public TemporaryFolderCleanerTests()
    {
        _cleaner = new(_fileSystem, new RepositoryPathResolver(@"C:\app\sources"), _logger);
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public void Clean_FilesAndFolders_DeletesAllAndLogsEach()
    {
        _fileSystem.AddFile(Path.Combine(_temporaryFolder, "a.zip"), new("partial"));
        _fileSystem.AddFile(Path.Combine(_temporaryFolder, "nested", "b.zip"), new("partial"));

        _cleaner.Clean(_config, CancellationToken);

        _fileSystem.Directory.GetFileSystemEntries(_temporaryFolder).ShouldBeEmpty();
        Records(_entryDeletedEventId).Select(record => record.GetStructuredStateValue("Path")).ShouldBe(
            [Path.Combine(_temporaryFolder, "a.zip"), Path.Combine(_temporaryFolder, "nested")],
            ignoreOrder: true);
    }

    [Fact]
    public void Clean_NoTemporaryFolder_DoesNothing()
    {
        _cleaner.Clean(_config, CancellationToken);

        _fileSystem.Directory.Exists(_temporaryFolder).ShouldBeFalse();
        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public void Clean_FileHeldByAnotherProcess_LogsWarningAndDeletesTheRest()
    {
        var locked = Path.Combine(_temporaryFolder, "locked.zip");
        var other = Path.Combine(_temporaryFolder, "other.zip");
        _fileSystem.AddFile(locked, new("partial") { AllowedFileShare = FileShare.None });
        _fileSystem.AddFile(other, new("partial"));

        _cleaner.Clean(_config, CancellationToken);

        _fileSystem.File.Exists(locked).ShouldBeTrue();
        _fileSystem.File.Exists(other).ShouldBeFalse();
        var record = Records(_entryNotDeletedEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(locked);
    }

    [Fact]
    public void Clean_FolderIsLink_LeavesContentAndLogsWarning()
    {
        var leftover = Path.Combine(_temporaryFolder, "a.zip");
        _fileSystem.AddFile(leftover, new("partial"));
        _fileSystem.DirectoryInfo.New(_temporaryFolder).Attributes |= FileAttributes.ReparsePoint;

        _cleaner.Clean(_config, CancellationToken);

        _fileSystem.File.Exists(leftover).ShouldBeTrue();
        var record = Records(_folderIsLinkEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(_temporaryFolder);
    }

    [Fact]
    public void Clean_SiblingFolders_AreNotTouched()
    {
        var clone = @"C:\sources\owner\repo\file.txt";
        var archive = @"C:\backup\owner\repo\old.zip";
        _fileSystem.AddFile(clone, new("code"));
        _fileSystem.AddFile(archive, new("zip"));
        _fileSystem.AddFile(Path.Combine(_temporaryFolder, "a.zip"), new("partial"));

        _cleaner.Clean(_config, CancellationToken);

        _fileSystem.File.Exists(clone).ShouldBeTrue();
        _fileSystem.File.Exists(archive).ShouldBeTrue();
    }

    [Fact]
    public void Clean_Cancelled_ThrowsAndLeavesFiles()
    {
        var leftover = Path.Combine(_temporaryFolder, "a.zip");
        _fileSystem.AddFile(leftover, new("partial"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Should.Throw<OperationCanceledException>(() => _cleaner.Clean(_config, cancellation.Token));

        _fileSystem.File.Exists(leftover).ShouldBeTrue();
    }

    private List<FakeLogRecord> Records(int eventId) =>
        [.. _logger.Collector.GetSnapshot().Where(record => record.Id.Id == eventId)];
}
```

Не покрыт путь `FolderNotListed`: `MockFileSystem` не умеет ронять `GetFileSystemEntries`. Если он нужен, понадобится подставной `IFileSystem` на NSubstitute. Старые тесты оркестратора этот путь тоже не проверяли.

`BackupOrchestratorTests.cs`. Добавьте поле `private readonly ITemporaryFolderCleaner _cleaner = Substitute.For<ITemporaryFolderCleaner>();` и передавайте его в конструктор вместо `_fileSystem`. Сам `_fileSystem`, константы `_temporaryEntryDeletedEventId`, `_temporaryEntryNotDeletedEventId`, `_temporaryFolderIsLinkEventId` и `using System.IO.Abstractions.TestingHelpers` удалите, если ими больше никто не пользуется. Что менять в тестах:

- **Удалить**, их логика переехала в тесты сервиса:
  - `RunAsync_LeftoversInTemporaryFolder_DeletedBeforeFirstRepository`
  - `RunAsync_NoTemporaryFolder_ProcessesRepositories`
  - `RunAsync_LeftoverCannotBeDeleted_LogsWarningAndContinues`
  - `RunAsync_TemporaryFolderIsLink_LeavesItsContentAndContinues`
- **`RunAsync_NoRepositories_ReturnsSuccessAndEmptiesTemporaryFolder`**: переименовать в `RunAsync_NoRepositories_ReturnsSuccess`, убрать строки про файл.
- **`RunAsync_InvalidConfig_…` и `RunAsync_ToolMissing_…`**: проверка `_fileSystem.File.Exists(...)` заменяется на `_cleaner.DidNotReceiveWithAnyArgs().Clean(default!, default);`. Аналогично в тесте, где репозиторий фильтра не найден в конфигурации.
- **`RunAsync_TemporaryFolderEmptiedBeforeDiscovery`** заменить двумя тестами:

```csharp
[Fact]
public async Task RunAsync_ValidRun_CleansTemporaryFolderOnceWithTheConfiguration()
{
    await RunAsync();

    _cleaner.Received(1).Clean(_config, CancellationToken);
}

[Fact]
public async Task RunAsync_ValidRun_CleansTemporaryFolderBeforeDiscovery()
{
    var order = new List<string>();
    _cleaner.When(cleaner => cleaner.Clean(Arg.Any<BackupConfig>(), Arg.Any<CancellationToken>()))
        .Do(_ => order.Add("clean"));
    _discovery.DiscoverAsync(Arg.Any<BackupConfig>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(_ =>
    {
        order.Add("discover");
        return DiscoveryResult.Empty;
    });

    await RunAsync();

    order.ShouldBe(["clean", "discover"]);
}
```

`CoreServiceCollectionExtensionsTests.cs`. Нужен один тест вида `AddGitHubBackupCore_TemporaryFolderCleaner_IsResolvedAsSingleton`. Модель — тесты `RepositoryDiscoveryService` и `DiscoveryDecisionApplier` из этого файла (строки 126–157). Я их целиком не читал. В тесте надо подставить `IFileSystem` и `IRepositoryPathResolver` (подставные или настоящие) и проверить, что два вызова `GetRequiredService<ITemporaryFolderCleaner>()` возвращают один и тот же экземпляр.

## Дальше

После вашего «да» я внесу изменения и прогоню сборку и тесты отдельными командами. Коммит сделаю только по явной команде. Остаётся вопрос по блокировке запуска. Завести для неё отдельный issue с блокирующей связью, или она уже запланирована?
