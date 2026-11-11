# План: F2.6: Discovery in the run orchestrator

| Поле | Значение |
|---|---|
| Issue | [#25](https://github.com/askrinnik/GitHubBackup/issues/25) |
| Название | F2.6: Discovery in the run orchestrator |
| Lane | Feature |
| Сложность | L |
| Дата | 2026-11-11 |

## 1. Цель

`BackupOrchestrator` выполняет шаги 5–7 из §9. Шаг 5 — обнаружение через `IRepositoryDiscoveryService` (FR-2.1–2.5). Шаг 6 — решения по стратегии текущего режима через `IDiscoveryDecisionApplier` и сохранение `backup-config.json` (FR-2.6, FR-2.8). Шаг 7 — список работ строится из конфигурации после решений, поэтому новые подтверждённые репозитории бэкапятся в том же запуске. С фильтром `--repo` обнаружение не выполняется, с `--account` выполняется только для этого аккаунта (FR-2.7). Сбой API во время обнаружения — критическая ошибка (FR-9.10, FR-11.5). События обнаружения дают код возврата `3` (FR-9.10).

## 2. Критерии приёмки

1. (issue) Тест полного цикла с поддельным API: новый репозиторий подтверждается и бэкапится.
2. (issue) Всё покрыто тестами, CI зелёный.
3. (добавлено, FR-2.7) Запуск с `--repo` не выполняет обнаружение.
4. (добавлено, FR-2.7) Запуск с `--account` выполняет обнаружение только для этого аккаунта, и список работ содержит только его репозитории.
5. (добавлено, §9 шаг 6) dry-run не сохраняет `backup-config.json`. Предлагается перенести в issue `--dry-run` (см. Открытые вопросы, п. 1).
6. (добавлено, §9 шаг 7) Список работ строится из конфигурации после решений. Новые подтверждённые репозитории в него входят. Исключённые, недоступные (включая помеченные `Unavailable` в этом запуске) и пропущенные не обрабатываются: ни git, ни архива, ни записи состояния.
7. (добавлено, FR-9.10, FR-11.5) Сбой API при обнаружении — `GitHubApiException`, включая исчерпанный лимит с временем сброса и 401. Это критическая ошибка: репозитории не обрабатываются, решения не запрашиваются.
8. (добавлено, сверка) Невалидные изменения конфига (`BackupConfigException` из applier) и ошибка записи (`IOException`) — тоже критическая ошибка.
9. (добавлено, сверка) `--account` с неотслеживаемым логином — критическая ошибка (её источник — `ArgumentException` из `DiscoverAsync`).
10. (добавлено, сверка) `--repo` с репозиторием, которого нет в `backup-config.json`, — критическая ошибка (см. Открытые вопросы, п. 3).
11. (добавлено, NFR-4) Отмена во время обнаружения или решений пробрасывается как `OperationCanceledException`. Не обрабатывается ни один репозиторий, ничего не сохраняется.
12. (добавлено, FR-9.10) `ExitCode.DiscoveryEvents` (`3`) возвращается, когда ошибок нет, но были новые, пропавшие или переименованные репозитории. Коды `1` и `2` имеют приоритет.
13. (добавлено, сверка) Обнаружение без находок не вызывает applier и не пишет конфиг. Код возврата — `0`.
14. (добавлено, план #24) Флаг `HasWiki` берётся из метаданных, которые собрало обнаружение. Запрос `GetRepositoryAsync` остаётся запасным путём: для `--repo` и для репозиториев, которых нет в собранных данных.

## 3. Изменения PRD

Обязательных изменений нет. Если по п. 3 или 4 Открытых вопросов будет выбран вариант, которого нет в PRD, в FR-9.2 дописывается поведение `--repo` для репозитория вне конфига и состав `--account` (с повышением версии и строкой истории).

## 4. Затрагиваемые проекты и типы

**GitHubBackup.Core**
- `Runs/BackupRunFilter` (новый) — record с фабриками `All`, `ForAccount(login)`, `ForRepository(owner, name)`.
- `Runs/BackupRunRequest` (новый) — `Filter` и `DecisionHandler` (`IDiscoveryDecisionHandler`). Стратегию режима выбирает хост на каждый запуск.
- `Runs/IBackupOrchestrator` (изменён) — `RunAsync(BackupRunRequest request, CancellationToken cancellationToken)`.
- `Runs/BackupOrchestrator` (изменён) — зависимости `IRepositoryDiscoveryService` и `IDiscoveryDecisionApplier`, порядок шагов §9, фильтрация списка работ, критические ошибки обнаружения, `HasWiki` из обнаружения.
- `Runs/BackupOrchestratorLog` (изменён) — сообщения: discovery skipped, repository filtered out, not confirmed.
- `Runs/BackupRunResult` (изменён) — `Discovery` (`DiscoveryApplyResult?`) и правило `3` в `ExitCode`.
- `Discovery/DiscoveryResult` (изменён) — init-свойство `Repositories` (`IReadOnlyDictionary<long, GitHubRepository>`, по умолчанию пусто).
- `Discovery/RepositoryDiscoveryService` (изменён) — заполняет `Repositories`.
- `Discovery/AutomaticDiscoveryDecisionHandler` (новый) — политика FR-9.4: новый → `Add()`, переименованный → `RenameDecision.KeepArchiveFolder` (П-1).
- `CoreServiceCollectionExtensions` (изменён) — регистрация `AutomaticDiscoveryDecisionHandler`.

**GitHubBackup.Infrastructure** — изменений нет.

**GitHubBackup.Cli**
- `BackupCommandHandler` (изменён) — вызывает `RunAsync(new(BackupRunFilter.All, handler), ct)` с выбранной временной стратегией (см. Открытые вопросы, п. 2).

**GitHubBackup.App** — изменений нет: хост WPF пока не вызывает оркестратор.

**README.md** — статус, описание `backup`, код `2` (сбой API при обнаружении), код `3`.

## 5. Подход

1. Модель запуска: `BackupRunFilter`, `BackupRunRequest`, новая сигнатура `IBackupOrchestrator`; `BackupRunResult.Discovery` и правило кода `3`.
2. `DiscoveryResult.Repositories` заполняется в `RepositoryDiscoveryService.CollectAsync` и `FindAsync`.
3. `AutomaticDiscoveryDecisionHandler`.
4. Оркестратор, порядок §9: загрузка → инструменты → токен → очистка `.tmp` → шаг 5 → шаг 6 → шаг 7 → обработка. `ExcludeMatching` переносится в шаг 7.
   - Шаг 5: `--repo` — пропуск с записью в лог; иначе `DiscoverAsync(config, filter.AccountLogin, ct)`.
   - Шаг 6: при `discovery.HasChanges` вызывается `ApplyAsync(config, discovery, request.DecisionHandler, tools.Git.Path, ct)`; applier меняет `config` на месте.
   - Критические ошибки шагов 5–6: `GitHubApiException`, `ArgumentException`, `BackupConfigException`, `IOException`, `UnauthorizedAccessException` → `Stop(...)`.
   - `OperationCanceledException` из шагов 5–6 пробрасывается.
   - Шаг 7: `BackupWorkItem.CreateAll` → фильтр → `ExcludeMatching`. Неактивные остаются `Skipped` (FR-9.8). Неподтверждённые новые добавляются в результаты как `Skipped` без обработки.
   - Wiki: `IsWikiEnabledAsync` сначала смотрит `discovery.Repositories[item.Id]`, затем API.
5. DI и CLI.
6. README.

Ключевые решения:
- Обработчик решений передаётся в запросе, а не через DI в оркестратор: UI подставит свой диалог, CLI выберет интерактив или `--silent` (FR-9.3, FR-9.4, FR-12.2).
- Автоматическая стратегия живёт в Core, чтобы не дублировать её в хостах (FR-12.7).
- Флаг `DryRun` в запрос не добавляется (см. Открытые вопросы, п. 1).

## 6. Тесты

**GitHubBackup.Core.Tests / `Runs/BackupOrchestratorTests`** (substitutes `IRepositoryDiscoveryService`, `IDiscoveryDecisionApplier`, `IDiscoveryDecisionHandler`; по умолчанию `DiscoveryResult.Empty`):
- `RunAsync_NoFilter_DiscoversAllAccountsAndAppliesWithHandlerAndGitPath`
- `RunAsync_NoDiscoveryChanges_DoesNotApplyAndReturnsSuccess`
- `RunAsync_NewRepositoryAddedByApplier_IsBackedUpInSameRun`
- `RunAsync_EntryMarkedUnavailableByApplier_ReportsSkippedWithoutGit`
- `RunAsync_NewRepositoryNotConfirmed_ReportsSkippedWithoutProcessing`
- `RunAsync_RepositoryFilter_SkipsDiscoveryAndProcessesOnlyThatRepository`
- `RunAsync_RepositoryFilterNotInConfig_ReturnsCritical`
- `RunAsync_RepositoryFilterMatchesExclusion_ProcessesNothing`
- `RunAsync_AccountFilter_DiscoversThatAccountAndProcessesOnlyItsRepositories`
- `RunAsync_AccountNotTracked_ReturnsCritical`
- `[Theory] RunAsync_DiscoveryFails_ReturnsCriticalWithoutApplyingOrProcessing`
- `[Theory] RunAsync_ApplyFails_ReturnsCriticalWithoutProcessing`
- `[Theory] RunAsync_CancelledDuringDiscoveryOrDecision_ThrowsWithoutProcessing`
- `RunAsync_InvalidConfigOrToolMissing_DoesNotDiscover`
- `RunAsync_TemporaryFolderEmptiedBeforeDiscovery`
- `[Theory] RunAsync_DiscoveryEvents_ExitCode`
- `RunAsync_DiscoveredRepository_WikiFlagFromDiscoveryWithoutApiCall`, `RunAsync_RepositoryFilter_WikiFlagFromApi`
- существующие тесты переводятся на `RunAsync(request)`.

**GitHubBackup.Core.Tests**: `Discovery/AutomaticDiscoveryDecisionHandlerTests` (новый), `Discovery/RepositoryDiscoveryServiceTests` (`Repositories`), `Runs/BackupRunResultTests` (правило `3`), `CoreServiceCollectionExtensionsTests`.

**GitHubBackup.Cli.Tests / `BackupCommandHandlerTests`**: передаются `BackupRunFilter.All` и выбранный обработчик; код `3` проходит насквозь.

**GitHubBackup.Infrastructure.IntegrationTests / `Runs/BackupOrchestratorTests`**:
- фикстура с реальными `RepositoryDiscoveryService`, `DiscoveryDecisionApplier`, `JsonBackupConfigStore`, `AutomaticDiscoveryDecisionHandler` и рукописным `FakeGitHubApi`;
- `RunAsync_NewAccountRepository_IsAddedToConfigAndBackedUp`: первый запуск добавляет запись в конфиг и создаёт архив (код `3`), второй — `UpToDate` (код `0`).

## 7. Проверка

| Критерий | Как проверяется |
|---|---|
| 1 | `RunAsync_NewAccountRepository_IsAddedToConfigAndBackedUp` (IntegrationTests) |
| 2 | полная сборка, тесты, `dotnet format --verify-no-changes`, CI на PR |
| 3 | `RunAsync_RepositoryFilter_SkipsDiscoveryAndProcessesOnlyThatRepository` |
| 4 | `RunAsync_AccountFilter_...` и тесты сервиса с `accountLogin` |
| 5 | перенос в issue `--dry-run` (после подтверждения) |
| 6 | `RunAsync_NewRepositoryAddedByApplier_...`, `RunAsync_EntryMarkedUnavailableByApplier_...`, `RunAsync_NewRepositoryNotConfirmed_...` |
| 7–10 | тесты `..._ReturnsCritical...` |
| 11 | `RunAsync_CancelledDuringDiscoveryOrDecision_ThrowsWithoutProcessing` |
| 12 | `RunAsync_DiscoveryEvents_ExitCode`, `BackupRunResultTests` |
| 13 | `RunAsync_NoDiscoveryChanges_DoesNotApplyAndReturnsSuccess` |
| 14 | тесты wiki-флага |

## 8. Вне рамок

- Разбор `--account`, `--repo`, `--silent`, `--dry-run` в CLI — фаза 3. Core уже принимает `BackupRunFilter`.
- Интерактивный обработчик CLI (FR-9.3, FR-9.6) — фаза 3; диалог WPF (FR-12.2) — фаза 4.
- dry-run целиком (FR-9.5) — issue `--dry-run`.
- Вывод событий обнаружения в консольной сводке — сейчас только лог и `BackupRunResult.Discovery`.
- Блокировка запуска (NFR-SYS-1), история SQLite — фаза 3.
- Отдельная ветка для анонимного режима не нужна: обнаружение работает по публичным спискам (FR-1.8).

## 9. Открытые вопросы

1. dry-run: не добавлять `DryRun` в #25, критерий 5 перенести в issue `--dry-run`.
2. Временная стратегия CLI до фазы 3: автоматическое добавление (политика `--silent`) или пропуск всех новых.
3. `--repo` вне конфига: критическая ошибка.
4. `--account`: только записи аккаунта, без отдельных репозиториев того же owner.
5. `Reactivated` не считается событием для кода `3`.
6. Неподтверждённые новые показываются в сводке как `Skipped`.

## 10. Решения

Приняты на ревью плана (2026-11-11):

1. Флаг `DryRun` в #25 не добавляется. Критерий 5 переносится в [#29](https://github.com/askrinnik/GitHubBackup/issues/29) (F3.4: Dry run, FR-9.5).
2. CLI-команда `backup` до фазы 3 использует `AutomaticDiscoveryDecisionHandler` (политика `--silent`, FR-9.4): новые репозитории добавляются автоматически, переименования сохраняют старую папку архивов (П-1).
3. `--repo` для репозитория вне `backup-config.json` — критическая ошибка.
4. `--account` обрабатывает только записи аккаунта, без отдельных репозиториев того же owner. PRD не меняется.
5. `Reactivated` не даёт код `3`.
6. Неподтверждённые новые репозитории показываются в сводке как `Skipped`.

## 11. Задачи

- [x] Core: `BackupRunFilter`, `BackupRunRequest`; новая сигнатура `IBackupOrchestrator.RunAsync`.
- [x] Core: `BackupRunResult.Discovery` и правило `ExitCode.DiscoveryEvents`.
- [x] Core: `DiscoveryResult.Repositories`, заполнение в `RepositoryDiscoveryService`.
- [x] Core: `AutomaticDiscoveryDecisionHandler` и регистрация в `AddGitHubBackupCore`.
- [x] Core: `BackupOrchestrator` — порядок §9, шаги 5–7, фильтры, критические ошибки, отмена, `Skipped` для неподтверждённых, `HasWiki` из обнаружения; сообщения в `BackupOrchestratorLog`.
- [x] Cli: `BackupCommandHandler` строит запрос с выбранной стратегией.
- [x] Core.Tests: тесты оркестратора, `AutomaticDiscoveryDecisionHandlerTests`, `RepositoryDiscoveryServiceTests`, `BackupRunResultTests`, `CoreServiceCollectionExtensionsTests`.
- [x] Cli.Tests: `BackupCommandHandlerTests`.
- [x] IntegrationTests: `FakeGitHubApi`, фикстура с реальными сервисами обнаружения, тест полного цикла.
- [x] README: статус, `backup`, коды `2` и `3`.
- [x] Сборка, тесты, формат; вычитка добавленных комментариев.

## 12. Дополнение: недопустимые имена при обнаружении

Security review нашёл риск для доступности. GitHub может вернуть owner или имя, которое не годится как имя папки: `con`, `nul`, `x.wiki`, имя с точкой в конце. Автоматическая стратегия добавляет такой репозиторий, валидатор конфига отклоняет сохранение, и каждый запуск останавливается с критической ошибкой.

Решение пользователя (2026-11-11): исправить в рамках #25, сначала PRD. PRD 0.16 добавляет FR-2.9 и строку о проверке имён в §7.3.

Критерии приёмки:

15. (FR-2.9) Репозиторий с недопустимым owner или именем не попадает в новые репозитории (FR-2.2, FR-2.3). Программа пишет предупреждение.
16. (FR-2.9) Отслеживаемый репозиторий, найденный под недопустимым новым именем, не считается переименованным и не считается пропавшим. Запись остаётся под прежним именем.
17. (FR-2.9) Такой репозиторий не останавливает запуск: конфиг сохраняется без него, остальные репозитории обрабатываются.

Подход:

- Проверка стоит в `RepositoryDiscoveryService`, до классификации. Используются `GitHubUrl.IsValidLogin` и `GitHubUrl.IsValidRepositoryName`. Проверка в Core работает с любой реализацией `IGitHubApi`.
- Предупреждение пишется через `[LoggerMessage]` и содержит `owner/name`.

Задачи:

- [x] PRD 0.16: FR-2.9, §7.3, строка истории.
- [x] Core: проверка имён в `RepositoryDiscoveryService` для новых и переименованных репозиториев; предупреждение в лог.
- [x] Core.Tests: `RepositoryDiscoveryServiceTests` (новые и переименование с `con`, `x.wiki`, точкой в конце, недопустимым owner).
- [x] IntegrationTests: в полном цикле недопустимое имя рядом с допустимым; запуск не останавливается, конфиг содержит только допустимый репозиторий.
- [x] README, если он описывает обнаружение.
- [x] Сборка, тесты, формат.
