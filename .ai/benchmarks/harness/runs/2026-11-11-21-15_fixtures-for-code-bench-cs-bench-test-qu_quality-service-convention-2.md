**Итог.** Очистка по FR-7.4 в коде уже есть. Это приватный метод `BackupOrchestrator.EmptyTemporaryFolder` (`BackupOrchestrator.cs:243`). У него есть тесты и четыре записи лога. Поэтому «добавить сервис» здесь означает вынести этот метод в отдельный сервис с тем же поведением. Ниже код такого выноса. Я ничего не менял.

**Блокировки запуска в коде нет.** Поиск по `Mutex`, `RunLock` и `NFR-SYS-1` в `src` ничего не находит. Оркестратор чистит `.tmp` после получения токена, но не после захвата блокировки. Сейчас два одновременных запуска (CLI и UI) могут удалить недописанные архивы друг друга. Сервис ниже безопасен только под блокировкой, и его контракт это требует. Я предлагаю завести отдельный issue на NFR-SYS-1. Вызов `Clean` должен стоять сразу после захвата блокировки.

## Типы и файлы

Все три файла лежат в `src/GitHubBackup.Core/Runs/`. `Core` уже использует `IFileSystem`, так что новых зависимостей нет.

`ITemporaryFolderCleaner.cs`
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
    /// The caller must hold the run lock, so that no other run writes to the folder. An entry that cannot be
    /// deleted, for example a file that a stopped 7-Zip process still holds, is logged and left for the next run.
    /// A missing folder is not an error. A folder that is a junction or a symbolic link is not emptied.
    /// </remarks>
    /// <param name="folder">The absolute folder of temporary archives.</param>
    void Clean(string folder);
}
```

`TemporaryFolderCleaner.cs`
```csharp
using System.IO.Abstractions;

namespace GitHubBackup.Core.Runs;

/// <summary>
/// Implements <see cref="ITemporaryFolderCleaner"/> on top of <see cref="IFileSystem"/>.
/// </summary>
/// <remarks>
/// The folder holds only archives under construction, so anything in it at the start of a run is garbage. A leftover
/// that stays does not block new archives, whose temporary names are unique. A link is left alone because emptying
/// it would delete files outside the sources root.
/// </remarks>
/// <param name="fileSystem">Lists and deletes the entries.</param>
/// <param name="logger">The logger.</param>
public sealed class TemporaryFolderCleaner(
    IFileSystem fileSystem,
    ILogger<TemporaryFolderCleaner> logger) : ITemporaryFolderCleaner
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
                TemporaryFolderCleanerLog.EntryNotDeleted(logger, entry, exception);
            }
        }
    }
}
```
В файле нужен `using Microsoft.Extensions.Logging;`. Как и в оркестраторе, метод синхронный: `IFileSystem` не имеет async-API. Токен отмены я не добавлял, чтобы поведение не изменилось.

`TemporaryFolderCleanerLog.cs` — те же четыре сообщения, что сейчас в `BackupOrchestratorLog`, но с собственными EventId:
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

    /// <summary>Logs that the folder cannot be listed and is not emptied.</summary>
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

## Изменения в существующих файлах

**`BackupOrchestrator.cs`:**
- Удалить `using System.IO.Abstractions;`. `fileSystem` нужен только для очистки.
- В `<param>` заменить `fileSystem` на `<param name="temporaryFolderCleaner">Empties the folder of temporary archives.</param>`.
- В конструкторе заменить `IFileSystem fileSystem,` на `ITemporaryFolderCleaner temporaryFolderCleaner,`. Позиция та же.
- Строку 95 заменить:
  ```csharp
  temporaryFolderCleaner.Clean(temporaryFolder);
  ```
- Удалить метод `EmptyTemporaryFolder` (строки 233–287) целиком.

**`BackupOrchestratorLog.cs`:** удалить записи `TemporaryEntryDeleted`, `TemporaryEntryNotDeleted`, `TemporaryFolderNotListed`, `TemporaryFolderIsLink` (EventId 2, 3, 4, 10). Эти номера больше не использовать.

**`CoreServiceCollectionExtensions.cs`:** регистрация рядом с остальными синглтонами. Тип без состояния, поэтому `Singleton`. Перечень в `<summary>` метода тоже дополнить.
```csharp
services.TryAddSingleton<ITemporaryFolderCleaner, TemporaryFolderCleaner>();
```

**Как вызывается.** Оркестратор вызывает `Clean(temporaryFolder)` на том же месте, что и сейчас: после токена и до обнаружения. Папку он по-прежнему получает из `pathResolver.GetTemporaryArchiveFolder(config)`. Хосты (`Cli`, `App`) ничего не меняют. Когда появится блокировка, её захват встанет прямо перед этой строкой.

## Тесты

Новый файл `src/GitHubBackup.Core.Tests/Runs/TemporaryFolderCleanerTests.cs`. Он заменяет проверки файловой системы и логов из оркестраторных тестов.
```csharp
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Runs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;

namespace GitHubBackup.Core.Tests.Runs;

/// <summary>
/// Tests <see cref="TemporaryFolderCleaner"/>.
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
    public void Clean_FolderMissing_DoesNothing()
    {
        CreateCleaner().Clean(_folder);

        _fileSystem.Directory.Exists(_folder).ShouldBeFalse();
        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public void Clean_FilesAndNestedFolders_DeletesAllAndKeepsFolder()
    {
        _fileSystem.AddFile(Path.Combine(_folder, "a.zip"), new("partial"));
        _fileSystem.AddFile(Path.Combine(_folder, "nested", "b.zip"), new("partial"));

        CreateCleaner().Clean(_folder);

        _fileSystem.Directory.Exists(_folder).ShouldBeTrue();
        _fileSystem.Directory.GetFileSystemEntries(_folder).ShouldBeEmpty();
        Records(_entryDeletedEventId).Count.ShouldBe(2);
    }

    [Fact]
    public void Clean_FileCannotBeDeleted_LogsWarningAndDeletesTheOthers()
    {
        var locked = Path.Combine(_folder, "locked.zip");
        var other = Path.Combine(_folder, "other.zip");
        _fileSystem.AddFile(locked, new("partial") { AllowedFileShare = FileShare.None });
        _fileSystem.AddFile(other, new("partial"));

        CreateCleaner().Clean(_folder);

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

        CreateCleaner().Clean(_folder);

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

        new TemporaryFolderCleaner(fileSystem, _logger).Clean(_folder);

        var record = Records(_folderNotListedEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(_folder);
    }

    private TemporaryFolderCleaner CreateCleaner() => new(_fileSystem, _logger);

    private IReadOnlyList<FakeLogRecord> Records(int eventId) =>
        [.. _logger.Collector.GetSnapshot().Where(record => record.Id.Id == eventId)];
}
```

**Оркестраторные тесты.** В `BackupOrchestratorTests` поле `_fileSystem` остаётся для других нужд, а очиститель становится подменой `Substitute.For<ITemporaryFolderCleaner>()` в конструкторе оркестратора. Я не читал конструктор фикстуры, поэтому правка в нём ограничена заменой аргумента.

- Тесты `RunAsync_LeftoversInTemporaryFolder_DeletedBeforeFirstRepository`, `RunAsync_LeftoverCannotBeDeleted_LogsWarningAndContinues` и `RunAsync_TemporaryFolderIsLink_LeavesItsContentAndContinues` удалить: их проверки переехали в `TemporaryFolderCleanerTests`.
- Тест `RunAsync_TemporaryFolderEmptiedBeforeDiscovery` заменить проверкой порядка вызовов:
```csharp
[Fact]
public async Task RunAsync_Started_CleansTemporaryFolderBeforeDiscovery()
{
    var order = new List<string>();
    _cleaner.When(cleaner => cleaner.Clean(_temporaryFolder)).Do(_ => order.Add("clean"));
    _discovery.DiscoverAsync(Arg.Any<BackupConfig>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(_ =>
    {
        order.Add("discover");
        return DiscoveryResult.Empty;
    });

    await RunAsync();

    order.ShouldBe(["clean", "discover"]);
}
```
- Остальные тесты оркестратора проверяют `RunAsync_NoTemporaryFolder_ProcessesRepositories` и ему подобное. Их можно оставить на подмене.

**Тест регистрации** в `CoreServiceCollectionExtensionsTests`:
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
Тест `AddGitHubBackupCore_BackupOrchestrator_IsResolved` менять не нужно: `IFileSystem` в нём остаётся для других сервисов.

## Что ещё проверить и согласовать

- В `Infrastructure.IntegrationTests/Runs/BackupOrchestratorTests.cs` оркестратор тоже собирается. Я не смотрел, как именно. Если там `new BackupOrchestrator(...)`, нужно подставить настоящий `TemporaryFolderCleaner`. Если там DI, правка не нужна.
- Логи очистки теперь пишутся под категорией `TemporaryFolderCleaner`, а не `BackupOrchestrator`. Если Serilog-фильтры или документация привязаны к старой категории, их надо поправить.
- В PRD §8.2 есть таблица сервисов. Строка `ITemporaryFolderCleaner` будет новой. Это правка PRD (версия 0.13 и строка в истории), и по правилам репозитория она идёт до кода. Мне нужно ваше согласие.
- Для работы нужны issue, план `docs/plans/<type>-<issue>-<slug>.md`, ветка и коммит. Всё это ждёт вашего явного указания. Блокировку NFR-SYS-1 я предлагаю вести отдельным issue.

Создать issue и ветку и применить эти правки?
