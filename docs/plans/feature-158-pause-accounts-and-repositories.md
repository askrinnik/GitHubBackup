# План: #158 F2.11 — пауза аккаунтов и репозиториев; значки доступности и карточки в редакторе настроек

| Поле | Значение |
|---|---|
| Issue | [#158](https://github.com/askrinnik/GitHubBackup/issues/158) — F2.11: Pause accounts and repositories; availability and card layout in the settings editor |
| Lane | Feature |
| Сложность | L |
| Дата | 2026-11-20 |

## 1. Цель

Аккаунт, репозиторий аккаунта и отдельный репозиторий можно поставить на паузу ключом `paused: true` (FR-1.2, FR-1.3, FR-1.4, новое FR-1.9, §7.3).

- Запуск без фильтра пропускает записи на паузе и не обнаруживает аккаунты на паузе (FR-2.7). Сводка показывает `Paused: N` (FR-9.7).
- `--account` / «Backup account» и `--repo` / «Backup repository» снимают паузу на один запуск.
- Главное окно показывает статус значками Segoe Fluent Icons (FR-12.1).
- Редактор настроек получает карточки, заголовки колонок, флажки Paused/Unavailable и значки в списке аккаунтов (FR-12.5).

## 2. Критерии приёмки

Из issue, дословно:

- [ ] PRD содержит текст из раздела «Изменение PRD»; версия повышена, строка истории добавлена
- [ ] Каждое из изменений 1–5 — отдельный коммит в одной ветке и одном pull request, после каждого коммита и прогона тестов workflow остановился и дождался подтверждения пользователя
- [ ] `paused` читается и пишется у аккаунта, репозитория аккаунта и отдельного репозитория; пишется только `true`; `null` и другой тип — ошибка валидации; файл без ключа читается без изменений
- [ ] Запуск без фильтра не бэкапит записи на паузе и репозитории аккаунта на паузе, без элементов в результатах; сводка CLI и UI показывает `Paused: N`
- [ ] `--account` / «Бэкап аккаунта» бэкапит аккаунт на паузе, пропуская его репозитории на паузе; `--repo` / «Бэкап репозитория» бэкапит запись на паузе; пауза в файле не меняется
- [ ] Обнаружение не запрашивает репозитории аккаунта на паузе и не меняет его записи; записи на паузе во включённых аккаунтах сопоставляются как обычно
- [ ] `--dry-run` учитывает паузу; код возврата от паузы не зависит
- [ ] Дерево показывает статус значками по FR-12.1, с подсказками и именами UI Automation; колонки выровнены в светлой и тёмной теме
- [ ] Редактор: карточки разделов, Save и Cancel справа, заголовки колонок, флажки «Paused» и «Unavailable», значки в списке аккаунтов
- [ ] README описывает паузу и ключ `paused`
- [ ] Покрыто тестами (чтение и запись конфигурации, оркестратор, обнаружение, ViewModel, FlaUI); скриншотные тесты главного окна зелёные; CI зелёный

Добавлено проверкой покрытия по коду:

- (добавлено) Редактор настроек сохраняет `paused` уже с коммита 1. Методы `ToConfig` ViewModel редактора пересобирают записи заново; без сквозного поля Save в коммитах 1–4 стёр бы `paused`.
- (добавлено) Запись без ID на паузе не ищется по имени в запуске без фильтра (FR-2.10: поиск идёт только для записей, которые обрабатывает запуск).
- (добавлено) Запись исключена и на паузе: главнее исключение. Её нет в `Paused: N`, в лог пишется предупреждение об исключении, как сейчас.
- (добавлено) `Paused: N` считает только репозитории, без wiki. Если N = 0, счётчика нет. Так же в dry-run и в отменённом запуске.
- (добавлено) Если в запуске без фильтра все записи на паузе, сводка показывает `No repositories to back up.` и `Paused: N`.
- (добавлено) Репозиторий owner аккаунта на паузе из `/user/repos` не предлагается как новый, в том числе как отдельный при `discoverAccessibleRepositories: true`.
- (добавлено) «Backup repository» доступна для записи на паузе: `RepositoryNodeViewModel.IsActive` зависит только от статуса.

## 3. Затрагиваемые проекты и типы

**Core**

- `BackupConfiguration/AccountConfig`, `AccountRepositoryConfig`, `RepositoryConfig`: свойство `bool Paused`.
- `Runs/BackupWorkItem`: `IsPaused` (пауза записи) и `IsAccountPaused`; их заполняет `CreateAll`.
- `Runs/BackupRunFilter`: метод `Pauses(BackupWorkItem)`. Без фильтра — `IsPaused || IsAccountPaused`, с `ForAccount` — `IsPaused`, с `ForRepository` — `false`.
- `Runs/BackupOrchestrator`: пропускает записи на паузе после `ExcludeMatching` и считает их; `ResolveIdsAsync` не ищет ID записей на паузе; событие лога `RepositoryPaused` в `BackupOrchestratorLog` по образцу `RepositoryExcluded`.
- `Runs/BackupRunResult`: `int PausedCount { get; init; }`; на `ExitCode` не влияет.
- `Runs/BackupRunSummary.FormatTotals`: дописывает `, Paused: N` при N > 0.
- `Discovery/RepositoryDiscoveryService.DiscoverAsync`: без фильтра `scopeAccounts` и `scopeTracked` не включают аккаунты на паузе. Полный список `accounts` остаётся для `owningAccount`, поэтому их репозитории не станут новыми.
- `Overview/RepositoryOverviewEntry`: `IsPaused`, `IsAccountPaused`; `Overview/AccountOverview`: `IsPaused`; `RepositoryOverviewProvider` их заполняет (Core-часть коммита 3).

**Infrastructure**

- `BackupConfiguration/BackupConfigJsonContext`: модификатор необязательных ключей охватывает `AccountConfig`; для `paused` — `ShouldSerialize = value is true`. Обновить `<remarks>`.
- `null` и не-bool для `paused` уже дают ошибку через `RespectNullableAnnotations`, как у `discoverAccessibleRepositories`; `BackupConfigValidator` не меняется.

**Cli**

- `BackupSummaryRenderer`: код не меняется, строку даёт `FormatTotals`. Меняются тесты.

**App**

- `ViewModels/AccountRepositorySettingsViewModel`, `RepositorySettingsViewModel`, `AccountSettingsViewModel`: коммит 1 — сквозное `paused` в `ToConfig`; коммит 5 — `[ObservableProperty] IsPaused`, `IsUnavailable` (редактирует `status`), изменение вызывает `_edited`.
- `ViewModels/RepositoryNodeViewModel`, `AccountNodeViewModel`: `ShowsActiveIcon`, `ShowsPausedIcon`, `ShowsUnavailableIcon`, `ShowsExcludedIcon`, `IsMuted`, `StatusName`.
- `Views/RepositoriesView.xaml`: колонка статуса с двумя слотами фиксированной ширины, значок у узла аккаунта, приглушённый текст.
- `App.xaml`: глифы `E73E`, `E769`, `E733`, `E738`, стиль значка статуса.
- `Views/SettingsView.xaml`: карточки, заголовки разделов, строка заголовков таблиц, флажки, значки в списке аккаунтов, Save/Cancel справа (Save — акцентный стиль).

**TestUtilities**

- `BackupConfigJson`: параметр `paused` у аккаунта, записи аккаунта и отдельной записи.
- `UiScenarios`: сценарий `paused-entries` для скриншотных тестов, FlaUI и стенда.

**Документация**

- `docs/PRD.md`: текст по коммитам (раздел 4).
- `README.md`: `backup-config.json`, раздел о паузе, Command-line reference, Desktop app / Settings editor.
- `docs/development.md`: сценарий `paused-entries` в описании `-Scenario`.

## 4. Подход (порядок = коммиты, после каждого — остановка)

**Коммит 1 — ключ `paused` (Core, Infrastructure, сквозное поле в App).**

- `Paused` в трёх типах. Порядок ключей в JSON совпадает с примером §7.3: у аккаунта после `exclude`, перед `repositories`; у записи — после `clonePath`, перед `status`.
- Модификатор JSON пишет ключ только при `true`.
- ViewModel редактора переносят `paused` без изменений.
- PRD: FR-1.2, FR-1.3, FR-1.4, FR-1.9, §7.3; версия 0.51, дата, строка истории из issue.
- README: таблица ключей и пример `backup-config.json`.

**Коммит 2 — пауза в запуске (Core, Cli, App).**

- `BackupWorkItem`, `BackupRunFilter.Pauses`.
- Оркестратор: `SelectIncluded` → `ExcludeMatching` → отделить записи на паузе (лог и счёт) → `ProcessAllAsync`. `PausedCount` идёт в результат обычного запуска и dry-run.
- `ResolveIdsAsync` учитывает `Pauses`. Точечная проверка `--repo` не меняется.
- Обнаружение — по разделу 3.
- `FormatTotals`. История не меняется: элементов для записей на паузе нет.
- PRD: FR-2.7, FR-9.7. README: раздел о паузе (фильтры, `Paused: N`, dry-run, коды возврата).

**Коммит 3 — значки в дереве (App, Core Overview).**

- Поля Overview; логика значков в ViewModel узлов. Слот 1: галочка, пауза или минус. Слот 2: круг с чертой. Исключённая запись — только минус. Под аккаунтом на паузе своей паузы нет, текст приглушён.
- В XAML текст статуса заменяют значки с `ToolTip` и `AutomationProperties.Name` = `StatusName`; AutomationId `RepositoryStatusIcon`, `RepositoryUnavailableIcon`, `AccountStatusIcon`.
- Сценарий стенда `paused-entries`.
- PRD: FR-12.1. README: Desktop app.

**Коммит 4 — карточки редактора, поведение не меняется (App).**

- `GroupBox` разделов → заголовок раздела и карточки по FR-12.5 «Вид»; AutomationId разделов сохраняются.
- Save и Cancel справа, Save акцентный.
- PRD: FR-12.5 «Вид».

**Коммит 5 — флажки, заголовки колонок, значки аккаунтов (App).**

- Строка заголовков `Name`/`URL`, `Branch`, `Archive folder`, `Paused`, `Unavailable` вне `ScrollViewer`; ширины через `SharedSizeGroup` или фиксированные, с учётом полосы прокрутки.
- Подсказки заголовков объясняют пустые значения.
- Флажок «Paused» в карточке «Account»; значок справа от имени в `AccountsList`, приглушение аккаунта на паузе.
- PRD: FR-12.5 «Таблицы репозиториев», «Аккаунт». README: Settings editor.

**Проектные решения**

- Логика паузы в одном месте, `BackupRunFilter.Pauses` в Core; её используют оба хоста.
- `PausedCount` — свойство результата, а не элемент со статусом. Таблица, история и `ExitCode` не меняются.

## 5. Тесты

**Коммит 1**

- `Infrastructure.Tests/JsonBackupConfigStoreTests`: чтение `true`, `false`, без ключа для трёх видов записей; Theory ошибок `null`, `"yes"`, `1` с путями `$.accounts[0].paused`, `$.accounts[0].repositories[0].paused`, `$.repositories[0].paused`.
- `JsonBackupConfigStoreSaveTests`: пишется только `"paused": true` в ожидаемом месте; `false` не пишется; файл без ключа после Save совпадает с исходным.
- `App.Tests/SettingsViewModelTests`: Save без правок сохраняет `Paused` у трёх видов записей.

**Коммит 2**

- `BackupRunFilterTests`: Theory `Pauses` для трёх фильтров и четырёх сочетаний паузы.
- `BackupWorkItemTests`: `CreateAll` заполняет флаги.
- `BackupOrchestratorTests`: без фильтра — нет элементов, верный `PausedCount`, wiki не считается; `--account` для аккаунта на паузе; `--repo` для записи на паузе в аккаунте на паузе; исключена и на паузе; запись без ID на паузе не ищется; dry-run; `ExitCode`; отмена; лог `RepositoryPaused` через `FakeLogger`.
- `RepositoryDiscoveryServiceTests`: без фильтра нет запросов аккаунта на паузе, `/user/repos` вызывается; записи аккаунта на паузе не становятся missing/returned/renamed; репозиторий его owner не новый (и при `DiscoverAccessibleRepositories = true`); запись на паузе во включённом аккаунте — missing; отдельная запись на паузе — renamed; с `accountLogin` обнаружение полное.
- `BackupRunSummaryTests`, `BackupRunResultTests`.
- `Cli.Tests/BackupSummaryRendererTests`: `Paused: 2`; все записи на паузе.
- `CliEndToEndTests`: без фильтра — итоговая строка и код 0; `--repo` для записи на паузе, файл не меняется.
- `App.Tests/RunSummaryViewModelTests`, `RepositoriesViewModelTests` (фильтры «Backup account» и «Backup repository»).

**Коммит 3**

- `RepositoryOverviewProviderTests`: флаги паузы.
- `App.Tests`: Theory по сочетаниям Active, Paused, Unavailable, «Unavailable, paused», Excluded, Excluded+paused, под аккаунтом на паузе — флаги значков, `StatusName`, `IsMuted`.
- UITests: `MainWindowLayoutTests` (выравнивание и UIA-имена значков); `MainWindowScreenshotTests` — сценарий `paused-entries` в Light и Dark.

**Коммиты 4–5**

- `SettingsViewModelTests`: `IsPaused` → `IsDirty` и `Paused`; `IsUnavailable` ↔ `Status`; `Paused` аккаунта.
- `UITests/SettingsEditorTests`: карточки и AutomationId; Save справа от Cancel; заголовки выровнены с полями и не прокручиваются; флажки Paused записи, Unavailable, Paused аккаунта сохраняются в файл; UIA-имя значка в `AccountsList`.
- `ArchitectureTests` без изменений.

## 6. Проверка

- Конфигурация, запуск, обнаружение, dry-run и код возврата — тесты коммитов 1–2 (`--filter-not-trait "Category=Smoke"`).
- Дерево и редактор: App.Tests, ArchitectureTests, скриншотные тесты, FlaUI; стенд `scripts/test-bench.ps1 -Scenario paused-entries` в светлой и тёмной теме.
- PRD и README — по диффу, версия 0.51 и строка истории.
- Перед PR — полный набор, все UI-тесты, format check.

## 7. Вне рамок

- Схема истории, счётчик Paused в истории, правила исключений, автоматический переход Unavailable→Active.
- Найдено попутно: `SettingsViewModel.BuildConfig` и `ToConfig` записей не переносят `clonePath`; Save в редакторе стирает ключ. Отдельный баг, отдельное issue.
- Атрибут паузы в телеметрии.

## 8. Открытые вопросы

1. Версия PRD 0.51 и строка истории — в коммите 1, где PRD меняется впервые; остальные коммиты дописывают свои части без новой версии.
2. `Paused: N` — в конце строки итогов, после `Skipped` (или `Cancelled`).
3. Все записи на паузе: оставить `No repositories to back up.` вместе с `Paused: N`.
4. Значок узла аккаунта: точно в колонке статуса репозиториев (с компенсацией отступа TreeView) или сразу после имени?
5. Active-репозиторий под аккаунтом на паузе: без значка и без подсказки, только приглушённый текст.
6. Слоты: слот 1 — галочка, пауза или минус; слот 2 — Unavailable.

## 9. Задачи

- [x] К1: `Paused` в `AccountConfig`, `AccountRepositoryConfig`, `RepositoryConfig`; модификатор JSON пишет только `true`
- [x] К1: сквозной `paused` в `ToConfig` ViewModel редактора и при смене owner в `DiscoveryDecisionApplier`
- [x] К1: тесты чтения, записи и ошибок; тест Save в редакторе; PRD FR-1.2, FR-1.3, FR-1.4, FR-1.9, §7.3, версия 0.51 и строка истории; README (ключ)
- [x] К2: `BackupWorkItem.IsPaused`/`IsAccountPaused`, `BackupRunFilter.Pauses`
- [x] К2: оркестратор (пропуск, счёт, лог, `ResolveIdsAsync`), `BackupRunResult.PausedCount`, `FormatTotals`
- [x] К2: область обнаружения в `RepositoryDiscoveryService`
- [x] К2: `BackupConfigJson` с паузой; тесты Core, Cli, App; PRD FR-2.7, FR-9.7; README (пауза)
- [x] К3: флаги Overview; значки в ViewModel узлов; XAML-слоты, глифы, подсказки, UIA; сценарий `paused-entries`; development.md; тесты; PRD FR-12.1; README
- [ ] К4: карточки разделов, заголовки, Save/Cancel справа с акцентом; PRD FR-12.5 «Вид»
- [ ] К5: `IsPaused`/`IsUnavailable` в ViewModel; заголовки колонок; флажок аккаунта; значки в `AccountsList`; тесты; PRD FR-12.5; README
- [ ] Перед PR: полный набор, все UI-тесты (FlaUI и скриншоты), format check

## 10. Решения

Ревью плана 2026-11-20, план одобрен с предложенными ответами:

1. Версия PRD 0.51, дата и строка истории — в коммите 1; коммиты 2–5 дописывают свои части PRD без новой версии.
2. `Paused: N` — последний счётчик строки итогов.
3. Все записи на паузе: сводка показывает `No repositories to back up.` и `Paused: N`.
4. Значок узла аккаунта стоит в колонке статуса, выровненной по всем строкам дерева (FR-12.1: колонки выровнены по всем строкам).
5. Active-репозиторий под аккаунтом на паузе: без значка и подсказки, только приглушённый текст.
6. Слот 1 — галочка, пауза или минус; слот 2 — Unavailable.
