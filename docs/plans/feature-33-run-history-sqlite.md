# План: #33 «F3.8: Run history in SQLite (EF Core)»

| Поле | Значение |
|---|---|
| Issue | [#33](https://github.com/askrinnik/GitHubBackup/issues/33) |
| Lane | Feature |
| Сложность | L |
| Дата | 2026-11-12 |

## 1. Цель

Нужна история запусков в SQLite-базе `data\history.db` рядом с exe (FR-8.1). Каждый запуск записывается с элементами (FR-8.2, FR-8.3) и событиями обнаружения (FR-8.4). Схема базы создаётся и обновляется миграциями EF Core при старте хоста (FR-8.6).

Задача выпуска: абстракция `IRunHistoryStore` в `Core` без типов EF, её реализация на EF Core SQLite в `Infrastructure`, регистрация в DI и применение миграций при старте. Запись реальных запусков из оркестратора — следующая задача (см. «Вне рамок»). FR-8.5 и FR-8.7 в этот выпуск не входят.

## 2. Критерии приёмки

Из issue, дословно:
- [x] Интеграционный тест записи и чтения запуска
- [x] Пустая база создаётся автоматически
- [ ] Покрыто тестами, CI зелёный

Добавлены по PRD и по сверке с кодом:
- [x] (добавлено, FR-8.6) Хост при `StartAsync` создаёт папку `data` и базу со схемой. Если база есть и миграции применены, повторный старт ничего не меняет и сохраняет данные.
- [x] (добавлено, FR-8.1) Путь к базе берётся из `History:DatabasePath`. Относительный путь разрешается от content root через существующий `ContentRootPathPostConfigure`.
- [x] (добавлено) Необязательные поля записываются и читаются как `null` без потерь: фильтр, архив, ошибка, ветка, HEAD, решение по событию. Пустой список изменившихся ссылок тоже читается без потерь.
- [x] (добавлено) Перечисления хранятся строками (имена значений). Код возврата хранится числом. `DateTimeOffset` сохраняет смещение.
- [x] (добавлено) Модель EF совпадает со snapshot миграций. Тест ловит забытую миграцию.
- [x] (добавлено, NFR-1) В базу не попадает токен: схема не содержит полей конфигурации и учётных данных.

## 3. Затрагиваемые проекты и типы

**`GitHubBackup.Core`**
- `Runs/RunMode.cs` (новый): enum `Interactive`, `Silent`, `Ui` (FR-8.2).
- `History/IRunHistoryStore.cs` (новый): `Task AddAsync(RunHistoryEntry run, CancellationToken)` и `Task<RunHistoryEntry?> GetAsync(Guid runId, CancellationToken)`.
- `History/RunHistoryEntry.cs` (новый record): `RunId` (тот же, что `IRunContext.RunId` в логах), `StartedAt`/`FinishedAt` (`DateTimeOffset`), `Mode`, `IsDryRun`, `AccountFilter?`, `RepositoryFilter?`, `ExitCode`, `Items`, `DiscoveryEvents`, метод `Count(BackupItemStatus)`.
- `History/RunHistoryItem.cs` (новый record): `Repository`, `Target` (`BackupTarget`), `Status` (`BackupItemStatus`), `Duration`, `Branch?`, `HeadCommit?`, `ArchiveFileName?`, `ChangedRefs`, `Error?`.
- `History/DiscoveryHistoryEvent.cs` (новый record): `Repository`, `PreviousFullName?`, `Kind`, `Decision?`, `DecidedBy?`.
- `History/DiscoveryEventKind.cs`: `New`, `Missing`, `Renamed`, `Returned`.
- `History/DiscoveryEventDecision.cs`: `Added`, `Skipped`, `Excluded`, `FolderRenamed`, `OldFolderKept` (список из FR-8.4).
- `History/DecisionMaker.cs`: `User`, `Automatic`.
- `History/RunHistoryException.cs`: база не открывается, не мигрирует или не принимает запись. Сообщение называет путь к базе.

**`GitHubBackup.Infrastructure`**
- `.csproj`: `Microsoft.EntityFrameworkCore.Sqlite` и `Microsoft.EntityFrameworkCore.Design` (`PrivateAssets=all`).
- `History/HistoryDbContext.cs` (internal sealed): три `DbSet`, каскадное удаление, уникальный индекс на `RunId`, строковая конверсия перечислений.
- `History/RunEntity.cs`, `RunItemEntity.cs`, `DiscoveryEventEntity.cs`: ключ `long Id`; `ChangedRefs` — примитивная коллекция EF Core (JSON-колонка).
- `History/SqliteRunHistoryStore.cs`: `IDbContextFactory<HistoryDbContext>`, контекст на операцию, порядок элементов и событий по `Id`; `DbUpdateException`/`SqliteException` → `RunHistoryException`.
- `History/HistoryDatabaseInitializer.cs` (`IHostedService`): создаёт папку через `IFileSystem`, затем `Database.MigrateAsync`; ошибки → `RunHistoryException`.
- `History/HistoryDatabaseLog.cs`: `[LoggerMessage]` о готовности базы.
- `History/HistoryDesignTimeDbContextFactory.cs`: только для `dotnet ef`.
- `History/HistoryConnectionString.cs`: `SqliteConnectionStringBuilder { DataSource = path, Pooling = false }`.
- `History/Migrations/*_InitialCreate.cs`, `.Designer.cs`, `HistoryDbContextModelSnapshot.cs`: сгенерированный код.
- `InfrastructureServiceCollectionExtensions.cs`: `AddDbContextFactory`, `IRunHistoryStore`, `AddHostedService<HistoryDatabaseInitializer>()`.
- `Hosting/ConfigurationErrorMessages.cs`: `RunHistoryException` → одна строка, код 2.

**Корень и `src/`**
- `src/Directory.Packages.props`: группа `Persistence`, последний патч 10.0.x.
- `.config/dotnet-tools.json`: локальный `dotnet-ef` той же версии.
- `src/.editorconfig`: секция для `History/Migrations/**.cs` — только если сгенерированный код ломает сборку или format.
- `CLAUDE.md`, `README.md`: `dotnet tool restore` и `dotnet ef migrations add <Name> --project src/GitHubBackup.Infrastructure --output-dir History/Migrations`.

## 4. Подход

1. Core: перечисления, record-типы, `IRunHistoryStore`, `RunHistoryException` без EF. Свои типы нужны: в `BackupItemResult` нет ветки, HEAD и ссылок, в `DiscoveryApplyResult` нет автора решения.
2. Пакеты и инструмент; `dotnet list package --vulnerable`.
3. Сущности, контекст, design-time factory; сгенерировать `InitialCreate` (`Infrastructure` — и целевой, и стартовый проект).
4. Проверить сгенерированный код под `TreatWarningsAsErrors` и `dotnet format`; подавления — только в секции `.editorconfig` для `Migrations`.
5. Store и initializer, регистрация в DI. Миграции применяются в `host.StartAsync`.

Ключевые решения:
- `Pooling = false`: иначе пул держит файл открытым, и временные папки в тестах не удаляются.
- Порядок по `Id`; `DateTimeOffset` хранится как есть, без сортировки на стороне SQL.
- Время передаёт вызывающий; store не читает часы.
- Счётчики по статусам выводятся из элементов (`RunHistoryEntry.Count`).
- Одна транзакция на запуск (`SaveChangesAsync` один раз).
- Папка `data` создаётся через `IFileSystem`; файл создаёт SQLite.

## 5. Тесты

**`Infrastructure.IntegrationTests/History/SqliteRunHistoryStoreTests`**
- `AddAsync_RunWithItemsAndDiscoveryEvents_GetAsyncReturnsEqualRun`
- `AddAsync_OptionalFieldsNull_ReadsBackNull`
- `AddAsync_Filter_ReadsBackFilter` (Theory)
- `AddAsync_EachStatus_StoredAsName` (Theory)
- `AddAsync_ExitCode_StoredAsNumber`
- `AddAsync_DryRun_ReadsBackFlag`
- `GetAsync_UnknownRunId_ReturnsNull`
- `AddAsync_SameRunIdTwice_ThrowsRunHistoryException`
- `AddAsync_CancelledToken_ThrowsOperationCanceledException`

**`Infrastructure.IntegrationTests/History/HistoryDatabaseInitializerTests`**
- `StartAsync_NoDataFolder_CreatesFolderAndDatabaseWithAllMigrations`
- `StartAsync_DatabaseUpToDate_KeepsRunsAndAppliesNothing`
- `StartAsync_PathIsExistingFolder_ThrowsRunHistoryExceptionNamingPath`

**`Infrastructure.IntegrationTests/History/HistoryDatabaseHostTests`**
- `StartAsync_DefaultSettings_CreatesDataHistoryDbInContentRoot`

**`Infrastructure.Tests`**
- `History/HistoryDbContextTests.HasPendingModelChanges_CurrentModel_ReturnsFalse`
- `InfrastructureServiceCollectionExtensionsTests`: store и hosted service зарегистрированы.
- `Hosting/ConfigurationErrorMessagesTests`: `RunHistoryException` → одно сообщение.

**`Core.Tests/History/RunHistoryEntryTests`**
- `Count_ItemsWithStatuses_ReturnsNumberPerStatus`

## 6. Проверка

| Критерий | Чем проверяется |
|---|---|
| Запись и чтение запуска | `SqliteRunHistoryStoreTests.AddAsync_RunWithItemsAndDiscoveryEvents_GetAsyncReturnsEqualRun` |
| Пустая база создаётся | `HistoryDatabaseInitializerTests.StartAsync_NoDataFolder_…`, `HistoryDatabaseHostTests` |
| Повторный старт без изменений | `StartAsync_DatabaseUpToDate_KeepsRunsAndAppliesNothing` |
| Необязательные поля, строки и числа | тесты `SqliteRunHistoryStoreTests` |
| Нет забытой миграции | `HistoryDbContextTests` |
| Сборка, анализаторы, format | `build-runner`, scope `full` |
| CI зелёный | CI в PR |

## 7. Вне рамок

- Запись реальных запусков из оркестратора и хостов (расширение `BackupItemResult`, `DiscoveryApplyResult`, `RunMode` в запросе) — новое issue, заблокированное #33.
- Просмотр истории в UI и выборки по дате (FR-8.7).
- История архивов из `.backup-state.json` (FR-8.5).
- Очистка истории (FR-8.7).
- Single-file-публикация с нативной `e_sqlite3.dll` (NFR-8).
- Старт хоста в App: initializer заработает, когда App получит Generic Host.

## 8. Открытые вопросы

Нет: все вопросы решены на ревью (раздел 9).

## 9. Решения

- Битая или недоступная база при старте останавливает запуск: одна строка ошибки с путём к базе и код 2 (`RunHistoryException` в `ConfigurationErrorMessages`). PRD не меняется.
- У событий `Missing` и `Returned` поля `Decision` и `DecidedBy` равны `null`. PRD не меняется.
- Счётчики по статусам выводятся из элементов запуска, отдельных колонок нет.
- `IRunHistoryStore` содержит только `AddAsync` и `GetAsync`; список запусков добавит задача FR-8.7.
- Базу создаёт старт хоста для любой команды, включая `validate` и `set-token` (FR-8.6).
- По итогам проверки безопасности `SqliteRunHistoryStore` маскирует текст ошибки через `ISecretMasker` перед записью: ошибка приходит из вывода git или HTTP, а токен не должен попасть в базу (S1).

## 10. Задачи

- [x] Core: `RunMode`, `IRunHistoryStore`, record-типы, перечисления, `RunHistoryException` с XML-doc
- [x] `Directory.Packages.props`, `.csproj` `Infrastructure`, `.config/dotnet-tools.json`; `dotnet list package --vulnerable`
- [x] Infrastructure: сущности, `HistoryDbContext`, `HistoryConnectionString`, design-time factory
- [x] Сгенерировать `InitialCreate`; проверить сборку и format
- [x] `SqliteRunHistoryStore`, `HistoryDatabaseInitializer`, `HistoryDatabaseLog`
- [x] Регистрация в DI
- [x] `ConfigurationErrorMessages` для `RunHistoryException`
- [x] Тесты по разделу 5
- [x] Полный прогон тестов, временные папки удаляются
- [x] `CLAUDE.md` и `README.md`: команды `dotnet tool restore` и `dotnet ef migrations add`
- [x] Follow-up issue [#95](https://github.com/askrinnik/GitHubBackup/issues/95) «F3.12: Record runs in run history» (blocked by #33)
