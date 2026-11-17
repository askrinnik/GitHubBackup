**Коротко:** эта очистка уже есть. Она живёт внутри `BackupOrchestrator.EmptyTemporaryFolder` (`BackupOrchestrator.cs:923`) и вызывается после захвата блокировки (`:238`). Поэтому «добавить сервис» значит вынести этот код в отдельный тип. Поведение при этом не меняется. Файлы я не правил.

Дальше: что выносим, код, регистрация и вызов, тесты, вопрос по PRD.

## 1. Типы и файлы

```
src/GitHubBackup.Core/Cleanup/
  ITemporaryFolderCleaner.cs          (new)
  TemporaryFolderCleaner.cs           (new)
  TemporaryFolderCleanerLog.cs        (new)
src/GitHubBackup.Core/Runs/BackupOrchestrator.cs      (edit)
src/GitHubBackup.Core/Runs/BackupOrchestratorLog.cs   (edit)
src/GitHubBackup.Core/CoreServiceCollectionExtensions.cs  (edit)
src/GitHubBackup.Core.Tests/Cleanup/TemporaryFolderCleanerTests.cs  (new)
src/GitHubBackup.Core.Tests/Runs/BackupOrchestratorTests.cs         (edit)
src/GitHubBackup.Core.Tests/CoreServiceCollectionExtensionsTests.cs (edit)
```

Тип лежит в `Core`, потому что ему нужен только `IFileSystem`. Метод синхронный, как сейчас: `IFileSystem` не имеет async-API.

## 2. Код

`ITemporaryFolderCleaner.cs`:

```csharp
namespace GitHubBackup.Core.Cleanup;

/// <summary>
/// Deletes the files that interrupted runs left in the folder of temporary archives.
/// </summary>
public interface ITemporaryFolderCleaner
{
    /// <summary>
    /// Empties the folder of temporary archives.
    /// </summary>
    /// <remarks>
    /// The caller holds the run lock, which guarantees that no other run writes to the folder. An entry that cannot be
    /// deleted is logged and left for the next run; a folder that does not exist, cannot be listed or is a junction or
    /// a symbolic link is left alone. The method throws no I/O exception.
    /// </remarks>
    /// <param name="folder">The absolute folder of temporary archives.</param>
    void Clean(string folder);
}
```

`TemporaryFolderCleaner.cs` (тело перенесено из оркестратора):

```csharp
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Cleanup;

/// <summary>
/// Implements <see cref="ITemporaryFolderCleaner"/> on <see cref="IFileSystem"/>.
/// </summary>
/// <param name="fileSystem">Lists and deletes the entries of the folder.</param>
/// <param name="logger">Logs each deleted entry and each entry that stays.</param>
public sealed class TemporaryFolderCleaner(
    IFileSystem fileSystem,
    ILogger<TemporaryFolderCleaner> logger) : ITemporaryFolderCleaner
{
    /// <inheritdoc />
    /// <remarks>
    /// An undeletable entry does not block new archives, because their temporary names are unique. A link is not
    /// followed, because emptying it would delete files outside the sources root.
    /// </remarks>
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
            DeleteEntry(entry);
        }
    }

    /// <summary>
    /// Deletes one file or folder and logs the outcome.
    /// </summary>
    /// <param name="entry">The file or folder.</param>
    private void DeleteEntry(string entry)
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

`TemporaryFolderCleanerLog.cs` (четыре записи переезжают из `BackupOrchestratorLog`, где у них были EventId 2, 3, 4 и 10):

```csharp
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Cleanup;

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

    /// <summary>Logs a leftover that cannot be deleted and stays for the next run.</summary>
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

## 3. Регистрация и вызов

`CoreServiceCollectionExtensions.cs`, рядом со строкой 63:

```csharp
services.TryAddSingleton<ITemporaryFolderCleaner, TemporaryFolderCleaner>();
```

`BackupOrchestrator.cs`:
- Добавить параметр `ITemporaryFolderCleaner temporaryFolderCleaner` в первичный конструктор. Описание: `Empties the folder of temporary archives.`
- В `<param name="fileSystem">` убрать фразу про очистку. `fileSystem` остаётся нужен для `IsSameFolder`, `IsInside` и `IsReparsePoint`.
- Удалить метод `EmptyTemporaryFolder` (`:913-967`).
- В `RunLockedAsync` заменить `:238`:

```csharp
else
{
    temporaryFolderCleaner.Clean(temporaryFolder);
}
```

Порядок не меняется. Блокировка берётся в `RunCoreAsync` (`:173`), очистка идёт позже, в `RunLockedAsync`, до `cloneLocationStore.LoadAsync`.

`BackupOrchestratorLog.cs`: удалить `TemporaryEntryDeleted`, `TemporaryEntryNotDeleted`, `TemporaryFolderNotListed`, `TemporaryFolderIsLink`. `TemporaryFolderLeftUntouched` остаётся, это сообщение dry-run.

## 4. Тесты

`Core.Tests/Cleanup/TemporaryFolderCleanerTests.cs`. Я перенёс три теста из `BackupOrchestratorTests` (`:789`, `:816`, `:831`) и добавил два новых.

```csharp
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Cleanup;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using Shouldly;

namespace GitHubBackup.Core.Tests.Cleanup;

public sealed class TemporaryFolderCleanerTests
{
    private const string _folder = @"C:\sources\.tmp";
    private const int _entryDeletedEventId = 1;
    private const int _entryNotDeletedEventId = 2;
    private const int _folderNotListedEventId = 3;
    private const int _folderIsLinkEventId = 4;

    private readonly MockFileSystem _fileSystem = new();
    private readonly FakeLogger<TemporaryFolderCleaner> _logger = new();

    private TemporaryFolderCleaner CreateCleaner(IFileSystem? fileSystem = null) =>
        new(fileSystem ?? _fileSystem, _logger);

    private IReadOnlyList<FakeLogRecord> Records(int eventId) =>
        [.. _logger.Collector.GetSnapshot().Where(record => record.Id.Id == eventId)];

    [Fact]
    public void Clean_FilesAndNestedFolder_DeletesThemAndKeepsTheFolder()
    {
        _fileSystem.AddFile(Path.Combine(_folder, "a.zip"), new("partial"));
        _fileSystem.AddFile(Path.Combine(_folder, "nested", "b.zip"), new("partial"));

        CreateCleaner().Clean(_folder);

        _fileSystem.Directory.GetFileSystemEntries(_folder).ShouldBeEmpty();
        Records(_entryDeletedEventId).Select(record => record.GetStructuredStateValue("Path")).ShouldBe(
            [Path.Combine(_folder, "a.zip"), Path.Combine(_folder, "nested")], ignoreOrder: true);
    }

    [Fact]
    public void Clean_FolderMissing_DoesNothing()
    {
        CreateCleaner().Clean(_folder);

        _fileSystem.Directory.Exists(_folder).ShouldBeFalse();
        _logger.Collector.Count.ShouldBe(0);
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
    public void Clean_FolderIsLink_LeavesItsContent()
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
    public void Clean_FolderCannotBeListed_LogsWarningAndDoesNotThrow()
    {
        var directory = Substitute.For<IDirectory>();
        directory.Exists(_folder).Returns(true);
        directory.GetFileSystemEntries(_folder).Returns(_ => throw new IOException("denied"));
        var info = Substitute.For<IDirectoryInfo>();
        info.Attributes.Returns(FileAttributes.Directory);
        var infoFactory = Substitute.For<IDirectoryInfoFactory>();
        infoFactory.New(_folder).Returns(info);
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.Directory.Returns(directory);
        fileSystem.DirectoryInfo.Returns(infoFactory);

        CreateCleaner(fileSystem).Clean(_folder);

        var record = Records(_folderNotListedEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(_folder);
    }
}
```

`BackupOrchestratorTests.cs`:
- Удалить три перенесённых теста и константы EventId `_temporaryEntryDeletedEventId`, `_temporaryEntryNotDeletedEventId`, `_temporaryFolderIsLinkEventId`.
- Добавить `ITemporaryFolderCleaner` как `Substitute.For` и передать его в конструктор. Место создания оркестратора я не нашёл по `new BackupOrchestrator(`, его нужно будет найти при правке.
- Добавить три теста:
  - `RunAsync_RunLockTaken_CleansTemporaryFolderAfterTheLock`. Проверка: `Received.InOrder(() => { _runLock.TryAcquireAsync(CancellationToken); _temporaryFolderCleaner.Clean(_temporaryFolder); })`.
  - `RunAsync_RunLockHeld_DoesNotCleanTemporaryFolder`. Проверка: `DidNotReceiveWithAnyArgs().Clean(default!)`.
  - `RunAsync_DryRun_DoesNotCleanTemporaryFolder`. Проверка: то же самое.

`CoreServiceCollectionExtensionsTests.cs`: добавить проверку, что `ITemporaryFolderCleaner` разрешается в `TemporaryFolderCleaner`, по образцу соседних. Структуру этого файла я не читал.

## 5. Вопрос по PRD

Код пропускает очистку в dry-run: он пишет `TemporaryFolderLeftUntouched` и не вызывает `EmptyTemporaryFolder`. FR-7.4 и §9 шаг 4 такого исключения не называют. NFR-SYS-1 говорит только, что dry-run тоже захватывает блокировку.

Я сохранил это поведение. Не знаю, описан ли пропуск где-то ещё в PRD, я искал только по FR-7.x. По правилу «PRD first» стоит либо добавить исключение в FR-7.4, либо считать пропуск ошибкой. Как поступить?

Если подтвердите план, то после правок нужно будет собрать решение и прогнать `Core.Tests`.
