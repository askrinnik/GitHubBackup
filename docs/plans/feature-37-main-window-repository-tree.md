# План: #37 F4.2 «Главное окно: дерево репозиториев и запуск бэкапа»

- Issue: [#37](https://github.com/askrinnik/GitHubBackup/issues/37) F4.2: Main window: repository tree and backup launch
- Lane: Feature
- Сложность: L
- Дата: 2026-11-13
- Статус: одобрен 2026-11-13

## 1. Цель

Главное окно WPF показывает дерево «аккаунты → репозитории» и группу отдельных репозиториев. У каждого репозитория видны статус и дата последнего архива. Кнопки «Backup all», «Backup account» и «Backup repository» запускают бэкап через тот же `IBackupOrchestrator`, что и CLI. Переключатель «Dry run» задаёт `BackupRunRequest.DryRun` (FR-12.1, FR-12.7). Перед каждым запуском UI перечитывает `backup-config.json` (NFR-SYS-2, §8.4).

## 2. Критерии приёмки

Из issue, дословно:
- Unit-тесты ViewModel
- Покрыто тестами, CI зелёный

Из PRD (в issue их нет):
- Дерево строится из `backup-config.json`. Узлы аккаунтов идут в порядке файла, аккаунт без репозиториев тоже виден. После них идёт группа «Individual repositories» (FR-12.1, §7.3).
- У каждого репозитория есть статус и дата последнего архива. Дата берётся из `.backup-state.json`, по аналогии с FR-8.5 (FR-12.1).
- Запуски идут с `Mode = RunMode.Ui`, и история записывает каждый из них (FR-8.2, FR-8.8).
- Перед каждым запуском `backup-config.json` перечитывается, и дерево обновляется (NFR-SYS-2).

Добавлено проверкой покрытия:
- Файл `backup-config.json` отсутствует или невалиден. Вместо дерева видна ошибка загрузки, окно не падает.
- В конфигурации нет ни аккаунтов, ни отдельных репозиториев. Видно сообщение о пустом списке.
- Есть только отдельные репозитории. Видна только группа, узлов аккаунтов нет.
- Нет ни одного архива (история пуста). Дата показывается как `Never`.
- `.backup-state.json` нечитаем. Дата показывается как `Unknown`, текст ошибки виден в подсказке, остальное дерево строится.
- Исключённый маской или `Unavailable` репозиторий показан с этим статусом. «Backup repository» для него недоступна.
- Пока идёт запуск, все кнопки запуска и переключатель Dry run выключены (`IsRunning`). Второй запуск из окна невозможен.
- Блокировку держит другой процесс, например CLI. Тогда `CriticalErrors` содержит `BackupOrchestrator.RunLockHeldMessage`, и UI показывает ошибку в диалоге.
- Запуск остановлен критической ошибкой (настройки, инструменты, токен). Ошибки видны в диалоге.
- Неожиданное исключение из ядра показывается в диалоге и не роняет приложение.
- После запуска дерево перечитывается, даты обновляются, выбор восстанавливается по ключу.
- Второй запуск в том же сеансе тоже записывается в историю, под новым `RunId` (FR-8.8, FR-10.3).

## 3. Изменения требований

Не нужны. Трактовка слова «статус» вынесена в *Открытые вопросы*.

## 4. Затронутые проекты и типы

**GitHubBackup.Core**
- `Overview/IRepositoryOverviewProvider` (новый): `Task<RepositoryOverview> LoadAsync(CancellationToken)`.
- `Overview/RepositoryOverviewProvider` (новый). Читает `IBackupConfigStore`, строит элементы через `BackupWorkItem.CreateAll` и `IRepositoryPathResolver`. Статус определяет через `IExclusionMatcher`, дату последнего архива берёт из `IBackupStateStore.LoadAsync(...).GetLastArchive(BackupTarget.Repository)`.
- `Overview/RepositoryOverview` (новый record): `Accounts` и `StandaloneRepositories`.
- `Overview/AccountOverview` (новый record): `Login`, `Url`, `Repositories`.
- `Overview/RepositoryOverviewEntry` (новый record): `Owner`, `Name`, `FullName`, `AccountLogin`, `Status`, `LastArchiveAt` (`DateTimeOffset?`), `StateError` (`string?`).
- `Overview/RepositoryOverviewStatus` (новый enum): `Active`, `Unavailable`, `Excluded`.
- `Runs/IRunContext` и `Runs/RunContext`: добавить `Guid BeginRun()`. Метод выдаёт новый `RunId`, и последующие записи лога и истории идут под ним.
- `CoreServiceCollectionExtensions`: регистрация `IRepositoryOverviewProvider`.

**GitHubBackup.Infrastructure**
- `Logging/RunIdEnricher`: читать `runContext.RunId` при каждом событии, а не один раз в конструкторе.

**GitHubBackup.App**
- `Runs/IBackupRunService` и `Runs/BackupRunService` (новые). Аналог `BackupCommandHandler`: вызывает `runContext.BeginRun()` и собирает `BackupRunRequest(filter, handler) { DryRun, Mode = RunMode.Ui }`. Затем вызывает `IBackupOrchestrator.RunAsync` через `Task.Run`, чтобы ядро не занимало UI-поток. В `Core` нет `ConfigureAwait(false)`, поэтому без `Task.Run` продолжения выполнялись бы в UI-потоке.
- `Discovery/DeferredDiscoveryDecisionHandler` (новый, временный до #39). Новый репозиторий получает `NewRepositoryDecision` с действием `Skip`: ничего не пишется, и обнаружение снова предложит его в следующем запуске. Переименованный получает `RenameDecision.KeepArchiveFolder`.
- `ViewModels/RepositoriesViewModel` (новый, вместо `HomeViewModel`). Содержит `Nodes`, `SelectedNode`, `IsDryRun`, `IsRunning`, `LoadError`, `IsEmpty`, `LastRunMessage`, а также `RefreshCommand` и `StartBackupCommand(BackupScope)` с `IncludeCancelCommand = true`.
- `ViewModels/TreeNodeViewModel` (базовый: `IsSelected`, `IsExpanded`), `AccountNodeViewModel`, `RepositoryGroupNodeViewModel`, `RepositoryNodeViewModel` (новые).
- `ViewModels/BackupScope` (новый enum): `All`, `Account`, `Repository`.
- `Views/RepositoriesView.xaml(.cs)` (новый, вместо `HomeView`). Содержит `TreeView` с `HierarchicalDataTemplate`, виртуализацией, тремя кнопками и `ToggleButton` Dry run. У каждого интерактивного элемента есть `AutomationId`.
- `App.xaml`: `DataTemplate` для `RepositoriesViewModel`.
- `MainWindowViewModel`: переход на `RepositoriesViewModel`.
- `AppServiceCollectionExtensions`: регистрация `IBackupRunService`, `IDiscoveryDecisionHandler` → `DeferredDiscoveryDecisionHandler`, `RepositoriesViewModel`.
- Удалить `HomeViewModel` и `HomeView`: заглушка «screens are not available yet» становится неверной.

**Harness**
- `.claude/rules/wpf-mvvm.md`: пример селектора `#HomeMessage` заменить на `#RepositoryTree`. Проверить `docs/ai/README.md` на упоминания `HomeMessage`.

## 5. Подход

1. **Core, `RunContext.BeginRun`.** Сейчас `RunContext` — синглтон с одним `Guid` на процесс. CLI выполняет один запуск на процесс, а UI выполняет много. Тогда `SqliteRunHistoryStore.AddAsync` второго запуска упадёт на дубле `RunId`, и запуск не попадёт в историю. CLI `BeginRun` не вызывает, поэтому его поведение не меняется. Затем поправить `RunIdEnricher`.
2. **Core, `RepositoryOverviewProvider`.** Порядок работы:
   - Загрузить конфигурацию. `BackupConfigException` пробрасывается. `ArgumentException` из `CreateAll` оборачивается в `BackupConfigException` с путём и текстом: оркестратор считает эту ошибку критической так же.
   - Взять узлы аккаунтов из `config.Accounts` через `GitHubUrl.TryParseAccount`, чтобы аккаунт без репозиториев тоже был виден.
   - Вычислить статус: `Excluded`, если `IExclusionMatcher.Match` не равен `null`; иначе `Unavailable`, если `!IsActive`; иначе `Active`.
   - Прочитать дату последнего архива из `IBackupStateStore`. `BackupStateException` ловится на уровне одного репозитория: `LastArchiveAt = null`, `StateError = message`.

   Всё это логика без WPF, поэтому по `csharp.md` («самый нижний слой») она живёт в `Core`.
3. **App, `BackupRunService` и `DeferredDiscoveryDecisionHandler`.** Регистрация в DI.
4. **App, ViewModel.** Шаги `StartBackupAsync(scope, ct)`:
   - (a) Взять `BackupRunFilter` из текущего выбора: `All`, `ForAccount(login)` или `ForRepository(owner, name)`.
   - (b) Перечитать дерево (NFR-SYS-2). Ошибка загрузки не блокирует запуск. Оркестратор сам перечитает конфигурацию, вернёт критическую ошибку и запишет запуск в историю (FR-8.8), как в CLI.
   - (c) Вызвать `IBackupRunService.RunAsync`.
   - (d) Обработать результат. Если есть `CriticalErrors`, вызвать `IDialogService.ShowError` со всеми ошибками. Иначе записать в `LastRunMessage` строку `"Backup finished with exit code {code}."` (`"Dry run finished …"` для dry run). `HistoryWarning` тоже идёт в диалог.
   - (e) `OperationCanceledException` проглатывается. Другие исключения уходят в `ShowError` с сообщением.
   - (f) В `finally` перечитать дерево и сбросить `IsRunning`.

   Условия `CanStartBackup(scope)`:
   - общее: `!IsRunning`;
   - `All`: всегда;
   - `Account`: выбран узел аккаунта или репозиторий внутри аккаунта;
   - `Repository`: выбран репозиторий со статусом `Active`.

   Выбор в `TreeView` связывается через `IsSelected` в `ItemContainerStyle`. `OnIsSelectedChanged` узла вызывает callback родителя, а `[NotifyCanExecuteChangedFor]` обновляет команду.
5. **App, View.** Состав окна:
   - `TreeView` с `VirtualizingPanel.IsVirtualizing="True"` и `VirtualizationMode="Recycling"`, без внешнего `ScrollViewer`;
   - дата отображается через `StringFormat` и `TargetNullValue='Never'`;
   - начальная загрузка вызывается из `Loaded` одной строкой в code-behind: `RefreshCommand.Execute(null)`.

   `AutomationId`: `RepositoryTree`, `BackupAllButton`, `BackupAccountButton`, `BackupRepositoryButton`, `DryRunToggle`, `TreeLoadError`, `EmptyTreeMessage`, `LastRunMessage`.
6. Обновить тесты хоста, правило `wpf-mvvm.md`, выполнить проверку в запущенном приложении.

## 6. Решения (спорные)

| Решение | Альтернатива и почему отклонена |
|---|---|
| Статус = статус в конфигурации (`Active`, `Unavailable`) плюс `Excluded` по маске. Дата = последний архив репозитория из `.backup-state.json`. | Статус последнего запуска из истории. У `IRunHistoryStore` нет запроса «последний элемент по репозиторию», понадобились бы новый EF-запрос, интеграционные тесты и, вероятно, уточнение PRD. Источник «состояние → дата» уже задан в FR-8.5. |
| Дата по разделу `Repository`, без wiki. | Максимум из repository и wiki: смешивает две независимые цели (FR-5). |
| Временный `DeferredDiscoveryDecisionHandler` (Skip / KeepArchiveFolder) до #39. | (1) `AutomaticDiscoveryDecisionHandler`: молча добавляет репозитории в конфигурацию без согласия пользователя. (2) Обработчик, который бросает `OperationCanceledException`: запуск с находками обнаружения обрывается и не записывается в историю. (3) Отказ от запуска, как FR-9.6 в CLI: тогда «Backup all» и «Backup account» не работали бы до #39. Skip ничего не меняет и ничего не теряет. Минус: история пометит решение как `DecisionMaker.User`, потому что `RunMode.Ui` ≠ `Silent`. |
| `IRunContext.BeginRun()`, вызов в `BackupRunService`. | Новый `RunId` в `RecordingBackupOrchestrator`: в CLI записи лога до запуска разойдутся с историей, а это ломает корреляцию, которую проверяет `BackupHistoryEndToEndTests`. Отдельный issue до #37: без этого исправления повторный запуск из UI сразу даёт предупреждение истории. |
| Ошибка перечитывания перед запуском не блокирует запуск. | Блокировать: запуск с критической ошибкой конфигурации не попал бы в историю, хотя FR-8.8 требует его записать, и UI разошёлся бы с CLI. |
| Одна команда `StartBackupCommand(BackupScope)` с параметром. | Три команды: три `…CancelCommand`, а #38 нужна одна кнопка отмены. |
| `Task.Run` в `BackupRunService`, а не в ViewModel. | `Task.Run` в ViewModel: тесты ViewModel стали бы зависеть от пула потоков. `IUiDispatcher` пока не нужен: прогресса нет, его добавит #38. |
| Начальная загрузка из `Loaded` одной строкой в code-behind. | Пакет `Microsoft.Xaml.Behaviors.Wpf` (`InvokeCommandAction`): новый пакет ради одного события. Загрузка в конструкторе ViewModel: ввод-вывод при разрешении из DI ломает `AppHostTests` и правило async. |
| Минимальная строка `LastRunMessage` плюс диалог для критических ошибок. | Ничего не показывать: у пользователя нет обратной связи. Полная сводка — это FR-12.4, её делает #38. |
| `RepositoriesViewModel` и `RepositoriesView` вместо `HomeViewModel`. | Переписать `HomeViewModel`: имя не отражает экран, и сменилась бы семантика `HomeMessage`. |

## 7. Тесты

**GitHubBackup.Core.Tests**
- `Overview/RepositoryOverviewProviderTests` (новый). Substitute для `IBackupConfigStore` и `IBackupStateStore`, реальные `RepositoryPathResolver` и `ExclusionMatcher`. Случаи:
  - `LoadAsync_AccountsAndStandalone_ReturnsAccountsInFileOrderThenStandalone`
  - `LoadAsync_AccountWithoutRepositories_ReturnsEmptyAccount`
  - `LoadAsync_OnlyStandaloneRepositories_ReturnsNoAccounts`
  - `LoadAsync_EmptyConfig_ReturnsEmptyOverview`
  - `LoadAsync_ConfigStoreThrows_PropagatesBackupConfigException`
  - `LoadAsync_RepositoryWithArchives_ReturnsCreatedAtOfLastRepositoryArchive`
  - `LoadAsync_NoStateFile_ReturnsNullLastArchive`
  - `LoadAsync_StateStoreThrows_ReturnsStateErrorAndKeepsOtherEntries`
  - `[Theory] LoadAsync_Status_ReflectsExclusionAndAvailability`
  - `LoadAsync_WikiArchiveOnly_IgnoresWikiSection`
- `Runs/RunContextTests`: `BeginRun_ReturnsNewIdAndUpdatesRunId`, `BeginRun_Twice_ReturnsDistinctIds`.
- `CoreServiceCollectionExtensionsTests`: регистрация `IRepositoryOverviewProvider`, если класс проверяет регистрации поштучно.

**GitHubBackup.Infrastructure.Tests**
- `Logging/RunIdEnricherTests`: `Enrich_AfterRunIdChanges_AddsCurrentRunId`.

**GitHubBackup.App.Tests**
- `ViewModels/RepositoriesViewModelTests` (новый): загрузка (`Refresh_*`), доступность команд (`CanStartBackup_BySelection_MatchesScope`, `StartBackup_WhileRunning_CannotExecuteAnyScope`), запуск (`StartBackup_Scope_PassesFilter`, `StartBackup_DryRunToggle_PassesDryRun`, `StartBackup_RereadsConfigBeforeRun`, `StartBackup_RereadFails_StillStartsRun`, `StartBackup_CriticalErrors_ShowsErrorDialog`, `StartBackup_HistoryWarning_ShowsWarning`, `StartBackup_Success_SetsLastRunMessageWithExitCode`, `StartBackup_RunnerThrows_ShowsErrorAndResetsIsRunning`, `StartBackup_RunnerCancelled_ShowsNothingAndResetsIsRunning`, `StartBackup_Finished_RefreshesTreeAndRestoresSelection`).
- `ViewModels/MainWindowViewModelTests`: заменить `HomeViewModel` на `RepositoriesViewModel`.
- `Runs/BackupRunServiceTests` (новый): `RunAsync_BuildsUiRequestWithFilterAndDryRun`, `RunAsync_BeginsNewRunBeforeOrchestrator`.
- `Discovery/DeferredDiscoveryDecisionHandlerTests` (новый): `DecideNewAsync_ReturnsSkip`, `DecideRenameAsync_ReturnsKeepArchiveFolder`, `[Theory] Decide_CancelledToken_ReturnsCanceledTask`.
- `Hosting/AppHostTests`: ожидать `RepositoriesViewModel`; новый `BuildHost_DiscoveryDecisionHandler_IsDeferredHandler`; переименовать тест времени жизни.

**GitHubBackup.ArchitectureTests**
- Без новых тестов. Существующее правило `AppViewModelDependencyTests` покрывает новые ViewModel.

## 8. Проверка

| Пункт | Как проверяется |
|---|---|
| Unit-тесты ViewModel | `RepositoriesViewModelTests` |
| Дерево, статус, дата | `RepositoryOverviewProviderTests`, `Refresh_*` |
| Кнопки и Dry run | `CanStartBackup_*`, `StartBackup_Scope_*`, `StartBackup_DryRunToggle_*` |
| Перечитывание перед запуском (NFR-SYS-2) | `StartBackup_RereadsConfigBeforeRun` |
| Каждый UI-запуск записан под своим `RunId` | `RunContextTests`, `RunIdEnricherTests`, `BackupRunServiceTests` |
| Покрыто тестами, CI зелёный | сборка, тесты без Smoke, `dotnet format --verify-no-changes`, CI пулл-реквеста |
| Окно в запущенном приложении (`wpf-mvvm.md`) | Netwright: без `backup-config.json` виден `#TreeLoadError`; с временным `backup-config.json` видны `#RepositoryTree`, кнопки, `#DryRunToggle` и узлы. Реальный бэкап не запускается. |

## 9. Вне объёма

- Панель прогресса, живой лог, кнопка отмены, сводка, поведение при закрытии окна во время запуска: #38 (FR-12.3, FR-12.4).
- Диалог обнаружения и замена `DeferredDiscoveryDecisionHandler`: #39 (FR-12.2).
- Статус последнего запуска по репозиторию из истории: просмотр истории относится к FR-12.6.
- Кнопка «Refresh» и перечитывание после редактора настроек: в FR-12.1 кнопки нет, редактор настроек — это FR-12.5.
- FlaUI-тест главного окна: #42 (F4.7).

## 10. Открытые вопросы

1. «Статус» в FR-12.1 означает статус в конфигурации (Active, Unavailable, Excluded), а не результат последнего запуска. Подтвердить.
2. Временный обработчик Skip / KeepArchiveFolder до #39 приемлем, включая пометку `User` в истории для промежуточного коммита.
3. Исправление `RunId` на запуск (`IRunContext.BeginRun`) делается в #37, а не отдельным issue.
4. Строка `LastRunMessage` как временная обратная связь до сводки #38 приемлема.
5. «Backup repository» недоступна для `Unavailable` и `Excluded`. Альтернатива: разрешить, и запуск вернёт `Skipped`.

## Решения

Ревью плана 2026-11-13: пользователь принял все пять открытых вопросов в предложенном виде.

1. «Статус» в FR-12.1 — статус в конфигурации: `Active`, `Unavailable`, `Excluded`.
2. До #39 работает временный `DeferredDiscoveryDecisionHandler`: новый репозиторий — `Skip`, переименованный — `RenameDecision.KeepArchiveFolder`. Пометка `DecisionMaker.User` в истории допустима.
3. `IRunContext.BeginRun()` и исправление `RunIdEnricher` входят в #37.
4. `LastRunMessage` — временная обратная связь до сводки #38; критические ошибки и `HistoryWarning` идут в диалог.
5. «Backup repository» недоступна для `Unavailable` и `Excluded`.

## 11. Сложность

**L.** Изменений больше восьми файлов в четырёх проектах. Добавляется фоновый запуск ядра из UI (`Task.Run`, `IsRunning`, повторный вход). Меняется общий для хостов `IRunContext`, а от него зависят лог и история CLI.

## 12. Задачи

- [x] Core: `IRunContext.BeginRun()` и `RunContext` + `RunContextTests`
- [x] Infrastructure: `RunIdEnricher` читает текущий `RunId` + тест
- [x] Core: `Overview/*` и регистрация в `AddGitHubBackupCore`
- [x] Core.Tests: `RepositoryOverviewProviderTests`
- [x] App: `DeferredDiscoveryDecisionHandler` + тесты
- [x] App: `IBackupRunService` и `BackupRunService` + тесты
- [x] App: `BackupScope`, узлы дерева, `RepositoriesViewModel`
- [x] App.Tests: `RepositoriesViewModelTests`
- [x] App: `RepositoriesView.xaml(.cs)`, `App.xaml`, `MainWindowViewModel`, DI; удалить `HomeViewModel` и `HomeView`
- [x] App.Tests: обновить `MainWindowViewModelTests` и `AppHostTests`
- [x] `.claude/rules/wpf-mvvm.md`: пример селектора; сверить `docs/ai/README.md`
- [x] Сборка, тесты без Smoke, `dotnet format --verify-no-changes`
- [x] Проверка в запущенном приложении через Netwright, результат записать в план

## 13. Результат проверки в запущенном приложении

Проверено 2026-11-13 через Netwright (`desktop_expect`) на сборке Debug. Реальный бэкап не запускался.

- Без `backup-config.json`: `#TreeLoadError` виден и называет отсутствующий файл; `#BackupAllButton` доступна, `#BackupRepositoryButton` недоступна.
- С временным `backup-config.json` (аккаунт с репозиториями `Active`, исключённым маской и `Unavailable`; пустой аккаунт; отдельный репозиторий): `#RepositoryTree` виден, `#TreeLoadError` скрыт. Аккаунты идут в порядке файла, пустой аккаунт виден, группа «Individual repositories» идёт последней. Статусы `Active`, `Excluded`, `Unavailable`, дата `Never`.
- Выбран `Active`-репозиторий: `#BackupAccountButton` и `#BackupRepositoryButton` доступны. Выбран исключённый: `#BackupRepositoryButton` недоступна.
- `#DryRunToggle` включается щелчком.
- Хост WPF не читает аргументы командной строки, поэтому временный `backup-config.json` клался рядом с exe и удалялся после проверки.
