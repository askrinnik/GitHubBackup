# План: #95 F3.12: Record runs in run history

| | |
|---|---|
| Issue | [#95](https://github.com/askrinnik/GitHubBackup/issues/95) |
| Заголовок | F3.12: Record runs in run history |
| Лейн | Feature |
| Сложность | L |
| Дата | 2026-11-12 |

## 1. Цель

Каждый запуск бэкапа записывается в `data\history.db` через `IRunHistoryStore.AddAsync`. Запись содержит поля FR-8.2 (начало, конец, режим, `DryRun`, фильтр, код возврата, счётчики), элементы FR-8.3 (статус, длительность, ветка, HEAD, архив, изменившиеся ссылки, ошибка) и события обнаружения FR-8.4 (вид, решение, кто принял решение). Записываются также dry-run (П-5) и отменённый запуск. Запись делается в конце запуска, до сводки и кода возврата (§9, шаг 9). Сбой записи истории не меняет код возврата по FR-9.10. Хранилище, модель и база уже есть (#33), но вызова записи нет.

## 2. Критерии приёмки

1. После запуска CLI в `data\history.db` есть запись запуска с элементами и событиями обнаружения
2. Dry-run записывается с флагом `DryRun`
3. Поведение при ошибке записи истории покрыто тестом
4. Покрыто тестами, CI зелёный

Добавлено по PRD и по сверке с кодом:

- (доб.) Отменённый запуск записывается с кодом `2`. Записываются элементы, обработанные до отмены, прерванный элемент (`Cancelled`) и необработанные (`Skipped`). Отмена до захвата блокировки тоже даёт запись, без элементов.
- (доб.) Запуск, остановленный критической ошибкой внутри оркестратора, записывается с кодом `2` и без элементов: `backup-config.json`, инструменты, `--repo` нет в настройках, блокировка занята, сбой API при обнаружении, сбой сохранения решений.
- (доб.) `Mode` в записи равен `Silent` при `--silent`, иначе `Interactive`. `AccountFilter` и `RepositoryFilter` берутся из `--account` и `--repo`.
- (доб.) Решения обнаружения `Added`, `Skipped`, `Excluded`, `FolderRenamed`, `OldFolderKept` записываются с `DecidedBy`: `Automatic` в тихом режиме, `User` в интерактивном. События `Missing` и `Returned` записываются без решения, как задано моделью `DiscoveryHistoryEvent`. В dry-run события записываются без решения.
- (доб.) Ошибка записи истории: код возврата не меняется, сводка выводится полностью, в лог пишется `Error`, в консоль выводится строка `Warning: …` (раздел 3).

## 3. Изменения PRD

PRD не задаёт поведение при сбое записи и не говорит, какие запуски записываются. Версия **0.21**:

- В §6.8 добавить:
  - **FR-8.8. Какие запуски записываются.** Запись делается в конце каждого запуска бэкапа, дошедшего до общего ядра: обычный запуск, dry-run (П-5), отменённый запуск (NFR-4; в том числе отменённый до захвата блокировки) и запуск, остановленный критической ошибкой с кодом `2` (настройки `backup-config.json`, инструменты, занятая блокировка, сбой API при обнаружении). У остановленного запуска нет элементов. Не записываются: ошибки командной строки и `appsettings.json`, недоступная база истории, интерактивный запуск с перенаправленным вводом (FR-9.6). В этих случаях бэкап не начинается. Отмена (Ctrl+C) не прерывает запись истории.
  - **FR-8.9. Ошибка записи истории** не меняет код возврата (FR-9.10) и не прерывает сводку. Ошибка с путём базы пишется в лог. Сводка показывает строку предупреждения о том, что запуск не записан в историю.
- В FR-9.7 добавить: «Если запуск не записан в историю, сводка показывает предупреждение (FR-8.9)».
- В §14 добавить строку 0.21.

Обоснование. Состояние архивов главное (FR-8.1), история нужна для просмотра (FR-8.7). Сбой истории не должен превращать успешный бэкап в код `2` и ломать работу по расписанию. Похожий приём уже есть: `ArchiveAsync` пишет состояние через `CancellationToken.None`, чтобы отмена не теряла запись.

## 4. Затрагиваемые проекты и типы

**GitHubBackup.Core**

- `Runs/BackupItemResult` (изм.): init-свойства `Branch`, `HeadCommit`, `ChangedRefs` (`IReadOnlyList<string>`, по умолчанию `[]`).
- `Runs/BackupRunRequest` (изм.): init-свойство `RunMode Mode`, по умолчанию `RunMode.Interactive`.
- `Runs/BackupRunResult` (изм.): init-свойство `string? HistoryWarning`; не участвует в `ExitCode`.
- `Git/RefSnapshot` (изм.): метод `GetChangedRefs(RefSnapshot? previous)` — имена добавленных, удалённых или сдвинутых ссылок, порядок `Ordinal`. Без `previous` — все ссылки.
- `Runs/BackupOrchestrator` (изм.): `BackupAsync` возвращает приватный record (`Status`, `Archive`, `Warning`, `Branch`, `HeadCommit`, `ChangedRefs`) вместо кортежа. `ProcessAsync` и `FinishWiki` переносят данные в `BackupItemResult`. HEAD берётся через `TryGetValue`. `ChangedRefs = detection.Refs.GetChangedRefs(LastArchive?.Refs)` только для `Archived`, `WouldArchive` или `Warning` с архивом; для `UpToDate` список пуст, `Branch` и `HeadCommit` заполнены.
- `History/RunHistoryEntryBuilder` (нов., `internal static`): собирает `RunHistoryEntry` из `runId`, `startedAt`, `finishedAt`, `BackupRunRequest` и `BackupRunResult` по таблице раздела 5.
- `Runs/RecordingBackupOrchestrator` (нов., `sealed`, `IBackupOrchestrator`): декоратор над `BackupOrchestrator`. Зависимости: `IRunHistoryStore`, `IRunContext`, `TimeProvider`, `ILogger`.
- `Runs/RecordingBackupOrchestratorLog` (нов.): `[LoggerMessage]` `RunRecorded` (Information) и `RunNotRecorded` (Error, с исключением и путём базы).
- `CoreServiceCollectionExtensions` (изм.): `BackupOrchestrator` — конкретный singleton; `IBackupOrchestrator` — фабрика, создающая `RecordingBackupOrchestrator`.

**GitHubBackup.Cli**

- `BackupCommandHandler` (изм.): `Mode = options.Silent ? RunMode.Silent : RunMode.Interactive`.
- `BackupSummaryRenderer` (изм.): выводит `WarningPrefix + result.HistoryWarning` жёлтой строкой после предупреждений конфигурации, до ветки критических ошибок; строка выводится и для остановленного запуска.

**Документы:** `docs/PRD.md` (раздел 3).

Infrastructure не меняется: `SqliteRunHistoryStore` уже маскирует секреты в `Error` и бросает `RunHistoryException`.

## 5. Подход

Порядок: PRD → Core (данные элементов → построитель → декоратор → DI) → Cli → тесты.

1. **Декоратор, а не правка оркестратора.** У `BackupOrchestrator` 17 зависимостей и несколько выходов `RunAsync` (`Stop`, отмена, обычный результат). Декоратор оборачивает все выходы в одном месте и проверяется с подменённым внутренним оркестратором. Код в Core, поэтому UI получит запись без изменений (FR-12.7).
2. **Алгоритм `RecordingBackupOrchestrator.RunAsync`:**
   - `startedAt = timeProvider.GetLocalNow()`;
   - вызвать `inner.RunAsync`;
   - `OperationCanceledException` при отменённом токене (отмена до блокировки) → записать «отменённый, без элементов, `ExitCode.Critical`» и пробросить исключение;
   - иначе построить вход из результата и записать; `AddAsync` всегда получает `CancellationToken.None`;
   - `RunHistoryException` → лог `RunNotRecorded`, вернуть `result with { HistoryWarning = "The run was not recorded in the run history: …" }`;
   - остальные исключения не перехватываются.
3. **Таблица событий:**

| Источник | Вид | Решение | `DecidedBy` |
|---|---|---|---|
| `Discovery.Added` / `Skipped` / `Excluded` | `New` | `Added` / `Skipped` / `Excluded` | по `Mode` |
| `Discovery.MarkedUnavailable` | `Missing` | — | — |
| `Discovery.Renamed` (`PreviousFullName = OldFullName`, `Repository = NewFullName`) | `Renamed` | `Applied == RenameArchiveFolder ? FolderRenamed : OldFolderKept` | по `Mode` |
| `Discovery.Reactivated` | `Returned` | — | — |
| `PlannedDiscovery` (dry-run): `NewInAccounts` + `NewStandalone`, `Missing`, `Renamed`, `Returned` | соответствующий | — | — |

   `DecidedBy`: `Silent` → `Automatic`, `Interactive` и `Ui` → `User`. Порядок событий: New, Missing, Renamed, Returned.
4. **Отклонение от текста issue.** «Кто решил» выводится из `BackupRunRequest.Mode`, а не хранится в `DiscoveryApplyResult`: хост уже выбирает обработчик решений по режиму. Пропавшие и вернувшиеся уже есть в `DiscoveryApplyResult` (`MarkedUnavailable`, `Reactivated`).
5. **Элементы.** `Repository`, `Target`, `Status`, `Duration`, `ArchiveFileName`, `Error` (из `Message`), `Branch`, `HeadCommit`, `ChangedRefs` — из `BackupItemResult`. Счётчики FR-8.2 вычисляются из элементов.
6. **Поля запуска.** `RunId = IRunContext.RunId` (как в логе), `AccountFilter = Filter.AccountLogin`, `RepositoryFilter = Filter.RepositoryFullName`, `ExitCode = result.ExitCode`.
7. **Безопасность.** Вход несёт только имена, хеши и тексты ошибок; `SqliteRunHistoryStore` маскирует `Error`. Строка предупреждения содержит путь базы и текст SQLite, токена там нет.

## 6. Тесты

**GitHubBackup.Core.Tests**

- `Git/RefSnapshotTests`: `GetChangedRefs` — без предыдущего снимка, равные снимки, сдвинутая, добавленная и удалённая ветка, тег, порядок `Ordinal`.
- `Runs/BackupOrchestratorTests`: `Branch`, `HeadCommit`, `ChangedRefs` для `Archived`, `UpToDate`, `Warning` с откатом, `WouldArchive`, `WouldClone`, `Failed`, wiki, пустой репозиторий.
- `History/RunHistoryEntryBuilderTests` (нов.): поля запуска и фильтр; `Mode` → `DecidedBy`; все строки таблицы событий; dry-run; остановленный запуск; отменённый запуск.
- `Runs/RecordingBackupOrchestratorTests` (нов.): запись с `RunId`, `StartedAt`, `FinishedAt`; `CancellationToken.None` при отменённом токене; `RunHistoryException` → тот же `ExitCode`, `HistoryWarning`, лог `Error`; отмена до блокировки → запись и проброс; отмена до блокировки при сбое записи; другое исключение → без записи, проброс.
- `CoreServiceCollectionExtensionsTests`: `IBackupOrchestrator` разрешается в `RecordingBackupOrchestrator`.

**GitHubBackup.Infrastructure.IntegrationTests**

- `Runs/BackupOrchestratorTests`: декоратор поверх реального оркестратора, `SqliteRunHistoryStore` (`TemporaryHistoryDatabase`) и локальных bare-репозиториев — архив, ветка, HEAD, `ChangedRefs`; новый репозиторий в тихом режиме → `New/Added/Automatic`.

**GitHubBackup.Cli.Tests**

- `BackupCommandHandlerTests`: `Mode` по `--silent`, в том числе в dry-run; `HistoryWarning` не меняет код возврата.
- `BackupSummaryRendererTests`: строка предупреждения в обычной сводке и при критической ошибке.
- `BackupHistoryEndToEndTests` (нов.): реальный хост через `CliApplication`, `TemporaryContentRoot`, WireMock.Net на loopback. Случаи: `backup --silent` с пропавшим репозиторием (код `3`, `Silent`, элемент `Skipped`, событие `Missing`); `backup --dry-run` (`IsDryRun`, событие без решения); `backup --silent --repo owner/off` (`RepositoryFilter`). В `GitHubBackup.Cli.Tests.csproj` добавить `WireMock.Net`.

## 7. Проверка

| Критерий | Как проверяется |
|---|---|
| 1 | `BackupHistoryEndToEndTests` (silent); интеграционный тест оркестратора с SQLite |
| 2 | `BackupHistoryEndToEndTests` (dry-run), `RunHistoryEntryBuilderTests` |
| 3 | `RecordingBackupOrchestratorTests`, `BackupSummaryRendererTests`, `BackupCommandHandlerTests` |
| 4 | полная сборка, тесты решения, `dotnet format --verify-no-changes`, CI pull request |
| (доб.) | `RunHistoryEntryBuilderTests`, `RecordingBackupOrchestratorTests`, e2e с `--repo` |

## 8. Вне рамок

- **UI (`RunMode.Ui`).** `GitHubBackup.App` пока не запускает бэкап. `IRunContext` — singleton на хост: несколько запусков в одном процессе UI получат один `RunId`. Нужен отдельный issue вместе с запуском бэкапа из UI.
- **Текст критической ошибки на уровне запуска.** В `RunHistoryEntry` нет такого поля, FR-8.2 его не требует.
- **Запуски, которые не доходят до оркестратора** — по FR-8.8 не записываются.
- **e2e со сбоем записи на реальной базе** — сбой покрыт unit-тестами.
- **Полная e2e-инфраструктура CLI с clone через file-URL** (§11).

## 9. Открытые вопросы

1. Согласовать FR-8.8 и FR-8.9.
2. Записывать ли запуск, получивший отказ из-за занятой блокировки (код `2`)? Предложение: да.
3. Интерактивный запуск с перенаправленным вводом (FR-9.6) не записывается.
4. «Кто решил» выводится из `Mode`.
5. `ChangedRefs` для первого архива — все ссылки снимка.
6. `BackupRunRequest.Mode` со значением по умолчанию `Interactive`.

## 10. Решения

Ревью 2026-11-12: все предложения раздела 9 приняты.

1. PRD 0.21 с FR-8.8 и FR-8.9 принят как в разделе 3.
2. Отказ из-за занятой блокировки (код `2`) записывается.
3. Интерактивный запуск с перенаправленным вводом (FR-9.6) не записывается.
4. «Кто решил» выводится из `BackupRunRequest.Mode`.
5. `ChangedRefs` для первого архива содержит все ссылки снимка.
6. `BackupRunRequest.Mode` не `required`, по умолчанию `Interactive`.

## 11. Задачи

- [x] PRD 0.21: FR-8.8, FR-8.9, дополнение FR-9.7, строка в §14
- [x] `RefSnapshot.GetChangedRefs` и тесты
- [x] `BackupItemResult`: `Branch`, `HeadCommit`, `ChangedRefs`
- [x] `BackupOrchestrator` заполняет новые поля, тесты
- [x] `BackupRunRequest.Mode`, `BackupRunResult.HistoryWarning`
- [x] `RunHistoryEntryBuilder` и тесты
- [x] `RecordingBackupOrchestrator`, лог и тесты
- [x] DI-декоратор и тест регистрации
- [x] `BackupCommandHandler` передаёт `Mode`, тесты
- [x] `BackupSummaryRenderer` выводит `HistoryWarning`, тесты
- [x] Интеграционный тест: декоратор, реальный оркестратор и SQLite
- [x] `WireMock.Net` в `GitHubBackup.Cli.Tests` и `BackupHistoryEndToEndTests`
- [x] Сборка, тесты, `dotnet format --verify-no-changes`
- [x] Проверка комментариев по `csharp.md`
