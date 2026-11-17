**Ответ:** очистка `<sourcesRoot>\.tmp\` уже есть. Её делает приватный метод `BackupOrchestrator.EmptyTemporaryFolder` (`BackupOrchestrator.cs:923`). Он вызывается в `RunLockedAsync` после захвата блокировки и пропускается при dry-run (FR-9.5). Поэтому новый сервис — это вынос готовой логики из оркестратора. Поведение при этом не меняется, правка PRD не нужна. Если вы ждали новую функциональность, скажите: тогда я что-то не понял. Файлы я не менял.

## Типы и файлы

```
src/GitHubBackup.Core/Archiving/ITemporaryFolderCleaner.cs
src/GitHubBackup.Core/Archiving/TemporaryFolderCleaner.cs
src/GitHubBackup.Core/Archiving/TemporaryFolderCleanerLog.cs
src/GitHubBackup.Core.Tests/Archiving/TemporaryFolderCleanerTests.cs
```

Правятся: `BackupOrchestrator.cs`, `BackupOrchestratorLog.cs`, `CoreServiceCollectionExtensions.cs`, `BackupOrchestratorTests.cs`.

Сервис лежит в `Core`, потому что он использует только `IFileSystem`. Метод синхронный, как и сам `IFileSystem`, поэтому токена отмены у него нет.

**ITemporaryFolderCleaner.cs**
```csharp
namespace GitHubBackup.Core.Archiving;

/// <summary>
/// Deletes what interrupted runs left in the folder of temporary archives.
/// </summary>
public interface ITemporaryFolderCleaner
{
    /// <summary>
    /// Deletes every file and folder in <paramref name="folder"/> and keeps the folder itself.
    /// </summary>
    /// <remarks>
    /// The caller holds the run lock, so no other run writes to the folder. An entry that cannot be deleted is logged
    /// and left for the next run. A folder that is a junction or a symbolic link is left alone, because emptying it
    /// would delete files outside the sources root. A missing folder is not an error.
    /// </remarks>
    /// <param name="folder">The folder of temporary archives.</param>
    void Clean(string folder);
}
```

**TemporaryFolderCleaner.cs**
```csharp
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Archiving;

/// <summary>
/// Implements <see cref="ITemporaryFolderCleaner"/> through <see cref="IFileSystem"/>.
/// </summary>
/// <param name="fileSystem">Lists and deletes the entries.</param>
/// <param name="logger">Logs each deleted entry and each entry that stays.</param>
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
                TemporaryFolderCleanerLog.EntryNotDeleted(logger, entry, exception);
            }
        }
    }
}
```

**TemporaryFolderCleanerLog.cs** — те же четыре сообщения, что сейчас в `BackupOrchestratorLog` (id 2, 3, 4, 10), с новыми id 1–4:
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

    /// <summary>Logs a leftover that cannot be deleted and stays for the next run.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The file or folder.</param>
    /// <param name="exception">The reason.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Cannot delete the leftover temporary archive {Path}")]
    public static partial void EntryNotDeleted(ILogger logger, string path, Exception exception);

    /// <summary>Logs that the folder cannot be listed, so it is not emptied.</summary>
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

`CoreServiceCollectionExtensions.cs`, рядом с `IArchiveNameBuilder` (около строки 52):
```csharp
services.TryAddSingleton<ITemporaryFolderCleaner, TemporaryFolderCleaner>();
```

`BackupOrchestrator.cs`:
1. Добавить в первичный конструктор `ITemporaryFolderCleaner temporaryFolderCleaner` (после `IFileSystem fileSystem`) и `<param>` в XML-doc. Сам `IFileSystem` остаётся: он нужен `IsSameFolder`, `IsReparsePoint` и другим методам.
2. В `RunLockedAsync` заменить вызов. Он остаётся в ветке `else`, то есть после блокировки и не в dry-run:
   ```csharp
   else
   {
       temporaryFolderCleaner.Clean(temporaryFolder);
   }
   ```
3. Удалить метод `EmptyTemporaryFolder` (строки 913–967).
4. В `BackupOrchestratorLog` удалить `TemporaryEntryDeleted`, `TemporaryEntryNotDeleted`, `TemporaryFolderNotListed`, `TemporaryFolderIsLink`. Старые id 2, 3, 4, 10 не переиспользовать. Сообщение `TemporaryFolderLeftUntouched` остаётся.

Хосты (`Cli`, `App`) не меняются: они получают сервис через `AddGitHubBackupCore()`.

## Тесты

**TemporaryFolderCleanerTests.cs** (`MockFileSystem`, `FakeLogger<T>`, как требует `.claude/rules/tests.md`):
```csharp
using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Archiving;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace GitHubBackup.Core.Tests.Archiving;

/// <summary>
/// Tests <see cref="TemporaryFolderCleaner"/>.
/// </summary>
public sealed class TemporaryFolderCleanerTests
{
    private const string _folder = @"C:\sources\.tmp";
    private const int _entryDeletedEventId = 1;
    private const int _entryNotDeletedEventId = 2;
    private const int _folderIsLinkEventId = 4;

    private readonly MockFileSystem _fileSystem = new();
    private readonly FakeLogger<TemporaryFolderCleaner> _logger = new();

    [Fact]
    public void Clean_FilesAndFolders_DeletesEverythingAndKeepsFolder()
    {
        _fileSystem.AddFile(Path.Combine(_folder, "a.zip"), new("partial"));
        _fileSystem.AddFile(Path.Combine(_folder, "nested", "b.zip"), new("partial"));

        Create().Clean(_folder);

        _fileSystem.Directory.Exists(_folder).ShouldBeTrue();
        _fileSystem.Directory.GetFileSystemEntries(_folder).ShouldBeEmpty();
        Records(_entryDeletedEventId).Count.ShouldBe(2);
    }

    [Fact]
    public void Clean_FolderMissing_DoesNothing()
    {
        Create().Clean(_folder);

        _fileSystem.Directory.Exists(_folder).ShouldBeFalse();
        _logger.Collector.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public void Clean_FileCannotBeDeleted_LogsWarningAndDeletesTheOthers()
    {
        var locked = Path.Combine(_folder, "locked.zip");
        var other = Path.Combine(_folder, "other.zip");
        _fileSystem.AddFile(locked, new("partial") { AllowedFileShare = FileShare.None });
        _fileSystem.AddFile(other, new("partial"));

        Create().Clean(_folder);

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

        Create().Clean(_folder);

        _fileSystem.File.Exists(leftover).ShouldBeTrue();
        var record = Records(_folderIsLinkEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(_folder);
    }

    private TemporaryFolderCleaner Create() => new(_fileSystem, _logger);

    private IReadOnlyList<FakeLogRecord> Records(int eventId) =>
        [.. _logger.Collector.GetSnapshot().Where(record => record.Id.Id == eventId)];
}
```

Случай «папку нельзя перечислить» (событие 3) через `MockFileSystem` не воспроизвести без обёртки. Я бы его не покрывал или добавил его с `IFileSystem`-подстановкой через NSubstitute — решите сами.

**BackupOrchestratorTests.cs** — правки:
- Тесты, которые проверяют сами удаления, переезжают в `TemporaryFolderCleanerTests` и из оркестратора исчезают. Это `RunAsync_LeftoversInTemporaryFolder_DeletedBeforeFirstRepository`, `RunAsync_LeftoverCannotBeDeleted_LogsWarningAndContinues` и `RunAsync_TemporaryFolderIsLink_LeavesItsContentAndContinues`. Вместе с ними уходят константы `_temporaryEntry*EventId` и `_temporaryFolderIsLinkEventId`.
- Остальные тесты оркестратора получают подстановку `Substitute.For<ITemporaryFolderCleaner>()` в конструктор.
- Тесты `RunAsync_LockHeld_ReturnsCriticalWithoutTemporaryFolderCleanupOrDiscovery`, `RunAsync_LockTakenAfterTokenAndBeforeTemporaryFolderCleanup`, `RunAsync_TemporaryFolderEmptiedBeforeDiscovery` и `RunAsync_DryRun_LeavesTemporaryFolderUntouched` переписываются на проверку вызова подстановки:
  - `_cleaner.Received(1).Clean(_temporaryFolder)` после блокировки и до `DiscoverAsync`;
  - `_cleaner.DidNotReceiveWithAnyArgs().Clean(default!)` при занятой блокировке и при dry-run.

**CoreServiceCollectionExtensionsTests.cs** — один тест:
```csharp
[Fact]
public void AddGitHubBackupCore_TemporaryFolderCleaner_IsResolved()
{
    using var provider = new ServiceCollection()
        .AddGitHubBackupCore()
        .AddSingleton<IFileSystem>(new MockFileSystem())
        .BuildServiceProvider();

    provider.GetRequiredService<ITemporaryFolderCleaner>().ShouldBeOfType<TemporaryFolderCleaner>();
}
```

Интеграционный тест `Infrastructure.IntegrationTests/Runs/BackupOrchestratorTests.RunAsync_LeftoverInTemporaryFolder_IsDeletedAtStart` не меняется: он проверяет весь путь с реальным диском.

## Что я не проверял

- Строки 1221–1468 `BackupOrchestrator.cs` я не читал. Там могут быть ещё ссылки на удаляемые логи или метод; перед правкой стоит сделать `grep` по `EmptyTemporaryFolder` и `TemporaryEntry`.
- Я не знаю, как именно тесты оркестратора создают `BackupOrchestrator` (конструктор около строки 129). Правка конструктора там обязательна.
- Код не компилировал и не запускал.
