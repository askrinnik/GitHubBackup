**Эта очистка уже есть в коде.** Отдельный сервис не добавляет нового поведения, он только выносит существующий код из оркестратора. Файлы я не менял.

## Что уже реализовано

- `BackupOrchestrator.RunLockedAsync` вызывает `EmptyTemporaryFolder(temporaryFolder)` после `runLock.TryAcquireAsync`. Сам метод находится в `BackupOrchestrator.cs:923`.
- Dry-run не очищает папку и пишет `TemporaryFolderLeftUntouched`, как требует FR-9.5.
- Файл или папку, которые не удалось удалить, метод логирует (`TemporaryEntryNotDeleted`, EventId 3) и оставляет до следующего запуска.
- Если папка `.tmp` — это junction или symlink, метод её не трогает (`TemporaryFolderIsLink`). Если папку нельзя прочитать, он пишет `TemporaryFolderNotListed`.
- Тесты лежат в `BackupOrchestratorTests.cs`: `RunAsync_TemporaryFolderEmptiedBeforeDiscovery` (строка 1377), тест на неудаляемый файл (около строки 825) и `RunAsync_TemporaryFolderIsLink_…` (строка 831).

Расхождений с FR-7.4 я не нашёл.

## Если вам нужен именно сервис

Это рефакторинг без изменения поведения, PRD менять не нужно. Вот набросок.

**Новые файлы в `src/GitHubBackup.Core/Runs/`**

```csharp
/// <summary>
/// Deletes what interrupted runs left in the folder of temporary archives.
/// </summary>
public interface ITemporaryFolderCleaner
{
    /// <summary>
    /// Empties <paramref name="folder"/>; an entry that cannot be deleted stays for the next run.
    /// </summary>
    /// <remarks>The caller holds the run lock, so no other run writes to the folder.</remarks>
    /// <param name="folder">The folder of temporary archives.</param>
    void Empty(string folder);
}
```

`TemporaryFolderCleaner` — это `sealed class TemporaryFolderCleaner(IFileSystem fileSystem, ILogger<TemporaryFolderCleaner> logger) : ITemporaryFolderCleaner`. Тело `Empty` — это текущее тело `EmptyTemporaryFolder` без изменений.

`TemporaryFolderCleanerLog` — это `internal static partial class` с тремя сообщениями из `BackupOrchestratorLog`: `TemporaryEntryDeleted`, `TemporaryEntryNotDeleted` и `TemporaryFolderNotListed`. Четвёртое сообщение, `TemporaryFolderIsLink`, тоже переезжает. EventId придётся пронумеровать заново, а константы в тестах поменять.

**Регистрация.** В `CoreServiceCollectionExtensions` добавляется строка `services.TryAddSingleton<ITemporaryFolderCleaner, TemporaryFolderCleaner>();`. Рядом нужно дописать тип в XML-doc метода и обновить `CoreServiceCollectionExtensionsTests`.

**Вызов.** В `BackupOrchestrator` в конструктор добавляется параметр `ITemporaryFolderCleaner temporaryFolderCleaner`. В `RunLockedAsync` строка `EmptyTemporaryFolder(temporaryFolder)` заменяется на `temporaryFolderCleaner.Empty(temporaryFolder)`. Приватный метод удаляется. `IFileSystem` в оркестраторе остаётся, потому что его использует `IsInside`.

**Тесты**
- Новый класс `TemporaryFolderCleanerTests` в `Core.Tests/Runs/` работает на `MockFileSystem` и `FakeLogger`. Он проверяет четыре случая: файлы и вложенные папки удаляются; неудаляемая запись логируется и не мешает остальным; папка-ссылка не очищается; отсутствующая папка — это no-op.
- Из `BackupOrchestratorTests` три теста про `.tmp` заменяются двумя. Первый проверяет, что `Empty` вызван после взятия блокировки и до discovery, второй — что в dry-run он не вызывается. Оба используют подставной `ITemporaryFolderCleaner`.
- `ArchitectureTests` ничего не нужно менять: тип лежит в `Core` и использует только `IFileSystem`.

Выносить код в сервис стоит, только если вам нужно вызывать очистку из другого места. Иначе текущий код достаточен.

Хотите, чтобы я сделал этот рефакторинг? Или вы имели в виду другой недостающий кусок FR-7.4?
