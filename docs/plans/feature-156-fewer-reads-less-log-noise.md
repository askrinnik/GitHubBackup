# План: #156 «F3.21: Fewer reads and less log noise in a backup run»

- Issue: [#156](https://github.com/askrinnik/GitHubBackup/issues/156)
- Лейн: Feature
- Сложность: L (по коммитам: 1 — M, 2 — S, 3 — L, 4 — S, 5 — S, 6 — M, 7 — S)
- Дата: 2026-11-20

Ветка `156-<slug>`, один PR, **7 коммитов в порядке изменений 1–7**. После каждого коммита сборка и тесты зелёные.

## 1. Цель

Убрать лишние чтения и лишний шум в логе за один запуск бэкапа.

- **NFR-SYS-2.** UI не перечитывает дерево перед запуском. После запуска UI читает состояния только у затронутых записей.
- **FR-6.2 п. 3.** На каждый репозиторий выполняется один `ls-remote`. Он возвращает ветки, теги и ветку по умолчанию, без `refs/pull/*`.
- **FR-10.5.** Убираются спан `ls-remote default-branch` и атрибут `githubbackup.git.query`.
- **FR-10.4 (уровни, текст PRD не меняется).** Проверка инструментов, `ls-remote`, лимит API, фильтр запуска и выключенная wiki дают по одной информативной строке. Подробности уходят на уровни Debug и Trace.

## 2. Критерии приёмки

Список из issue (дословно):
- PRD (FR-6.2, FR-10.5, NFR-SYS-2) содержит текст из раздела «Изменение PRD»; версия PRD повышена, строка в истории изменений добавлена
- Каждое из изменений 1–7 — отдельный коммит в одной ветке и одном pull request
- Перед запуском UI не читает `backup-config.json` и файлы состояния; после запуска читает состояния только обработанных, новых и перемещённых записей; без результата запуска и по Refresh — все
- Вместо строк `outside the filter` — одна строка Information с числом выбранных и всех репозиториев; без фильтра строки нет
- На репозиторий выполняется один `ls-remote`; в выводе нет `refs/pull/*`; ветка по умолчанию берётся из того же ответа в `ChangeDetector` и `CloneGuard`
- Парсер пропускает строки `HEAD` с хешем, `^{}` и ссылки вида `…/HEAD`; это покрыто тестами
- Спана `ls-remote default-branch` и атрибута `githubbackup.git.query` нет; README и урок 5 курса New Relic обновлены
- Для git, git-lfs и 7-Zip — по одной строке Information о доступности с версией и путём; запуск, вывод и завершение процессов проверки — на уровне Trace; справки 7-Zip нет в логе при уровне по умолчанию
- Запуск, вывод и успешное завершение `ls-remote` пишутся на уровне Debug; итог детектора изменений на уровне Information содержит число веток и тегов
- Лимит API: Debug после каждого запроса, одна строка Information в конце обнаружения, Warning при остатке меньше 10 %
- Выключенная wiki — одна строка Debug за запуск с числом репозиториев
- Покрыто тестами (ViewModel, оркестратор, `GitRefParser`, `GitClient` на локальном bare-репозитории с pull-ссылками, логирование через `FakeLogger`), CI зелёный

Добавлено проверкой покрытия:
- (добавлено) Запуск с результатом, но отменённый (`IsCancelled`) или с `CriticalErrors`, идёт по частичному пути: конфигурация перечитывается, состояния читаются только у элементов результата.
- (добавлено) Если до запуска дерево не было прочитано (ошибка или нет конфигурации), то после запуска UI читает всё.
- (добавлено) Статус записи (`Excluded`, `Active`, `Unavailable`) всегда пересчитывается из новой конфигурации, даже когда состояние берётся из прежних данных.
- (добавлено) Строка фильтра есть и у запуска одного репозитория (`--repo`, проверенная запись по ID).
- (добавлено) Ветка или тег с именем вида `x/HEAD` в `refs/heads/` или `refs/tags/` остаётся в снимку (решение 1).
- (добавлено) Строка `ref: …` для другой символической ссылки (не `HEAD`) не попадает в снимок.
- (добавлено) Нет строки Information о лимите, если запросов к API не было. Warning о лимите при следующем запуске снова возможен.
- (добавлено) Нет строки о выключенной wiki, если выключенных wiki нет.

## 3. Затронутые проекты и типы

**Core**
- `Overview/IRepositoryOverviewProvider`, `RepositoryOverviewProvider`: новый метод `LoadAfterRunAsync(RepositoryOverview previous, BackupRunResult result, CancellationToken)`.
- `Runs/BackupOrchestrator` (`SelectIncluded`, `ProcessAllAsync`, `ProcessWikiAsync`) и `BackupOrchestratorLog`: новые итоговые записи; удаляются `RepositoryFilteredOut` и `WikiDisabled`.
- `Git/RemoteRefs` (новый): `sealed record RemoteRefs(RefSnapshot Refs, string? DefaultBranch)`.
- `Git/IGitClient`: `GetRemoteRefsAsync(request, ct)` возвращает `RemoteRefs`; `GetDefaultBranchAsync` удаляется.
- `Git/RefSnapshot`: новые `BranchCount` и `TagCount`.
- `Changes/ChangeDetector`, `ChangeDetectorLog`: один `ls-remote`, ветка по умолчанию из ответа; в итог добавляются счётчики.
- `Clones/ICloneGuard`, `CloneGuard`: параметр `RemoteRefs? remote`, второго запроса нет.
- `Telemetry/BackupTelemetry`: удаляются `LsRemoteDefaultBranchSpanName`, `RemoteRefsQuery`, `DefaultBranchQuery`, `GitQueryTag`, `TraceDefaultBranchAsync` и параметр `query` метода `TraceAsync`; `TraceRemoteRefsAsync` получает перегрузку для `RemoteRefs`.
- `GitHub/IGitHubApi`: новое свойство `GitHubRateLimit? LastRateLimit { get; }`.
- `Discovery/RepositoryDiscoveryService` и его Log: строка Information о лимите в конце `DiscoverAsync` и `CheckRepositoryAsync`.

**Infrastructure**
- `Git/GitRefParser`: новый `ParseRemoteListing(lines) → RemoteRefs`; `ParseRemoteRefs` пропускает `HEAD`, строки `ref: …`, `^{}` и `refs/heads/HEAD`.
- `Git/GitClient`: новые аргументы `ls-remote`; уровень Debug для всех `ls-remote` (необязательный `LogLevel` в `RunGitAsync` и `RunGitProcessAsync`).
- `Tools/ToolLocator`, `ToolLocatorLog`: процессы проверки на уровне `Trace`; `ToolFound` на уровне Information с текстом «{Tool} {Version} is available at {Path}».
- `GitHub/OctokitGitHubApi`, `OctokitGitHubApiLog`: `RateLimit` на уровне Debug, новый Warning, `LastRateLimit`, сброс в `UseToken`.

**App**
- `ViewModels/RepositoriesViewModel`: перед запуском `LoadAsync` не вызывается; после запуска — `LoadAfterRunAsync`; поле `_overview` хранит последний прочитанный обзор.

**Документы:** `docs/PRD.md`, `README.md` (Telemetry), `docs/new-relic-course/05-githubbackup.md`.

## 4. Подход по коммитам

### Коммит 1. UI не перечитывает дерево перед запуском, после запуска читает выборочно (M)

- **PRD:** заменить NFR-SYS-2 (§8.4) одобренным текстом. Версия `0.48 → 0.49`, дата 2026-11-20. Строка истории `0.49` с частью про NFR-SYS-2 из одобренного текста.
- **Core.** `IRepositoryOverviewProvider.LoadAfterRunAsync(previous, result, ct)`:
  1. Читает `backup-config.json` и строит `BackupWorkItem.CreateAll`; обработка ошибок как в `LoadAsync`.
  2. Строит множество обработанных репозиториев из `result.Items`; для `Target == Wiki` суффикс `.wiki` отрезается; сравнение `OrdinalIgnoreCase`.
  3. Для каждой записи ищет прежнюю: ключ `RepositoryId`, если он не 0, иначе `FullName` (`OrdinalIgnoreCase`).
  4. Прежние `LastArchiveAt` и `StateError` используются, если прежняя запись найдена, `ArchiveFolder` совпадает (`OrdinalIgnoreCase`) и репозитория нет среди обработанных. Иначе читается `.backup-state.json`.
  5. Статус всегда считается заново.

  Общий код с `LoadAsync`: обход аккаунтов и `CreateEntryAsync` с делегатом «взять прежнее или прочитать».
- **App.** `StartBackupAsync`: убрать `await LoadAsync(...)` перед `runService.RunAsync`; сохранить `result`; в `finally`: есть `result` и `_overview` → `LoadAfterRunAsync`, иначе `LoadAsync`. `LoadAsync` и новый путь записывают `_overview`; ошибка и `BackupConfigNotFoundException` обнуляют его. `RefreshAsync` — полное чтение. Обновить `<remarks>` у `StartBackupAsync`.
- В Core передаётся весь `BackupRunResult`: разбор имён wiki живёт в Core и проверяется в `Core.Tests`.

### Коммит 2. Одна строка о фильтре запуска (S)

- `SelectIncluded` считает отобранные записи и в конце пишет одну строку, если фильтр не `All` или задан `checkedId`.
- Два `[LoggerMessage]` уровня Information: «Run filter account {Account} keeps {Kept} of {Total} repositories» и «Run filter repository {Repository} keeps {Kept} of {Total} repositories».
- Удалить `RepositoryFilteredOut`; новым записям — следующие свободные EventId.

### Коммит 3. Один `ls-remote` на репозиторий (L)

- **PRD:** FR-6.2 п. 3 и FR-10.5 (список спанов, пункт «все операции») — одобренный текст; версия `0.49 → 0.50`, строка истории `0.50` с частью про FR-6.2 и FR-10.5 из одобренного текста.
- **Core:** `RemoteRefs`; `RefSnapshot.BranchCount`/`TagCount`; `IGitClient.GetRemoteRefsAsync(request, ct)` → `Task<RemoteRefs>`; `GetDefaultBranchAsync` удаляется; перегрузка по URL оставляет `Task<RefSnapshot>`.
- **ChangeDetector:** `TryConfirmUnchangedAsync` и `ChooseBranchAsync` берут `remote.DefaultBranch`; `EnsureNoLocalChangesAsync` передаёт `RemoteRefs?` в `CloneGuard`.
- **CloneGuard:** `CheckAsync(git, RemoteRefs? remote, …)`; `CloneRelocator` передаёт `null` (тогда один запрос `GetRemoteRefsAsync`).
- **BackupTelemetry:** `TraceRemoteRefsAsync` ставит `githubbackup.git.refs_count`; спан и атрибут ветки по умолчанию удаляются.
- **GitClient:** через `origin`: `["ls-remote", "--symref", "origin", "HEAD", "refs/heads/*", "refs/tags/*"]` → `GitRefParser.ParseRemoteListing`. По URL (проверка wiki без клона в dry run): `["ls-remote", "--refs", "--", remoteUrl, "refs/heads/*", "refs/tags/*"]` — ветка по умолчанию там не нужна (`PreviewAsync` без клона сразу возвращает `CloneNeeded`). Токен только через окружение процесса.
- **GitRefParser:** `ParseRemoteRefs` пропускает имя `HEAD`, значения `ref: …`, суффикс `^{}`, `refs/heads/HEAD`; прочие пространства имён отбрасываются. `ParseRemoteListing = new(ParseRemoteRefs(lines), ParseDefaultBranch(lines))`.
- Путь клонирования не меняется: после `clone` без заданной ветки остаётся `ls-remote --symref origin HEAD` внутри спана `clone`.
- **Документы:** README (Telemetry: дерево спанов, таблицы спанов и атрибутов, шаги Traces), `docs/new-relic-course/05-githubbackup.md`.

### Коммит 4. Одна строка на инструмент (S)

- `ToolLocator.RunVersionAsync`: `LogLevel = LogLevel.Trace`.
- `ToolLocatorLog.ToolFound`: Information, «{Tool} {Version} is available at {Path}».
- Ошибки пишутся как сейчас.

### Коммит 5. `ls-remote` на уровне Debug, счётчики в итоге детектора (S)

- `GitClient`: необязательный `LogLevel` в `RunGitAsync`/`RunGitProcessAsync`; все вызовы `ls-remote` — `Debug`. Ошибки — как сейчас.
- `ChangeDetectorLog.Decided`: `{Branches}` и `{Tags}` из `detection.Refs`. Для превью без опроса сервера — отдельная запись без счётчиков.

### Коммит 6. Лимит GitHub API (M)

- `OctokitGitHubApi.LogRateLimit` сохраняет `LastRateLimit`, пишет `RateLimit` на Debug; при `remaining * 10 < limit` один Warning за запуск («GitHub API rate limit is low: …»). `UseToken` сбрасывает флаг и `LastRateLimit`.
- `RepositoryDiscoveryService`: в конце `DiscoverAsync` и `CheckRepositoryAsync` одна строка Information «GitHub API rate limit after discovery: {Remaining}/{Limit} remaining, resets at {ResetAt}», если `LastRateLimit` не `null`.

### Коммит 7. Одна строка о выключенной wiki (S)

- `ProcessAllAsync` считает проверенные и выключенные wiki активных репозиториев; в `finally` пишет Debug «Wiki is disabled for {Disabled} of {Checked} repositories», если `disabled > 0`.
- Удалить `WikiDisabled`; `WikiNotFound` не меняется.

## 5. Тесты

| Коммит | Проект и класс | Случаи |
|---|---|---|
| 1 | `Core.Tests/Overview/RepositoryOverviewProviderTests` | обработанный перечитывается; необработанный берёт прежние дату и `StateError` без чтения; новая запись читается; другая `ArchiveFolder` читается; wiki-элемент `owner/name.wiki` перечитывает `owner/name`; сопоставление по имени без ID; `Excluded` пересчитывается; удалённая запись исчезает; ошибка конфигурации → `BackupConfigException` |
| 1 | `App.Tests/ViewModels/RepositoriesViewModelTests` | перед `RunAsync` нет `LoadAsync`; после результата — `LoadAfterRunAsync`; отмена до старта и неожиданная ошибка → `LoadAsync`; дерево не прочитано → `LoadAsync`; Refresh → `LoadAsync` |
| 2 | `Core.Tests/Runs/BackupOrchestratorTests` | фильтр аккаунта: одна строка «keeps 2 of 3»; фильтр репозитория; `All` — строки нет |
| 3 | `Infrastructure.Tests/Git/GitRefParserTests` | `ref: … HEAD` → ветка по умолчанию; `<sha>\tHEAD`, `^{}`, `refs/remotes/origin/HEAD`, `refs/pull/1/head`, `refs/heads/HEAD` пропущены; `refs/heads/x/HEAD` остаётся; `ref:` другого имени пропущен; без `ref:` → `null`; пустой вывод |
| 3 | `Infrastructure.Tests/Git/GitClientTests` | точные аргументы для `origin` и URL; токена нет в аргументах |
| 3 | `Infrastructure.IntegrationTests/Git/GitClientRefSnapshotTests` | bare-репозиторий с `refs/pull/1/head`: снимок и stdout без `refs/pull/`; ветка по умолчанию совпадает; снимки сервера и клона совпадают |
| 3 | `Core.Tests/Changes/ChangeDetectorTests`, `Core.Tests/Clones/CloneGuardTests`, `Core.Tests/Telemetry/BackupTelemetryTests` | один запрос; ветка по умолчанию из ответа; один спан `ls-remote refs` с `refs_count`, без `githubbackup.git.query` |
| 4 | `Infrastructure.Tests/Tools/ToolLocatorTests` | `LogLevel == Trace` у трёх запросов; три строки Information «… is available at …» |
| 5 | `GitClientTests`, `ChangeDetectorTests` | `ls-remote` на Debug, `fetch`/`clone` на Information; итог детектора со счётчиками |
| 6 | `Infrastructure.Tests/GitHub/OctokitGitHubApiTests`, `Core.Tests/Discovery/RepositoryDiscoveryServiceTests` | Debug; 499/5000 → один Warning; 500/5000 → нет; после `UseToken` снова возможен; строка Information в конце обнаружения |
| 7 | `Core.Tests/Runs/BackupOrchestratorTests` | 2 из 3 → одна строка Debug «2 of 3»; нет выключенных → строки нет; неактивный не считается |

## 6. Проверка

- Каждый пункт приёмки проверяется тестами из раздела 5.
- Сборка, все тесты, `dotnet format` — через build runner.
- grep по `default-branch|git\.query` в `src`, `README.md`, `docs/new-relic-course` пустой.
- `git log --oneline main..HEAD`: 7 коммитов (по одному на изменение).

## 7. Вне рамок

- После `clone` без заданной ветки остаётся `ls-remote --symref origin HEAD`; замена на локальный `refs/remotes/origin/HEAD` — отдельная задача.
- Не меняются: запрос `/user/repos`, проверка инструментов в каждом запуске, алгоритм FR-6.2, подписка живого лога.

## Decisions

1. **`…/HEAD` в парсере.** Пропускаются имя `HEAD`, строки `ref: …`, `^{}` и `refs/heads/HEAD`. Ветки и теги вида `refs/heads/x/HEAD` остаются в снимке: локальный снимок их тоже содержит, иначе снимки никогда не совпадут. Ветка по умолчанию берётся только из строки `ref: … HEAD`.
2. **Знаменатель в строке о wiki** — активные репозитории, у которых проверялась wiki; строки нет, если выключенных 0.
3. **Порог Warning** — строго `remaining < 10 % limit`; сброс через `UseToken` в начале запуска.
4. **Версия PRD** — каждая правка PRD повышает версию (`.claude/rules/docs.md`): коммит 1 — `0.49` со строкой истории про NFR-SYS-2, коммит 3 — `0.50` со строкой про FR-6.2 и FR-10.5. Одобренная строка истории делится на две части по коммитам, текст не меняется.
5. **Dry run после запуска** — состояния элементов dry run перечитываются так же, как после обычного запуска.
6. **Сложность L** не остановила batch: пользователь попросил пройти весь процесс без остановок, если нет вопросов по плану, а issue в пакете один.

## 8. Задачи

- [x] Коммит 1: PRD NFR-SYS-2, версия 0.49, строка истории; `LoadAfterRunAsync`; `RepositoriesViewModel`; тесты
- [x] Коммит 2: итоговая строка фильтра в `SelectIncluded`; тесты
- [x] Коммит 3: PRD FR-6.2 и FR-10.5; `RemoteRefs`, `IGitClient`, `GitRefParser`, `GitClient`, `ChangeDetector`, `CloneGuard`, `BackupTelemetry`; тесты; README и урок 5
- [x] Коммит 4: `ToolLocator` — Trace для процессов, Information «is available at»; тесты
- [x] Коммит 5: `ls-remote` на Debug; счётчики в итоге детектора; тесты
- [ ] Коммит 6: лимит GitHub API — Debug, Warning, строка Information в конце обнаружения; тесты
- [ ] Коммит 7: одна строка о выключенной wiki; тесты
- [ ] Сборка, тесты, `dotnet format`; проверка комментариев
