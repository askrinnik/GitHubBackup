# План: F2.10 — обнаружение в области запуска для одного репозитория

- Задача: [#149](https://github.com/askrinnik/GitHubBackup/issues/149) «F2.10: Scoped discovery for a single-repository run»
- Направление: Feature
- Сложность: M
- Дата: 2026-11-17

## 1. Цель

Сейчас запуск с `--repo` пропускает обнаружение: `BackupOrchestrator.DiscoverAsync` возвращает `DiscoveryResult.Empty`. Нужна точечная проверка выбранной записи с ID (FR-2.7, FR-2.4, FR-2.5, FR-2.10, FR-9.5, FR-9.10, §6.9, §9 шаг 6).

Проверка идёт так:

- Программа запрашивает `GET /repositories/{id}`. Имя совпало — изменений нет; `Unavailable` возвращается в `Active`. Имя другое — это переименование по FR-2.5.
- При 404/403 программа запрашивает `GET /repos/{owner}/{repo}`. Снова 404/403 — запись пропала: `Unavailable`, ничего не удаляется, элементов `Failed` нет.
- Другой сбой API — критическая ошибка, код `2`.

Решения идут тем же путём, что при обнаружении: `IDiscoveryDecisionApplier` и `IDiscoveryDecisionHandler`.

## 2. Критерии приёмки

Из задачи, дословно:

- PRD обновлён по разделу «Изменение PRD»: версия, строка истории, FR-2.7, FR-2.10, §6.9, §9
- Тест: `--repo` для записи, которой нет на GitHub (404 по ID и по имени), ставит `Unavailable`, ничего не удаляет и не даёт элементов `Failed` для репозитория и wiki; код возврата `3`
- Тест: `--repo` для записи со статусом `Unavailable`, которую GitHub снова отдаёт, возвращает `Active` и бэкапит её в этом же запуске
- Тест: `--repo` для записи, которую GitHub отдаёт под другим именем, обрабатывается как переименование (FR-2.5)
- Тест: `--repo` для записи, найденной под тем же именем, бэкапится как раньше, `backup-config.json` не меняется
- Тест: `--repo --dry-run` показывает пропавшую запись и не меняет `backup-config.json`
- Тест: `--repo` для записи без ID не выполняет точечную проверку (FR-2.10 без изменений)
- Тест: сбой API при точечной проверке — критическая ошибка с кодом `2`
- UI: «Backup repository» для пропавшего репозитория показывает строку `Missing` в окне обнаружения, после запуска дерево показывает `Unavailable`
- Покрыто тестами, CI зелёный

Добавлено проверкой покрытия:

- (добавлено) 403 обрабатывается так же, как 404, и по ID, и по имени.
- (добавлено) ID дал 404/403, а по имени найден тот же ID — изменений нет.
- (добавлено) ID дал 404/403, а имя занято репозиторием с другим ID — запись пропала (правило `RepositoryDiscoveryService.FindAsync`, FR-2.4).
- (добавлено) Запись `Unavailable`, которой всё ещё нет на GitHub, — событий нет; элемент `Skipped`, кода `3` нет.
- (добавлено) Исключённая запись с `--repo` не проверяется: ни одного запроса, элементов нет.
- (добавлено) Переименование в недопустимое имя (FR-2.9) не предлагается: запись бэкапится под прежним именем.
- (добавлено) После переименования в этом же запуске бэкапится запись под новым именем. Сейчас `BackupRunFilter.Includes` сравнивает по старому имени и потерял бы её.
- (добавлено) Запись аккаунта и отдельная запись обрабатываются одинаково.
- (добавлено) `--dry-run` для записи `Unavailable`, которую GitHub отдаёт, обрабатывает её как активную (`ReactivateReturned`, как при полном обнаружении).

## 3. Изменение требований

Новой формулировки нет. Текст из раздела задачи «Изменение PRD» переносится дословно: FR-2.7 (новый полный текст), FR-2.10, строка `--repo` в §6.9, §9 шаг 6. Версия `0.45` → `0.46`, строка в истории изменений.

## 4. Затрагиваемые проекты и типы

**docs**

- `docs/PRD.md` — перенос одобренного текста, версия, строка истории.
- `README.md` — таблица команд (`--repo` проверяет запись на GitHub), раздел про `backup` (точечная проверка), телеметрия (`backup.discovery` есть и при `--repo`).

**GitHubBackup.Core**

- `Discovery/IRepositoryDiscoveryService` — новый метод `Task<DiscoveryResult> CheckRepositoryAsync(BackupConfig config, BackupWorkItem item, CancellationToken cancellationToken)`. Он проверяет одну запись с ID и возвращает `Missing`/`Returned`/`Renamed`, а в `Repositories` — найденный репозиторий. Новых репозиториев нет.
- `Discovery/RepositoryDiscoveryService`:
  - реализация `CheckRepositoryAsync`;
  - классификация одной записи (`missing`/`returned`/`renamed`, проверка `HasValidName`) выносится из цикла `DiscoverAsync` в общий private-метод, чтобы правила не разошлись;
  - private-метод поиска: сначала по ID, потом по имени.
- `Discovery/RepositoryDiscoveryServiceLog` — запись лога о результате точечной проверки.
- `Runs/BackupOrchestrator`:
  - `DiscoverAsync` при `filter.IsRepository` вызывает `CheckRepositoryAsync` для выбранной записи, если у неё был ID до разрешения ID и маски её не исключают; иначе возвращает `Empty`;
  - выбор работы после применения решений идёт по ID проверенной записи;
  - спан `backup.discovery` открывается для точечной проверки.
- `Runs/BackupOrchestratorLog` — текст `DiscoverySkipped` уточняется (или отдельное событие для случая без точечной проверки).

**GitHubBackup.TestUtilities**

- `GitHubApiStub`: `GivenRepositoryUnavailable(long id, string owner, string name, int statusCode = 404)` (404/403 на `/repositories/{id}` и `/repos/{owner}/{name}` с приоритетом выше сценарного) и `GivenServerError(string path)` (ответ 500).

Без изменений: `Infrastructure` (`GetRepositoryByIdAsync` есть; 404 → `GitHubNotFoundException`, 403 → `GitHubAccessDeniedException`), `Cli` (обработчики решений и сводка уже работают с `Missing`/`Renamed`/`Returned`), `App` (дерево перезагружается после запуска, окно обнаружения показывает строки `Missing`).

## 5. Подход

1. **PRD.** Перенести текст из задачи, поднять версию, добавить строку истории.
2. **`RepositoryDiscoveryService.CheckRepositoryAsync`.**
   - Запись находится через `ParseAccounts(config, out tracked)` по `item.Id`: так `TrackedRepository` получает статус и `AccountLogin` из конфигурации.
   - `item.Id <= 0` → `ArgumentOutOfRangeException` (оркестратор такой вызов не делает).
   - Поиск: `TryGetAsync(GetRepositoryByIdAsync)`; если `null` — `TryGetAsync(GetRepositoryAsync(owner, name))`, результат принимается только при `byName.Id == item.Id`.
   - Другие `GitHubApiException` не перехватываются.
   - Классификация — общим методом из `DiscoverAsync`.
   - `Repositories` содержит найденный репозиторий: wiki берёт `has_wiki` без второго запроса.
   - Порядок «ID, потом имя» отличается от `FindAsync` («имя, потом ID»), потому что так требует FR-2.7; `FindAsync` не переиспользуется.
3. **`BackupOrchestrator.RunLockedAsync`.**
   - До разрешения ID запомнить выбранную запись (`items.FirstOrDefault(filter.Includes)`); `BackupConfigValidator.CheckDuplicateName` гарантирует, что она одна.
   - Точечная проверка нужна, если `Id != 0` и запись не исключена. Запись, получившая ID в этом запуске, не проверяется (FR-2.10).
   - Результат идёт по существующему пути: обычный запуск — `decisionApplier.ApplyAsync` с обработчиком режима, сохранение, пересоздание `items`; dry-run — `PlannedDiscovery`, `ReactivateReturned`, `SkipMissing`.
   - Выбор работы по `Id` проверенной записи, чтобы переименованная запись бэкапилась в этом же запуске; иначе `filter.Includes`, как сейчас.
   - Пропавшая запись после применения неактивна: `Skipped`, wiki нет, элементов `Failed` нет; код `3` из `DiscoveryApplyResult`.
   - Сбой API ловит существующий `catch (GitHubApiException …)` → код `2`.
4. **Тестовые помощники**, затем тесты.
5. **README.**

Почему так: обнаружение и решения переиспользуются целиком, поэтому UI, CLI, `--silent`, история и коды возврата работают без новой логики в хостах. Отбор по ID закрывает единственное место, где старое имя ломало бы переименование.

## 6. Тесты

**`Core.Tests/Discovery/RepositoryDiscoveryServiceTests`** (NSubstitute `IGitHubApi`):

- `CheckRepositoryAsync_FoundByIdSameName_NoChangesAndRepositoryReturned` (`GetRepositoryAsync` не вызывается)
- `CheckRepositoryAsync_FoundByIdOtherName_IsRenamed` (Theory: отдельная запись и запись аккаунта)
- `CheckRepositoryAsync_FoundByIdInvalidName_IsNeitherRenamedNorMissing`
- `CheckRepositoryAsync_UnavailableFoundById_IsReturned`; `…_UnavailableFoundUnderOtherName_IsReturnedAndRenamed`
- `CheckRepositoryAsync_IdAndNameNotFoundOrDenied_IsMissing` (Theory 404/403 × 404/403)
- `CheckRepositoryAsync_IdNotFoundNameSameId_NoChanges`
- `CheckRepositoryAsync_IdNotFoundNameOtherId_IsMissing`
- `CheckRepositoryAsync_UnavailableNotFound_NoChanges`
- `CheckRepositoryAsync_ByIdApiFailure_Throws`; `…_ByNameApiFailure_Throws`
- `CheckRepositoryAsync_EntryWithoutId_ThrowsArgumentOutOfRange`

**`Core.Tests/Runs/BackupOrchestratorTests`.** По умолчанию `CheckRepositoryAsync` возвращает `DiscoveryResult.Empty`.

Изменить: тесты `RunAsync_RepositoryFilter_*`, `RunAsync_DryRunRepositoryFilter_SkipsDiscovery`, `RunAsync_RepositoryFilter_TracesNoDiscovery`, `Run_RepoFilterEntryWithoutId_ResolvedWithoutDiscovery` и `RunAsync_RepositoryFilterMatchesExclusion_ProcessesNothing` (оба — `CheckRepositoryAsync` DidNotReceive).

Новые: `RunAsync_RepositoryFilterMissing_AppliesAndSkipsWithoutFailed`, `…Returned_ProcessesReactivatedEntry`, `…Renamed_ProcessesEntryUnderNewName`, `…CheckFails_ReturnsCritical`, `RunAsync_DryRunRepositoryFilterMissing_PlansWithoutApplyOrSave`, `RunAsync_DryRunRepositoryFilterReturned_ProcessesAsActive`.

**`Cli.Tests/CliEndToEndTests`** (`GitHubApiStub` + `LocalGitSandbox`):

- `Backup_SilentRepoFilterRepositoryGone_MarksUnavailableKeepsFilesExitsDiscoveryEvents` (Theory 404/403): код `3`, `Unavailable`, архив и клон на месте, нет `Failed`.
- `Backup_SilentRepoFilterUnavailableReturned_ReactivatesAndArchives`: код `0`, `Active`, архив.
- `Backup_SilentRepoFilterRenamed_UpdatesNameAndArchives`: код `3`, новое имя в конфигурации, архив.
- `Backup_SilentRepoFilter_BacksUpOnlyThatRepository` расширить: байты `backup-config.json` не изменились.
- `Backup_DryRunRepoFilterRepositoryGone_ShowsMissingAndKeepsConfig`: конфигурация не изменилась, пропавшая запись в выводе, нет `Failed`, код `3`.
- `Backup_SilentRepoFilterApiFailure_ExitsCritical`: код `2`, архива нет.
- `Backup_SilentRepoFilterEntryWithoutId_…`: код `0`, ID записан.

**`App.UITests`** — новый класс `RepositoryBackupDiscoveryTests` по образцу `DiscoveryDialogTests`: запись id 1, API отвечает 404; «Backup repository» → окно обнаружения со строкой `Missing` → применить → `Failed: 0`, узел дерева `Unavailable`.

## 7. Проверка

| Пункт | Как проверяется |
|---|---|
| PRD | Ревью диффа `docs/PRD.md` против текста задачи |
| Пропавшая запись, код 3 | `Backup_SilentRepoFilterRepositoryGone_…`; `RunAsync_RepositoryFilterMissing_…`; `CheckRepositoryAsync_IdAndNameNotFoundOrDenied_IsMissing` |
| Unavailable → Active и бэкап | `Backup_SilentRepoFilterUnavailableReturned_…`; `RunAsync_RepositoryFilterReturned_…` |
| Переименование | `Backup_SilentRepoFilterRenamed_…`; `RunAsync_RepositoryFilterRenamed_…` |
| То же имя, конфигурация не меняется | `Backup_SilentRepoFilter_BacksUpOnlyThatRepository` |
| `--dry-run` | `Backup_DryRunRepoFilterRepositoryGone_…`; `RunAsync_DryRunRepositoryFilterMissing_…` |
| Запись без ID | `Run_RepoFilterEntryWithoutId_ResolvedWithoutDiscovery` |
| Сбой API, код 2 | `Backup_SilentRepoFilterApiFailure_ExitsCritical`; `RunAsync_RepositoryFilterCheckFails_ReturnsCritical` |
| UI | `RepositoryBackupDiscoveryTests` (UI-тесты с `-p:RunUITests=true`) |
| CI | сборка, тесты, `dotnet format --verify-no-changes`, CI пулл-реквеста |

## 8. Вне рамок

- Поиск новых репозиториев при `--repo`.
- `--repo` с именем, которого нет в конфигурации, по-прежнему даёт критическую ошибку.
- Переименование в имя, которое уже есть у другой записи конфигурации (полное обнаружение такой случай тоже не проверяет).
- Кнопка «Backup repository» для узла `Unavailable` в UI: сейчас она недоступна, FR-12 этого не меняет. Возврат через `--repo` покрывает CLI.
- Стадия прогресса `Discovering` для точечной проверки.

## 9. Решения

- ID дал 404/403, а по имени найден репозиторий с другим ID — запись пропала. Так требует FR-2.4, на который ссылается одобренный FR-2.7.
- Спан `backup.discovery` открывается для точечной проверки; README (телеметрия) обновляется.
- Пользователь разрешил перейти к реализации без отдельного ревью плана, если по планированию нет вопросов.

## 10. Задачи

- [x] Перенести одобренный текст в `docs/PRD.md` (FR-2.7, FR-2.10, §6.9, §9 шаг 6), версия 0.46, строка истории
- [x] Вынести классификацию одной записи из `RepositoryDiscoveryService.DiscoverAsync` в общий метод
- [x] Добавить `IRepositoryDiscoveryService.CheckRepositoryAsync` и реализацию: ID, потом имя, 404/403 → `null`, лог
- [x] `BackupOrchestrator`: точечная проверка при `--repo`, выбор работы по ID, спан `backup.discovery`, лог
- [x] Тесты `RepositoryDiscoveryServiceTests` для `CheckRepositoryAsync`
- [x] Тесты `BackupOrchestratorTests` для `--repo`
- [x] `GitHubApiStub`: `GivenRepositoryUnavailable`, `GivenServerError`
- [x] E2E-тесты в `CliEndToEndTests`
- [x] UI-тест `RepositoryBackupDiscoveryTests`
- [x] README: таблица команд, раздел про `backup`, телеметрия
- [x] Сборка, тесты, `dotnet format --verify-no-changes`
