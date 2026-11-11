# План: #15 F1.8: State file .backup-state.json

| Поле | Значение |
|---|---|
| Issue | [#15](https://github.com/askrinnik/GitHubBackup/issues/15) |
| Заголовок | F1.8: State file .backup-state.json |
| Направление | Feature |
| Сложность | L |
| Дата | 2026-11-11 |

## 1. Цель

Нужен `IBackupStateStore`. Он читает и пишет `.backup-state.json` в папке архивов репозитория. Формат файла задан в PRD §6.6: `schemaVersion`, `repositoryId`, `fullName`, секции `repository` и `wiki`, в каждой список `archives` (FR-6.1). У wiki отдельная секция (FR-5.4). Запись добавляется только после успешного создания архива, файл пишется атомарно (FR-6.4, NFR-2).

API должен подходить для `IChangeDetector` из #16. Детектору нужна последняя запись нужной секции: имя файла, ветка и снимок ссылок `RefSnapshot`.

## 2. Критерии приёмки

Из issue (дословно):

1. Snapshot test of the format
2. Reading a missing file gives an empty state
3. Covered by tests, CI green

Добавлено по PRD и по сверке с кодом:

4. *(добавлено)* Записанный файл совпадает по форме с примером из PRD §6.6. Отступ два пробела, переводы строк CRLF, UTF-8 без BOM. Обе секции есть всегда, пустая выглядит как `"wiki": { "archives": [] }`.
5. *(добавлено)* `createdAt` — местное время со смещением в виде `yyyy-MM-ddTHH:mm:sszzz`, например `2026-10-02T14:30:15+03:00` (FR-6.1).
6. *(добавлено)* Ключи `refs` имеют вид `heads/<name>` и `tags/<name>`, как в `RefSnapshot`. При записи они отсортированы по имени (ordinal), поэтому файл детерминирован. После чтения снимок равен исходному.
7. *(добавлено)* Запись атомарна: временный файл в той же папке, flush, затем `File.Replace` (или `File.Move`, если файла ещё нет). Предыдущая версия остаётся как `.backup-state.json.bak` (правило security F1). Сбой или отмена оставляет прежний файл целым и не оставляет временный файл.
8. *(добавлено)* `AppendAsync` добавляет запись в конец нужной секции и не трогает другую секцию (FR-5.4). Если файла архива `record.File` нет в папке архивов, метод отказывает и ничего не пишет (FR-6.4).
9. *(добавлено)* При чтении битый JSON, `schemaVersion` не равный 1, нет обязательного поля или неверный ключ ref дают `BackupStateException`. В сообщении есть путь и позиция ошибки, но нет текста из файла.
10. *(добавлено)* Если нет папки архивов, это тоже пустое состояние, как и при отсутствии файла.
11. Строже проверка `ArchiveRecord.File`. Отказ также на символы `<>:"|?*` и управляющие символы, на зарезервированные имена устройств (`CON`, `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9`, в любом регистре, в том числе с расширением) и на точку или пробел в конце. *(добавлено после security review)*
12. Если `repositoryId` в файле не совпадает с переданным, `LoadAsync` и `AppendAsync` бросают `BackupStateException`. Файл остаётся без изменений. *(добавлено после security review)*

## 3. Затрагиваемые проекты и типы

**GitHubBackup.Core** — новая папка `State/`, пространство имён `GitHubBackup.Core.State`:

- `BackupTarget` (enum `Repository`, `Wiki`) — выбирает секцию.
- `ArchiveRecord` (record) — `File`, `CreatedAt` (`DateTimeOffset`), `Branch`, `HeadCommit`, `SizeBytes` (`long`), `Refs` (`RefSnapshot`). Проверяет содержимое: `File` — непустое голое имя файла без разделителей пути и без `..`. `Branch` и `HeadCommit` непустые, `SizeBytes >= 0`.
- `BackupStateSection` (record) — `Archives` (`IReadOnlyList<ArchiveRecord>`) и `LastArchive` (последний элемент или `null`). Статическое `Empty`.
- `BackupState` (record) — `SchemaVersion`, `RepositoryId` (`long`), `FullName`, `Repository`, `Wiki`. Константы `CurrentSchemaVersion = 1` и `FileName = ".backup-state.json"`. Методы:
  - `CreateEmpty(long repositoryId, string fullName)`;
  - `GetSection(BackupTarget)`;
  - `GetLastArchive(BackupTarget)` — точка входа для #16;
  - `WithArchive(BackupTarget, ArchiveRecord, string fullName)` — чистая функция: новое состояние с записью в конце секции и текущим `fullName`.
- `IBackupStateStore`:
  - `Task<BackupState> LoadAsync(string archiveFolder, long repositoryId, string fullName, CancellationToken)`;
  - `Task<BackupState> AppendAsync(string archiveFolder, long repositoryId, string fullName, BackupTarget target, ArchiveRecord record, CancellationToken)`.
- `BackupStateException` — путь файла и список ошибок, по образцу `BackupConfigException`.

**GitHubBackup.Infrastructure:**

- `Files/AtomicFileWriter` (новый, internal sealed, зависит от `IFileSystem`) — `WriteAsync(string path, ReadOnlyMemory<byte> content, CancellationToken)`. Это логика из `JsonBackupConfigStore.SaveAsync`, вынесенная без изменений: temp, flush, Replace/Move с `.bak`, `RestoreFinalFile`, `TryDelete`.
- `BackupConfiguration/JsonBackupConfigStore` (изменён) — пишет через `AtomicFileWriter`. Константы `BackupExtension` и `TemporaryExtension` переходят в `AtomicFileWriter`. Поведение не меняется.
- `State/JsonBackupStateStore` (новый, internal sealed) — реализует `IBackupStateStore` через `IFileSystem`, `AtomicFileWriter` и `ILogger`.
- `State/BackupStateJsonContext` (новый) — source-generated контекст с теми же опциями, что `BackupConfigJsonContext`: camelCase, indent 2, CRLF, `UnsafeRelaxedJsonEscaping`, `RespectNullableAnnotations`, `UnmappedMemberHandling.Disallow`. Комментарии и висячие запятые не допускаются: файл пишет только программа.
- `State/RefSnapshotJsonConverter` (новый) — объект `name → sha`, пишет отсортированно. При чтении `ArgumentException` из конструктора `RefSnapshot` превращается в `JsonException`.
- `State/LocalDateTimeOffsetJsonConverter` (новый) — пишет `yyyy-MM-ddTHH:mm:sszzz` с `CultureInfo.InvariantCulture`. Читает любой ISO 8601 со смещением.
- `State/BackupStateLog` (новый) — `[LoggerMessage]` для `Loaded`, `NotFound` и `ArchiveAppended`.
- `InfrastructureServiceCollectionExtensions` (изменён) — `TryAddSingleton<AtomicFileWriter>()` и `TryAddSingleton<IBackupStateStore, JsonBackupStateStore>()`.

## 4. Подход

1. **Core.** Модели и интерфейс без I/O. `WithArchive` — чистая функция, поэтому логика «добавить в нужную секцию» тестируется в Core.Tests. `RefSnapshot` из #14 используется как есть: у него те же ключи `heads/`/`tags/` и равенство без учёта порядка. Это нужно #16.
2. **Infrastructure, рефакторинг.** Атомарная запись выносится в `AtomicFileWriter`, `JsonBackupConfigStore` переключается на него. Существующие `JsonBackupConfigStoreSaveTests` должны пройти без правок. Так у логики Replace/restore одна копия, а не две.
3. **Infrastructure, хранилище.**
   - `LoadAsync`: открыть `Path.Combine(archiveFolder, BackupState.FileName)`. `FileNotFoundException` или `DirectoryNotFoundException` дают `BackupState.CreateEmpty(...)`. Ошибки I/O и JSON дают `BackupStateException`. Сообщение строится как в `DescribeJsonError`, `JsonException` не кладётся во inner. `schemaVersion != 1` даёт `BackupStateException`.
   - `AppendAsync`: проверить `fileSystem.File.Exists(Path.Combine(archiveFolder, record.File))`. Заглушка OneDrive считается существующим файлом, содержимое не читается. Если файла нет, бросить `BackupStateException` и ничего не писать. Иначе Load, `WithArchive`, сериализация, `AtomicFileWriter.WriteAsync`, лог, возврат нового состояния.
4. **Host.** Только регистрация в DI. Вызов из оркестратора будет в следующих issue.

## 5. Решения

- **Где берётся время.** `TimeProvider` в хранилище не нужен. `createdAt` приходит в `ArchiveRecord` от вызывающего кода, который берёт его из `TimeProvider.GetLocalNow()`. Конвертер отбрасывает доли секунды при записи.
- **Отсутствующий файл.** `repositoryId` и `fullName` передаёт вызывающий код. Поэтому пустое состояние сразу полноценно, и `BackupState` обходится без nullable-полей.
- **Переименование.** `fullName` обновляется при каждом `AppendAsync`: это отражает переименование репозитория. Если `repositoryId` в файле не совпадает с переданным, `LoadAsync` и `AppendAsync` бросают `BackupStateException`. Сообщение называет путь файла и оба id. `AppendAsync` такой файл не перезаписывает.
- **`.bak`.** Предыдущая версия хранится как `.backup-state.json.bak`. Так требует правило security F1.
- **Строгое чтение.** Неизвестные поля и `null` в обязательных полях — ошибка. Файл пишет только программа, а схему задаёт `schemaVersion`.
- **Без блокировок.** Один репозиторий обрабатывает один исполнитель, поэтому блокировок нет.
- **Отдельный запуск.** Задача запускается отдельно от пакета `/implement-issues 15 16 18`, потому что её сложность L.

## 6. Тесты

**GitHubBackup.Core.Tests**, папка `State/`:

- `BackupStateTests`:
  - `CreateEmpty_Always_HasSchemaVersion1AndEmptySections`;
  - `WithArchive_Repository_AppendsOnlyToRepositorySection` и `WithArchive_Wiki_AppendsOnlyToWikiSection` (Theory по `BackupTarget`);
  - `WithArchive_Twice_LastArchiveIsSecond`;
  - `WithArchive_NewFullName_UpdatesFullName`;
  - `GetLastArchive_EmptySection_ReturnsNull`.
- `ArchiveRecordTests`:
  - Theory на некорректный `File`: пусто, `a\b.zip`, `a/b.zip`, `..`, корневой путь;
  - пустые `Branch` и `HeadCommit`;
  - `SizeBytes = -1` даёт `ArgumentException`;
  - `SizeBytes = 0` допустим (граница).

**GitHubBackup.Infrastructure.Tests**. Используются `MockFileSystem` с путями `C:\backup\...` и `FaultInjectingFileSystem` из `BackupConfiguration/`.

- `State/JsonBackupStateStoreAppendTests`:
  - `AppendAsync_DocumentedSample_WritesExpectedJson` — snapshot. Ожидаемый текст задан raw-строкой и равен примеру PRD §6.6 (refs отсортированы, `+03:00` через `new DateTimeOffset(..., TimeSpan.FromHours(3))`).
  - `AppendAsync_AnyState_WritesUtf8WithoutBomAndWindowsLineBreaks`.
  - `AppendAsync_NoStateFile_CreatesFileWithoutBackup`.
  - `AppendAsync_ExistingFile_AppendsAndKeepsPreviousAsBackup`.
  - `AppendAsync_Wiki_LeavesRepositorySectionUnchanged`.
  - `AppendAsync_ArchiveFileMissing_ThrowsAndWritesNothing`.
  - `AppendAsync_WriteOfTemporaryFileFails_KeepsOriginalFileAndRemovesTemporaryFile`.
  - `AppendAsync_ReplaceFails_KeepsOriginalFile`.
  - `AppendAsync_Cancelled_KeepsOriginalFileAndRemovesTemporaryFile`.
  - `AppendAsync_UtcRecord_WritesZeroOffset` (граница `+00:00`).
  - `AppendAsync_FractionalSeconds_WritesWholeSeconds`.
- `State/JsonBackupStateStoreTests` (чтение):
  - `LoadAsync_MissingFile_ReturnsEmptyStateWithGivenIdentity`;
  - `LoadAsync_MissingFolder_ReturnsEmptyState`;
  - `LoadAsync_DocumentedSample_ReadsAllFields` (refs равны `RefSnapshot`, `CreatedAt.Offset` = 3 ч);
  - `LoadAsync_SavedAndLoaded_RoundTrips`;
  - Theory `LoadAsync_InvalidDocument_ThrowsBackupStateException`: битый JSON, `schemaVersion: 2`, нет `wiki`, неизвестное поле, ключ ref `main` без префикса, повтор ключа ref;
  - `LoadAsync_InvalidJson_MessageDoesNotQuoteContent`;
  - `LoadAsync_UnreadableFile_ThrowsBackupStateException`.
- `Files/AtomicFileWriterTests` — пути успеха и сбоя самого писателя: новый файл, замена с `.bak`, Replace fails, ReplaceFailsAfterBackup, отмена. По одному кейсу на ветку.
- `InfrastructureServiceCollectionExtensionsTests` — новый кейс: `IBackupStateStore` разрешается в `JsonBackupStateStore`.

`GitHubBackup.ArchitectureTests` уже проверяют, что Core не ссылается на I/O и что Infrastructure не вызывает `File`/`Directory`. Новых тестов там не нужно.

## 7. Проверка

| Критерий | Как проверяется |
|---|---|
| 1 | `AppendAsync_DocumentedSample_WritesExpectedJson` |
| 2 | `LoadAsync_MissingFile_ReturnsEmptyStateWithGivenIdentity`, `LoadAsync_MissingFolder_ReturnsEmptyState` |
| 3 | полная пересборка, тесты решения, `dotnet format --verify-no-changes`; зелёный CI в PR |
| 4 | snapshot-тест и `..._WritesUtf8WithoutBomAndWindowsLineBreaks` |
| 5 | `..._UtcRecord_WritesZeroOffset`, `..._FractionalSeconds_WritesWholeSeconds` |
| 6 | snapshot-тест (порядок), `LoadAsync_SavedAndLoaded_RoundTrips` |
| 7 | fault-тесты в `JsonBackupStateStoreAppendTests` и `AtomicFileWriterTests`; `JsonBackupConfigStoreSaveTests` без изменений |
| 8 | `AppendAsync_Wiki_LeavesRepositorySectionUnchanged`, `AppendAsync_ArchiveFileMissing_ThrowsAndWritesNothing`, `BackupStateTests` |
| 9 | Theory `LoadAsync_InvalidDocument_ThrowsBackupStateException`, `LoadAsync_InvalidJson_MessageDoesNotQuoteContent` |
| 10 | `LoadAsync_MissingFolder_ReturnsEmptyState` |
| 11 | Theory `Constructor_InvalidFile_ThrowsArgumentException` (недопустимые символы, имена устройств, точка или пробел в конце) |
| 12 | `LoadAsync_OtherRepositoryId_ThrowsBackupStateException`, `AppendAsync_OtherRepositoryId_ThrowsAndLeavesFileUnchanged` |

## 8. Вне рамок

- Решение об архивации и сравнение снимков — `IChangeDetector`, это #16.
- Вызов `AppendAsync` после перемещения архива и получение `createdAt` из `TimeProvider.GetLocalNow()` — архиватор и оркестратор, следующие issue.
- История архивов в UI (FR-8.5).
- Очистка старых `.bak`: держится одна копия, она перезаписывается.

## 9. Открытые вопросы

Нет.

## 10. Задачи

- [x] Core: `State/BackupTarget`, `ArchiveRecord` (с проверкой полей), `BackupStateSection`, `BackupState` (`CreateEmpty`, `GetSection`, `GetLastArchive`, `WithArchive`), `BackupStateException`, `IBackupStateStore` с XML-doc
- [x] Core.Tests: `BackupStateTests`, `ArchiveRecordTests`
- [x] Infrastructure: вынести атомарную запись в `Files/AtomicFileWriter`, переключить на него `JsonBackupConfigStore`, убедиться, что `JsonBackupConfigStoreSaveTests` зелёные без правок
- [x] Infrastructure.Tests: `Files/AtomicFileWriterTests`
- [x] Infrastructure: `State/RefSnapshotJsonConverter`, `State/LocalDateTimeOffsetJsonConverter`, `State/BackupStateJsonContext`
- [x] Infrastructure: `State/JsonBackupStateStore` (`LoadAsync`, `AppendAsync`) и `State/BackupStateLog`
- [x] Регистрация `AtomicFileWriter` и `IBackupStateStore` в `InfrastructureServiceCollectionExtensions`, кейс в `InfrastructureServiceCollectionExtensionsTests`
- [x] Infrastructure.Tests: `State/JsonBackupStateStoreTests`, `State/JsonBackupStateStoreAppendTests` (snapshot по примеру PRD §6.6, сбои, отмена, границы смещения)
- [x] Ужесточить проверку `ArchiveRecord.File` и сверять `repositoryId` в `JsonBackupStateStore`, тесты в `ArchiveRecordTests`, `JsonBackupStateStoreTests`, `JsonBackupStateStoreAppendTests` *(добавлено после security review)*
- [x] Build, test, format check; проверить новые комментарии по правилам comment hygiene
