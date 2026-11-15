# План: #137 F3.17: Informative span attributes and a span table in README

| Поле | Значение |
|---|---|
| Issue | [#137](https://github.com/askrinnik/GitHubBackup/issues/137) |
| Lane | Feature |
| Сложность | M |
| Дата | 2026-11-15 |

## 1. Цель

FR-10.5 (PRD 0.38) задаёт фиксированный список имён спанов. Два запроса `ls-remote` получают разные имена: `ls-remote refs` и `ls-remote default-branch`; их различает атрибут `githubbackup.git.query`. Спаны получают новые атрибуты:
- `backup.repository`: решение и причина детектора изменений, ветка;
- `ls-remote refs`: число ссылок;
- `clone` и `fetch`: шаги LFS и сабмодулей;
- `archive`: размер архива.

Атрибуты содержат только значения фиксированных списков, числа и имена веток (NFR-1). README получает таблицу спанов, новое дерево и новый список атрибутов.

## 2. Критерии приёмки

Из issue:
1. PRD (FR-10.5) описывает имена спанов и атрибуты; версия 0.38, строка в истории.
2. Два запроса `ls-remote` видны под разными именами и с атрибутом `githubbackup.git.query`.
3. `backup.repository` несёт `githubbackup.change.decision`, `githubbackup.change.reason` и `githubbackup.branch`, когда значения известны.
4. `ls-remote refs` несёт `githubbackup.git.refs_count`; `clone` и `fetch` несут `githubbackup.git.lfs` и `githubbackup.git.submodules`; `archive` несёт `githubbackup.archive.size`.
5. Ни один атрибут не содержит командную строку git, URL, токен или текст ответа сервера.
6. Тесты в `GitHubBackup.Core.Tests` проверяют имена спанов и атрибуты через `ActivityListener`.
7. README: таблица спанов, обновлённые дерево и список атрибутов.
8. CI зелёный.

Добавлено по проверке покрытия:
- (добавлено) Оба `ls-remote`-спана сохраняют `githubbackup.operation` = `ls-remote`. У `clone`, `fetch` и `archive` имя спана равно операции.
- (добавлено) Неизвестное значение не ставится: сбой до решения детектора — нет `change.*` и `branch`; `Skipped` — нет `change.*`; `NothingToArchive` и `CloneNeeded` — нет `branch`; упавший `clone`/`fetch` — нет `git.lfs`/`git.submodules`; упавший `ls-remote refs` — нет `refs_count`; сбой `archive` до 7-Zip — нет `archive.size`.
- (добавлено) В dry run `backup.repository` тоже получает `change.decision` и `change.reason`.
- (добавлено) Wiki без клона в dry run даёт `ls-remote refs` с `refs_count`; `RemoteMayBeMissing` по-прежнему оставляет статус `Unset`.
- (добавлено) Внутренний `ls-remote --symref` внутри `clone`/`fetch` не создаёт отдельного спана; README пишет об этом.

## 3. Затронутые проекты и типы

**GitHubBackup.Core**
- `Telemetry/BackupTelemetry.cs`: константы имён спанов (`LsRemoteRefsSpanName`, `LsRemoteDefaultBranchSpanName`), тегов (`GitQueryTag`, `GitRefsCountTag`, `GitLfsTag`, `GitSubmodulesTag`, `ArchiveSizeTag`, `ChangeDecisionTag`, `ChangeReasonTag`, `BranchTag`) и значений query (`remote_refs`, `default_branch`); приватный `TraceAsync` с именем спана, операцией и query; перегрузка `TraceOperationAsync` с `Func<Activity?, Task<T>>`; `TraceRemoteRefsAsync`, `TraceDefaultBranchAsync`; статические `SetSyncResult`, `SetArchiveSize`, `SetDetection`.
- `Changes/ChangeDetector.cs`: три вызова ls-remote через новые методы; `clone` и `fetch` ставят `SetSyncResult`.
- `Runs/BackupOrchestrator.cs`: `BackupAsync` получает `Activity?` спана элемента и вызывает `SetDetection` сразу после детекции; `ArchiveAsync` вызывает `SetArchiveSize`.
- `Git/IGitClient.cs`, `Git/GitSyncResult.cs`: только XML-док о том, что `submodule update` и `lfs pull` выполняются ровно тогда, когда ветка выгружена.

**GitHubBackup.Core.Tests**: `Telemetry/BackupTelemetryTests.cs`, `Changes/ChangeDetectorTests.cs`, `Runs/BackupOrchestratorTests.cs`.

**README.md**, раздел «Telemetry (OpenTelemetry)».

Infrastructure, Cli и App не меняются.

## 4. Подход

1. `BackupTelemetry` сводит логику в один приватный `TraceAsync<T>(spanName, operation, query, action, remoteMayBeMissing)` с нынешней обработкой ошибок. `TraceRemoteRefsAsync` ставит `refs_count = result.Refs.Count` только при успехе. `TraceDefaultBranchAsync` ставит только `query`; имя ветки по умолчанию в этот спан не пишется.
2. Хелперы: `SetSyncResult` ставит `git.lfs` и `git.submodules` = `!sync.IsEmpty`; `SetArchiveSize`; `SetDetection` ставит `decision` и `reason` именами перечислений и `branch` только при `Branch is not null`. Имя ветки идёт только значением `SetTag`.
3. `ChangeDetector`: лямбды `clone`/`fetch` получают `activity` и после успеха вызывают `SetSyncResult`.
4. `BackupOrchestrator`: явный параметр `Activity?` вместо `Activity.Current`; `SetDetection` до возврата `WouldClone`.
5. README сверяется с кодом: точные условия появления каждого спана (см. Decisions 3 и 4); метрика `githubbackup.archive.size` и одноимённый атрибут спана — разные сигналы.

## 5. Тесты

Захват через существующий `Telemetry/TelemetryCapture` (ActivityListener).
- `BackupTelemetryTests`: ls-remote-методы (успех, сбой без `refs_count`, `RemoteMayBeMissing`), перегрузка с `Activity?`, хелперы (ветка известна и нет, пустой репозиторий, размер, `null`-активность).
- `ChangeDetectorTests`: обновить ожидаемые имена спанов в существующих тестах; новые тесты по веткам кода (неизменённый сервер с `refs_count`, ветка не задана → refs затем default-branch, нет архива или его файла → только `fetch`, `clone`/`fetch` с `true`/`false`, сбой `clone`, повторная проверка в `fetch`, wiki без клона в dry run, default-branch в dry run); защитный тест состава ключей тегов без URL, токена и `git `.
- `BackupOrchestratorTests`: теория тегов решения по путям, сбой детектора, `Skipped`, wiki, `archive.size`, сбой архива до 7-Zip, dry run без `archive`.

## 6. Вне объёма

- Атрибут с именем ветки по умолчанию в `ls-remote default-branch`.
- Отдельные спаны шагов внутри `clone`/`fetch`.
- Настройки, отключающие LFS или сабмодули.
- Статус спана wiki без git-репозитория.

## 7. Decisions

Решения приняты в пакете `implement-issues` без отдельного обзора плана:
1. `githubbackup.operation` у обоих `ls-remote`-спанов равен `ls-remote`; различие несут имя спана и `githubbackup.git.query`. Альтернатива — разные значения `operation`; тогда ломаются существующие фильтры по операции.
2. `git.lfs` и `git.submodules` = `!sync.IsEmpty`: `GitClient` выполняет `submodule update` и `lfs pull` ровно тогда, когда ветка выгружена. Контракт фиксируется в XML-доке. Альтернатива — явные флаги в `GitSyncResult`, которые ставит Infrastructure; честнее при будущих настройках, но затрагивает Infrastructure и все конструкторы `GitSyncResult` в тестах без пользы сейчас.
3. README пишет точное условие `ls-remote refs` в обычном запуске: клон есть и файл последнего архива существует; иначе сразу `fetch`. Таблица issue говорит упрощённо «быстрая проверка существующего клона».
4. README пишет, что у `backup.repository` без детей причину показывает `githubbackup.status` (и `error.type` при сбое), а среди спанов без детей `githubbackup.change.reason` есть только у dry run без клона (`NoClone`): `Skipped` и ранний сбой решения детектора не имеют. У спанов с детьми `change.*` ставится всегда, когда детектор дал результат. Issue называет оба атрибута для всех спанов без детей.
5. Если `clone`/`fetch` упал после части шагов, `git.lfs`/`git.submodules` не ставятся: значение неизвестно.

## 8. Задачи

- [x] `BackupTelemetry`: константы, `TraceAsync`, перегрузка с `Activity?`, `TraceRemoteRefsAsync`, `TraceDefaultBranchAsync`, `SetSyncResult`, `SetArchiveSize`, `SetDetection`, XML-доки.
- [x] `IGitClient`/`GitSyncResult`: XML-док о шагах `submodule update` и `lfs pull`.
- [x] `ChangeDetector`: ls-remote через новые методы; `SetSyncResult` в `clone`/`fetch`; XML-доки.
- [x] `BackupOrchestrator`: `activity` в `BackupAsync`, `SetDetection`; `SetArchiveSize` в `ArchiveAsync`.
- [x] `BackupTelemetryTests`: новые тесты.
- [x] `ChangeDetectorTests`: обновлённые имена спанов, тесты по веткам, защитный тест состава тегов.
- [x] `BackupOrchestratorTests`: теги решения, сбои, `Skipped`, wiki, `archive.size`, dry run.
- [x] README: таблица спанов, дерево, список атрибутов, пояснения, строка о wiki, шаг Aspire Dashboard.
- [x] Гигиена комментариев, сборка, тесты, `dotnet format`.
