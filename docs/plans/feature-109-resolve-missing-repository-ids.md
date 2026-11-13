# План: #109 F2.7: Resolve missing repository IDs from GitHub

| Поле | Значение |
|---|---|
| Issue | [#109](https://github.com/askrinnik/GitHubBackup/issues/109) |
| Заголовок | F2.7: Resolve missing repository IDs from GitHub |
| Lane | Feature |
| Сложность | L |
| Дата | 2026-11-13 |

## 1. Цель

Запись репозитория в `backup-config.json` (отдельная или в списке аккаунта) может быть без `id`. При запуске программа сама находит ID по имени (`GET /repos/{owner}/{repo}`) и записывает его в файл. Так произвольный ID, похожий на чужой репозиторий, больше не даёт ложного переименования (FR-2.5). Ложное переименование с `--silent` записало бы в настройки чужой репозиторий.

Требования: FR-1.7, новое FR-2.10, FR-2.8, FR-9.5, FR-9.9, FR-9.10, §7.3, §9.

## 2. Критерии приёмки

Из issue, дословно:

- PRD обновлён: версия, строка истории, FR-1.7, §6.2, FR-9.5, §7.3
- Тест: запись без `id` (отдельная и в аккаунте) проходит валидацию; отрицательный `id` — ошибка; две записи без id не считаются дублями
- Тест: запуск находит id по имени, записывает его в `backup-config.json` и бэкапит репозиторий; код возврата не `3` только из-за заполнения id
- Тест: для записи без id не выполняется поиск по id и не возникает переименование
- Тест: запись без id, которой нет на GitHub, получает `Failed`, остаётся без id и без `Unavailable`; остальные репозитории обработаны
- Тест: интерактивный `--dry-run` спрашивает про запись без id; «да» записывает id и больше ничего не меняет; «нет» не меняет файл, но репозиторий проверяется и есть в таблице
- Тест: `--dry-run --silent` и `--dry-run` с перенаправленным вводом записывают id без вопроса и больше ничего не меняют
- Тест: обычный интерактивный `backup` записывает id без вопроса
- README обновлён
- Покрыто тестами, CI зелёный

Добавлено проверкой покрытия:

- (добавлено) Запись без id под `--repo` тоже получает ID, хотя обнаружения нет. При `--account` ID ищется только для записей этого аккаунта.
- (добавлено) Запись без id, которая подпадает под `exclude`, не ищется: нет запроса к API и нет `Failed`.
- (добавлено) Найденный ID уже есть у другой записи (например, старое имя с ID и новое имя без ID). Тогда ID не записывается, элемент получает `Failed` с причиной, которая называет эту запись. Иначе файл с дублем ID не прошёл бы валидацию при следующем запуске.
- (добавлено) Другой сбой API при поиске (лимит, 401, 5xx) — критическая ошибка, код `2`, как сбой при обнаружении.
- (добавлено) Отмена во время поиска или вопроса — код `2`, файл не меняется.
- (добавлено) `validate` не обращается к GitHub, и запись без id проходит проверку.
- (добавлено) При сохранении запись без найденного ID пишется без свойства `id`, а не с `"id": 0`.
- (добавлено) Если dry-run записал ID, строка над таблицей это говорит. Сейчас она утверждает «nothing … saved».
- (добавлено) Если вместе с ID есть изменения обнаружения, `backup-config.json` записывается один раз, и `.bak` хранит исходный файл.

## 3. Изменения требований (PRD 0.21 → 0.22, дата 2026-11-13)

**FR-1.7** (замена):

> **FR-1.7.** Каждый репозиторий в настройках хранит **GitHub ID** (числовой, неизменный при переименовании). По нему распознаётся переименование (FR-2.5). В записи, добавленной вручную, `id` можно не указывать: программа найдёт его по имени при запуске (FR-2.10). Произвольный ID указывать нельзя: запись с ID другого репозитория будет принята за переименованный репозиторий.

**FR-2.10** (новое, в §6.2):

> **FR-2.10. Запись без ID.** Запись без `id` (или с `id: 0`) — отдельная или в списке аккаунта — ищется при запуске по имени: `GET /repos/{owner}/{repo}`. ID берётся из ответа. Поиск идёт до обнаружения и только для записей, которые обрабатывает запуск: с учётом `--account`, а с `--repo` тоже, хотя обнаружения тогда нет. Исключённые записи не ищутся. Поиск по ID для такой записи не выполняется, поэтому переименование (FR-2.5) из-за неё не возникает.
>
> - Найденный ID записывается в `backup-config.json` без вопроса, вместе с другими изменениями обнаружения, одной атомарной записью (FR-2.8). Заполнение ID не считается событием обнаружения и само по себе не даёт код `3`.
> - Если GitHub вернул 404 или 403, элемент получает статус `Failed` с причиной «репозиторий не найден на GitHub». Запись остаётся без ID, `status: "Unavailable"` не выставляется, остальные репозитории обрабатываются (FR-9.9).
> - Если найденный ID уже есть у другой записи настроек, ID не записывается, и элемент получает `Failed` с причиной, которая называет ту запись.
> - Другой сбой API — критическая ошибка (FR-9.10).
> - В проверочном прогоне ID записывается по правилам FR-9.5.
> - UI-приложение записывает найденный ID без вопроса, в том числе в проверочном прогоне.

**FR-9.5**: в третьем пункте дописать «кроме ID записей без ID, см. ниже». Затем добавить пункт:

> - запись без ID (FR-2.10) проверяется всегда: ID ищется по имени, и репозиторий показывается в таблице как обычно. В интерактивной консоли для каждой такой записи задаётся вопрос `ID is missing for <owner>/<repo>; found <id>. Save it to backup-config.json?`. Ответ «да» записывает ID в `backup-config.json` (FR-2.8). Ответ «нет» оставляет ID только в памяти до конца прогона, файл не меняется. С `--silent` или при перенаправленном вводе ID записывается без вопроса. Кроме ID, прогон в настройках ничего не меняет; если ID записан, строка над таблицей об этом сообщает.

**§7.3**: заменить пункт о дублях и добавить пункты:

> - `id`, если задан, — положительное число; отсутствие `id`, `null` или `0` означает запись без ID (FR-2.10); отрицательный `id` — ошибка;
> - нет дублей репозиториев: по ID среди записей с ID и по имени среди всех записей;
> - проверка (`validate` и загрузка) к GitHub не обращается и ID не ищет.

**§9**: между шагами 4 и 5 добавить «Поиск ID для записей без ID (FR-2.10), для `--dry-run` — вопросы и запись по FR-9.5».

**История:**

> | 0.22 | 2026-11-13 | FR-1.7: `id` записи можно не указывать; новое FR-2.10: поиск ID по имени при запуске, запись ID в настройки без вопроса, ненайденная запись — `Failed` без `Unavailable`, конфликт ID — `Failed`; FR-9.5: исключение для записи без ID в проверочном прогоне; §7.3: `id` необязателен, отрицательный — ошибка, дубли по ID только среди записей с ID; §9: шаг поиска ID |

## 4. Затрагиваемые проекты и типы

**GitHubBackup.Core**

- `BackupConfiguration/AccountRepositoryConfig`, `RepositoryConfig` — тип `long Id` остаётся; в XML doc указать, что `0` означает «ID неизвестен».
- `BackupConfiguration/BackupConfigValidator.CheckRepositoryFields` — `0` допустим и не участвует в проверке дублей; отрицательное значение — ошибка `"{location}.id must be a positive number when set."`.
- `BackupConfiguration/BackupConfigEntries` (новый, static) — находит запись по `(accountLogin, owner, name)` и ставит ей `Id`.
- `Discovery/ResolvedRepositoryId` (новый record) — `Owner`, `Name`, `AccountLogin`, `GitHubRepository Repository`, плюс `FullName`.
- `Discovery/RepositoryIdResolution` (новый record) — списки `Resolved`, `NotFound` и `Conflicts` (полное имя и причина).
- `Discovery/IRepositoryDiscoveryService` и `RepositoryDiscoveryService` — новый метод `ResolveIdsAsync(BackupConfig config, IReadOnlyList<BackupWorkItem> items, CancellationToken)`. В `DiscoverAsync` записи с `Id == 0` пропускаются: они не попадают в `knownIds`, для них нет `FindAsync`, они не становятся `Missing` или `Renamed`. Собранный репозиторий с именем такой записи не считается новым.
- `Discovery/RepositoryDiscoveryServiceLog` — события: ID найден (Information), не найден (Warning), конфликт (Warning).
- `Discovery/IDiscoveryDecisionHandler` — новый метод `Task<bool> ConfirmIdSaveAsync(RepositoryIdProposal proposal, CancellationToken)`; вызывается только в dry-run.
- `Discovery/RepositoryIdProposal` (новый record) — `FullName`, `Id`.
- `Discovery/AutomaticDiscoveryDecisionHandler` — `ConfirmIdSaveAsync` возвращает `true`.
- `Runs/BackupOrchestrator.RunLockedAsync`, `ProcessAllAsync` — поиск, вопросы, сохранение, `Failed` для ненайденных и конфликтных записей; результаты поиска добавляются в `discovered` для проверки wiki.
- `Runs/BackupRunResult` — новое свойство `SavedRepositoryIds` (полные имена).
- `Runs/BackupRunSummary` — строка над таблицей dry-run с записанными ID (константа рядом с `DryRunHeader`).

**GitHubBackup.Infrastructure**

- `BackupConfiguration/BackupConfigJsonContext.CreateBackupConfigTypeInfo` — `TypeInfoResolver = Default.WithAddedModifier(...)`: у `AccountRepositoryConfig` и `RepositoryConfig` свойство `id` не пишется, когда оно `0`.

**GitHubBackup.Cli**

- `ConsoleDiscoveryDecisionHandler.ConfirmIdSaveAsync` — `ConfirmationPrompt` с текстом из FR-9.5, по умолчанию «да». При `console.IsInputRedirected` возвращает `true` без вывода.
- `BackupSummaryRenderer` — выводит новую строку над таблицей (через `BackupRunSummary`).
- `BackupCommandHandler` — обновить только `<remarks>`; логика не меняется.

**GitHubBackup.App**

- `Discovery/DeferredDiscoveryDecisionHandler.ConfirmIdSaveAsync` — возвращает `true`.
- `ViewModels/RunSummaryViewModel` — строка над таблицей из `BackupRunSummary`.

**Документы:** `docs/PRD.md`, `README.md`.

## 5. Подход

1. **PRD** — текст раздела 3. Код начинается только после одобрения.
2. **Модель и JSON.** `long Id`, где `0` значит «нет ID». Отсутствующее свойство и так читается как `0`. `"id": null` читается как `0` конвертером JSON в Infrastructure (решение 1).
   - Почему не `long?`: `Id` используется как ключ в state store, `BackupWorkItem`, `DiscoveryResult.Repositories` и в applier. С `long?` пришлось бы менять десятки мест и всё равно отдельно обрабатывать `0`.
   - Атрибут `JsonIgnore` в Core не ставим: слой Core не должен знать о сериализации. Поэтому `id` не пишется при `0` через модификатор в контексте Infrastructure. Так запись без ID сохраняется без свойства и файл проходит round-trip.
3. **Валидатор** — отрицательный ID даёт ошибку, `0` пропускается в `_ids`. Проверка дублей по имени не меняется.
4. **`ResolveIdsAsync`** в `RepositoryDiscoveryService` (issue: «обнаружение находит запись без id по имени»; отдельный интерфейс не нужен).
   - Для каждого элемента с `Id == 0` вызывается `gitHubApi.GetRepositoryAsync(owner, name)`.
   - `GitHubNotFoundException` и `GitHubAccessDeniedException` → `NotFound`. Остальные `GitHubApiException` пробрасываются.
   - Если ID уже есть у другой записи конфигурации или у ранее найденной записи этого прохода → `Conflicts`.
   - `GetRepositoryByIdAsync` не вызывается никогда.
   - Метод не меняет `config`.
5. **`DiscoverAsync`** — записи без ID пропускаются, как описано в разделе 4. Это страховка для записей вне области `--account` и для ненайденных записей.
6. **Оркестратор.** Новый шаг в `RunLockedAsync`, внутри того же `try`, что и обнаружение, поэтому сбой API и `IOException` при сохранении уже дают критическую ошибку:
   - область поиска: `ExcludeMatching(config, SelectIncluded(items, filter))`, затем элементы с `Id == 0`;
   - `resolution = await discoveryService.ResolveIdsAsync(...)`;
   - **dry-run:** для каждого найденного вызывается `request.DecisionHandler.ConfirmIdSaveAsync`. Подтверждённые ID ставятся в `config`, и сразу `configStore.SaveAsync(config)`. В dry-run `config` больше ничем не меняется, поэтому записываются только ID. Потом остальные ID ставятся только в память;
   - **обычный запуск:** все ID ставятся в `config` без вопроса. После `decisionApplier.ApplyAsync` вызывается `SaveAsync`, только если есть найденные ID и `applied?.ConfigSaved != true`. Так запись одна, и `.bak` = исходный файл;
   - `items = BackupWorkItem.CreateAll(config, pathResolver)` пересоздаётся после того, как ID поставлены;
   - `ProcessAllAsync` получает набор полных имён из `NotFound` и `Conflicts` (`OrdinalIgnoreCase`). Для таких элементов добавляется `Failed` с сообщением (`RepositoryNotFoundMessage = "The repository was not found on GitHub."`, сообщение конфликта называет вторую запись), wiki не обрабатывается. Так сохраняется порядок, а отмена работает как раньше;
   - найденные `GitHubRepository` добавляются в словарь `discovered`, чтобы при `--repo` не было повторного запроса для wiki;
   - `BackupRunResult.SavedRepositoryIds` заполняется в обоих режимах.
7. **Код возврата** считается без изменений: найденные ID не попадают в `DiscoveryResult` и `DiscoveryApplyResult`, поэтому `3` не появляется. `Failed` даёт `1` по FR-9.10.
8. **Хосты** — CLI-вопрос, `true` при перенаправленном вводе, App без вопросов.
9. **README.**

Решения и почему:

- **Порядок «поиск → обнаружение».** Обнаружение работает уже с ID. Найденная запись при повторном поиске по имени даёт совпадающий ID, поэтому переименования нет.
- **Повторный запрос.** У отдельной чужой записи, которой нет в списках, в первом запуске будет второй `GET /repos/{owner}/{repo}` из `FindAsync`. Это сознательно: сигнатура `DiscoverAsync` не меняется, а следующие запуски стоят столько же, сколько сейчас.
- **Редирект после переименования.** `GET /repos/{старое имя}` на GitHub ведёт на новое имя. Тогда ID берётся, а обнаружение показывает настоящее переименование (FR-2.5). Это правильное поведение, а не ложное.

## 6. Тесты

**GitHubBackup.Core.Tests**

- `BackupConfiguration/BackupConfigValidatorTests`:
  - `Validate_IdMissingOrZero_IsValid` — theory: отдельная и в аккаунте, `0`;
  - `Validate_NegativeId_ReportsError` — theory: оба вида записей;
  - `Validate_TwoEntriesWithoutId_NoDuplicateError`;
  - `Validate_SameNameOneWithoutId_ReportsDuplicateName`;
  - существующий тест на дубли положительных ID остаётся.
- `Discovery/RepositoryDiscoveryServiceTests` (NSubstitute `IGitHubApi`):
  - `ResolveIdsAsync_StandaloneWithoutId_ReturnsIdFromName`;
  - `…_AccountEntryWithoutId_…`;
  - `…_NotFound_ReturnsNotFound` — theory: 404 и 403;
  - `…_RateLimit_Throws`;
  - `…_NeverLooksUpById` — `DidNotReceive().GetRepositoryByIdAsync`;
  - `…_EntryWithId_NotLookedUp`;
  - `…_IdOfOtherEntry_ReturnsConflict`;
  - `…_TwoEntriesSameId_SecondConflicts`;
  - `…_Cancelled_Throws`;
  - `DiscoverAsync_EntryWithoutId_NotMissingNotRenamedNoLookup`;
  - `DiscoverAsync_CollectedRepositoryNamedLikeEntryWithoutId_NotNew`;
  - `DiscoverAsync_ResolvedEntryFoundByName_NoRename`.
- `Runs/BackupOrchestratorTests` (подменённые сервисы):
  - `Run_EntryWithoutId_FillsIdSavesOnceAndBacksUp`;
  - `Run_IdsAndDiscoveryChanges_SavedOnceByApplier`;
  - `Run_OnlyIdFilled_ExitCodeSuccess`;
  - `Run_NotFound_FailedOthersProcessedNoUnavailable`;
  - `Run_Conflict_Failed`;
  - `Run_InteractiveNotDryRun_DoesNotAskConfirm`;
  - `Run_RepoFilterEntryWithoutId_ResolvedWithoutDiscovery`;
  - `Run_AccountFilter_ResolvesOnlyThatAccount`;
  - `Run_ExcludedEntryWithoutId_NotResolved`;
  - `Run_ResolveApiFailure_Critical`;
  - `Run_CancelledDuringResolve_Cancelled`;
  - `DryRun_ConfirmYes_SavesOnlyIds`;
  - `DryRun_ConfirmNo_NoSaveItemChecked`;
  - `DryRun_SavedIds_ReportedInResult`.
- `Runs/BackupRunResultTests` — найденные ID не влияют на `ExitCode`.
- `Runs/BackupRunSummaryTests` — строка над таблицей с записанными ID.
- `Discovery/AutomaticDiscoveryDecisionHandlerTests` — `ConfirmIdSaveAsync` возвращает `true`.

**GitHubBackup.Infrastructure.Tests**

- `BackupConfiguration/JsonBackupConfigStoreTests`:
  - загрузка записи без `id` и с `"id": null` проходит, `Id == 0` (оба вида записей);
  - отрицательный `id` даёт ошибку с новым текстом (обновить существующий ожидаемый текст).
- `JsonBackupConfigStoreSaveTests` — `Id == 0` сохраняется без `id`, ненулевой ID пишется; ожидаемый JSON — raw string.

**GitHubBackup.Infrastructure.IntegrationTests**

- `Runs/BackupOrchestratorTests`: реальный `JsonBackupConfigStore`, `FakeGitHubApi`, `LocalGitSandbox`.
  - Отдельная запись без ID: ID в файле, есть `.bak`, есть архив.
  - Dry-run с подменённым обработчиком: «да» — файл отличается только `id`; «нет» — файл не изменился, в результатах `WouldClone`.
  - При необходимости `FakeGitHubApi` получает метод, который отдаёт репозиторий только по имени.

**GitHubBackup.Cli.Tests**

- `ConsoleDiscoveryDecisionHandlerTests` (`TestCliConsole`):
  - текст вопроса;
  - «да» → `true`, «нет» → `false`;
  - перенаправленный ввод → `true` без вывода и без чтения ввода.
- `CliEndToEndTests` (`GitHubApiStub`, `LocalGitSandbox`, `CliInstallation`, процесс с перенаправленным вводом):
  - `Backup_SilentEntryWithoutId_WritesIdArchivesExitsSuccess`;
  - `Backup_SilentEntryWithoutIdNotOnGitHub_FailedWithoutIdOthersArchived`;
  - `Backup_DryRunRedirectedEntryWithoutId_WritesOnlyId`;
  - `Backup_DryRunSilentEntryWithoutId_WritesOnlyId`;
  - `Validate_EntryWithoutId_ExitsSuccess`.
- `BackupSummaryRendererTests` — строка dry-run с записанными ID.

**GitHubBackup.App.Tests**

- `Discovery/DeferredDiscoveryDecisionHandlerTests` — `ConfirmIdSaveAsync` возвращает `true`.

## 7. Проверка

| Критерий | Чем проверяется |
|---|---|
| PRD | ревью diff `docs/PRD.md`: версия 0.22, дата, строка истории, FR-1.7, FR-2.10, FR-9.5, §7.3, §9 |
| Валидация | `BackupConfigValidatorTests` |
| Поиск, запись, бэкап, код не `3` | `Run_EntryWithoutId_*`, `Run_OnlyIdFilled_ExitCodeSuccess`, интеграционный тест, `Backup_SilentEntryWithoutId_*` |
| Нет поиска по ID и переименования | `ResolveIdsAsync_NeverLooksUpById`, `DiscoverAsync_ResolvedEntryFoundByName_NoRename` |
| Ненайденная запись | `Run_NotFound_*`, `Backup_SilentEntryWithoutIdNotOnGitHub_*` |
| Интерактивный dry-run да/нет | `DryRun_ConfirmYes_*`, `DryRun_ConfirmNo_*`, интеграционные тесты, `ConsoleDiscoveryDecisionHandlerTests` |
| `--dry-run --silent` и перенаправленный ввод | `Backup_DryRunSilent*`, `Backup_DryRunRedirected*` |
| Обычный интерактивный `backup` без вопроса | `Run_InteractiveNotDryRun_DoesNotAskConfirm` |
| README | ревью diff |
| Сборка и CI | build, test, `dotnet format --verify-no-changes`, CI pull request |

## 8. Вне рамок

- Обзор репозиториев в WPF (`RepositoryOverviewProvider`). Запись без ID читает `.backup-state.json` с `Id == 0`. Если файл состояния уже есть (пользователь удалил `id` у старой записи), обзор покажет ошибку состояния до следующего запуска, который найдёт ID. Новых записей это не касается.
- Записанные ID не попадают в историю как события обнаружения и в список `New/Missing/Renamed`. Они есть в логе и, для dry-run, в строке над таблицей.
- Отдельный вопрос о записи ID в UI-приложении не делаем: оно записывает без вопроса.

## 9. Открытые вопросы

Нет: все вопросы решены на ревью (раздел 10).

## 10. Решения

Приняты на ревью плана 2026-11-13:

1. `"id": null` принимается как «нет ID», так же как отсутствие `id` и `0`. В Infrastructure добавляется конвертер JSON, который читает `null` как `0`; при сохранении запись без ID пишется без свойства `id`. §7.3 PRD упоминает `null` вместе с `0` и отсутствием `id`; тесты загрузки покрывают `null`.
2. Конфликт ID (найденный ID уже есть у другой записи) → `Failed` с причиной, которая называет ту запись; ID не записывается.
3. Запись без ID со `status: "Unavailable"` тоже ищется: найдена — обнаружение возвращает её в `Active` (FR-2.4), не найдена — `Failed`.
4. UI-приложение записывает найденный ID без вопроса, в том числе в проверочном прогоне.
5. Строка над таблицей dry-run, когда ID записаны: `Dry run: nothing was cloned, updated or archived; only the missing repository ids were saved to backup-config.json.`

## 11. Задачи

- [x] PRD 0.22: FR-1.7, FR-2.10, FR-9.5, §7.3, §9, строка истории
- [x] `BackupConfigValidator`: `0` допустим, отрицательный — ошибка, дубли по ID без `0`; тесты валидатора и `JsonBackupConfigStoreTests`
- [x] `BackupConfigJsonContext`: читать `"id": null` как `0`, не писать `id`, когда он `0`; тесты загрузки и сохранения
- [x] `ResolvedRepositoryId`, `RepositoryIdResolution`, `RepositoryIdProposal`, `BackupConfigEntries`
- [x] `IRepositoryDiscoveryService.ResolveIdsAsync` и реализация (404/403, конфликт, без поиска по ID), события лога; тесты
- [x] `DiscoverAsync`: пропуск записей без ID, собранный репозиторий с именем такой записи не новый; тесты
- [x] `IDiscoveryDecisionHandler.ConfirmIdSaveAsync`: Automatic, Console (вопрос и перенаправленный ввод), Deferred (App); тесты
- [x] `BackupOrchestrator`: поиск, dry-run вопросы и сохранение, одно сохранение в обычном запуске, `Failed` для ненайденных и конфликтных, пересборка `items`, `discovered`; тесты оркестратора
- [x] `BackupRunResult.SavedRepositoryIds`, `BackupRunSummary`, `BackupSummaryRenderer`, `RunSummaryViewModel`: строка dry-run; тесты
- [x] Интеграционные тесты оркестратора (реальное хранилище, `FakeGitHubApi`)
- [x] CLI end-to-end: `--silent`, не найдено, `--dry-run` с перенаправленным вводом, `--dry-run --silent`, `validate`
- [x] README: `id` для ручных записей можно не указывать, произвольный ID указывать нельзя; поведение dry-run и `Failed`
- [x] Сборка, тесты, `dotnet format --verify-no-changes`; проверка комментариев в diff
