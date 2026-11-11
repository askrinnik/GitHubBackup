# План: #19 F1.12: Backup orchestrator for an explicit repository list

| Поле | Значение |
|---|---|
| Issue | [#19](https://github.com/askrinnik/GitHubBackup/issues/19) |
| Заголовок | F1.12: Backup orchestrator for an explicit repository list |
| Направление | Feature |
| Сложность | L |
| Дата | 2026-11-11 |

## 1. Цель

Нужен `BackupOrchestrator` (§8.2), который выполняет запуск по §9 для фазы 1 (§12) без обнаружения. Порядок такой:

1. Загрузить `backup-config.json`.
2. Проверить инструменты.
3. Получить токен.
4. Очистить `<sourcesRoot>\.tmp\` (FR-7.4).
5. Обработать активные репозитории по одному: детектор изменений → архив → запись состояния.
6. Вернуть сводку (FR-9.7, статусы FR-9.8) и код возврата (FR-9.10).

Ошибка одного репозитория не прерывает запуск (FR-9.9, NFR-3). В CLI появляется минимальная команда `backup` (FR-9.1) без `--account/--repo/--dry-run/--silent`.

## 2. Критерии приёмки

Из issue:

1. Интеграционный тест полного цикла на локальных репозиториях: первый запуск, повтор без изменений, повтор с изменениями.
2. Ошибка одного репозитория отражена в сводке, остальные обработаны.
3. Тест: файл, оставленный в `<sourcesRoot>\.tmp\` прерванным запуском, удалён в начале следующего запуска.
4. Покрыто тестами, CI зелёный.

Добавлено (из PRD и сверки с кодом):

5. *(добавлено, FR-9.7)* Сводка — таблица по репозиториям: репозиторий, статус, архив, длительность, ошибка. Под таблицей итоги по каждому статусу и путь к текстовому логу дня.
6. *(добавлено, FR-9.10)* Коды возврата:
   - `2` — ошибка `backup-config.json` (`BackupConfigException`) или инструментов (`ToolUnavailableException`). Репозитории не обрабатываются, `.tmp` не трогается. В stderr — по строке на ошибку и путь к логу.
   - `1` — хотя бы один `Failed`.
   - `0` — во всех остальных случаях, в том числе при пустом списке.
7. *(добавлено, FR-9.8)* `Unavailable` в `backup-config.json` даёт `Skipped`. Детектор и git для такого репозитория не вызываются.
8. *(добавлено, FR-4.4)* `ChangeDetection.BranchFallback = true` даёт `Warning`. Архив при этом создаётся, если он нужен. В столбце ошибки — текст предупреждения.
9. *(добавлено, FR-4.5)* Пустой репозиторий (`ChangeDecision.NothingToArchive`) получает `UpToDate`, архив не создаётся.
10. *(добавлено, FR-6.4)* Если архиватор упал, запись в `.backup-state.json` не добавляется. Если упала запись состояния, статус `Failed`.
11. *(добавлено)* Обрабатываются оба вида записей: репозитории аккаунтов (`accounts[].repositories`) и отдельные (`repositories`).
12. *(добавлено)* Если папки `.tmp` нет, это не ошибка. Файл, который не удалось удалить, пишется в лог как предупреждение, запуск продолжается.
13. *(добавлено, NFR-4)* `OperationCanceledException` при отменённом токене не превращается в `Failed`: исключение выходит из оркестратора, следующие репозитории не обрабатываются.
14. *(добавлено, FR-3.8)* Предупреждения `BackupConfigLoadResult.Warnings` пишутся в лог и показываются в консоли.

## 3. Изменения PRD

- **FR-9.10, строка кода `2`:** добавить «неверные аргументы командной строки». Без этого System.CommandLine при ошибке разбора возвращает `1`, а по FR-9.10 код `1` значит «часть репозиториев `Failed`». Версия `0.14`, строка в истории изменений.

Остальное в пределах PRD: фаза 1 реализует часть шагов §9 (1, 2, 3, 4 без блокировки, 7, 8, 9 без истории).

## 4. Затрагиваемые проекты и типы

**GitHubBackup.Core**

- `Runs/BackupItemStatus` (новый) — статусы FR-9.8 для фазы 1: `Archived`, `UpToDate`, `Warning`, `Failed`, `Skipped`. `WouldArchive`/`WouldClone` добавит задача про dry-run.
- `Runs/BackupItemResult` (новый, record) — `Repository` (`owner/name`), `Status`, `ArchiveFileName?`, `Duration`, `Message?`.
- `Runs/BackupRunResult` (новый, record) — `Items`, `ConfigWarnings`, `CriticalErrors`; вычисляет `ExitCode` (Critical → RepositoriesFailed → Success) и счётчики по статусам.
- `Runs/IBackupOrchestrator`, `Runs/BackupOrchestrator` (новые) — `Task<BackupRunResult> RunAsync(CancellationToken)`.
- `Runs/BackupOrchestratorLog` (новый) — `[LoggerMessage]`: начало и итог репозитория, пропуск, удаление и неудаление файла в `.tmp`, критическая ошибка.
- `Runs/BackupWorkItem` и сборка списка работ (новые) — плоский список из `BackupConfig`: owner, name, id, branch, папка архивов, папка клона, `IsActive`.
- `GitHub/IRemoteUrlProvider`, `GitHub/GitHubRemoteUrlProvider` (новые) — `GetRepositoryUrl(owner, name)` → `https://github.com/<owner>/<name>.git`. Точка подмены для интеграционного теста на локальных bare-репозиториях; поведение продукта не меняется.
- `CoreServiceCollectionExtensions` (изменён) — регистрация оркестратора и `IRemoteUrlProvider`.

**GitHubBackup.Infrastructure** — изменений в коде нет; используются `GitClient`, `SevenZipArchiver`, `JsonBackupStateStore`, `JsonBackupConfigStore`, `ToolLocator`, `TokenProvider`, `RepositoryPathResolver`, `LogFiles`.

**GitHubBackup.Cli**

- `Program` (изменён) — `RootCommand` с подкомандой `backup` (System.CommandLine).
- Описание команд (новый тип) — `TreatUnmatchedTokensAsErrors = false` на корне, чтобы `--Section:Key=value` проходили; опция `--config <path>` для справки; ошибка разбора → `ExitCode.Critical`.
- `CliApplication` (изменён) — для `backup` вызывает `BackupCommandHandler`; без команды поведение прежнее.
- `BackupCommandHandler` (новый) — вызывает оркестратор, отдаёт результат рендереру, возвращает `ExitCode`.
- `BackupSummaryRenderer` (новый) — Spectre.Console через внедрённый `IAnsiConsole`: таблица, итоги, `Log: <path>`; при критической ошибке — строки ошибок и путь к логу.
- `GitHubBackup.Cli.csproj` — `System.CommandLine`, `Spectre.Console`.

**src/Directory.Packages.props** — `System.CommandLine` (2.0.x), `Spectre.Console`, `Spectre.Console.Testing` (тесты). Перед добавлением — проверка `--vulnerable` (правило D1).

## 5. Подход

1. **Core: модель результата** — `BackupItemStatus`, `BackupItemResult`, `BackupRunResult`, затем `IRemoteUrlProvider`.
2. **Core: оркестратор.** `RunAsync`:
   1. `IBackupConfigStore.LoadAsync`; при `BackupConfigException` — результат с `CriticalErrors`.
   2. `IToolLocator.LocateAsync`; при `ToolUnavailableException` — Critical.
   3. `ITokenProvider.GetTokenAsync`; анонимный режим допустим.
   4. Очистка `GetTemporaryArchiveFolder(config)` через `IFileSystem`: нет папки — пропуск; `IOException`/`UnauthorizedAccessException` — предупреждение в лог, запуск продолжается.
   5. Список работ: репозитории аккаунтов, затем отдельные, в порядке файла.
   6. Для каждого элемента — scope `owner/name`, замер через `TimeProvider`:
      - `Unavailable` → `Skipped`;
      - иначе: состояние → `IChangeDetector.DetectAsync` (детектор сам клонирует и обновляет клон);
      - при `ArchiveNeeded`: HEAD из снимка `Refs`, `IArchiveNameBuilder.Build` → `IArchiver.CreateAsync` → `IBackupStateStore.AppendAsync`;
      - статус: `BranchFallback` → `Warning`; иначе `Archived` или `UpToDate`; `NothingToArchive` → `UpToDate`;
      - изоляция: `catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)` → `Failed`.
   7. Оркестратор сам превращает критические ошибки в результат Critical, чтобы CLI и будущий UI получали код возврата одинаково.
3. **DI** через `AddGitHubBackupCore`; обновить `CoreServiceCollectionExtensionsTests`.
4. **CLI** — пакеты → парсер → handler → renderer → `CliApplication`. Ctrl+C не подключается (фаза 3).
5. **Безопасность (S1/S2)** — в сводку попадает `exception.Message`; токен идёт только через окружение (FR-11.3); проверить, что сообщения не включают окружение процесса.

## 6. Тесты

**Core.Tests → `Runs/BackupOrchestratorTests`** (NSubstitute, `MockFileSystem`, `FakeTimeProvider`):

- архив нужен → архив и запись состояния (поля `ArchiveRequest`, `ArchiveRecord`);
- без изменений; пустой репозиторий → `UpToDate` без архива;
- откат ветки → `Warning` (`[Theory]`: архив нужен / не нужен);
- сбой детектора → `Failed`, следующий обработан, код `1`;
- сбой архиватора → `Failed` без записи состояния; сбой чтения и записи состояния → `Failed`;
- `Unavailable` → `Skipped` без git;
- пустой список → код `0`, `.tmp` очищен;
- неверный конфиг, нет инструмента → Critical, `.tmp` не тронут;
- отмена → исключение, обработка остановлена;
- остатки в `.tmp` удалены до первого репозитория; папки нет → продолжение; файл не удаляется → лог и продолжение;
- анонимный токен; аккаунт и отдельные репозитории в порядке файла; длительность из `TimeProvider`; предупреждения конфига в результате.

**Core.Tests, другие классы:** `BackupRunResultTests` (коды и счётчики), `GitHubRemoteUrlProviderTests`, тесты списка работ, `CoreServiceCollectionExtensionsTests`.

**Infrastructure.IntegrationTests → `Runs/BackupOrchestratorTests`** (реальные `GitClient`, `SevenZipArchiver`, `JsonBackupStateStore`, `JsonBackupConfigStore`; заглушки `IToolLocator`, `ITokenProvider`, `IRemoteUrlProvider` → `LocalGitRemote.Url`; `LocalGitSandbox`):

- полный цикл: `Archived` → `UpToDate` → коммит → `Archived` (два zip, две записи состояния);
- один remote отсутствует → `Failed`, другой архивирован, код `1`;
- остаток в `.tmp` удалён в начале запуска.

**Cli.Tests:** `BackupSummaryRendererTests` (`TestConsole`, эталон в raw string, явная культура), `BackupCommandHandlerTests`, дополнения `CliApplicationTests` (`backup` без конфига → `2`; `--GitHub:Token=…` проходит парсер и не выводится; неизвестная опция → `2`; без команды — прежнее поведение).

## 7. Проверка

| Критерий | Как проверяется |
|---|---|
| 1 | интеграционный `RunAsync_FullCycle_ArchivesThenUpToDateThenArchivesChange` |
| 2 | интеграционный `RunAsync_OneRemoteMissing_ReportsFailedAndArchivesOther`, unit `RunAsync_DetectorFails_ReportsFailedAndProcessesNext`, `BackupSummaryRendererTests` |
| 3 | интеграционный `RunAsync_LeftoverInTemporaryFolder_IsDeletedAtStart`, unit-тест очистки |
| 4 | сборка, тесты, `dotnet format --verify-no-changes`, CI pull request |
| 5 | `BackupSummaryRendererTests` |
| 6 | `BackupRunResultTests`, unit-тесты Critical, `CliApplicationTests` |
| 7–14 | unit-тесты `BackupOrchestratorTests` из §6 |

## 8. Вне рамок

- Обнаружение, маски исключений, wiki — фаза 2.
- Блокировка запуска (NFR-SYS-1), Ctrl+C (NFR-4), история SQLite (FR-8), `--account/--repo/--dry-run/--silent`, интерактивные вопросы, `set-token`, `validate`, код `3` — фаза 3.
- CLI end-to-end через exe на локальных репозиториях: нужна подмена адреса git в конфигурации (новая настройка, изменение PRD). Полный цикл здесь покрывает интеграционный тест оркестратора.
- Статусы `WouldArchive`/`WouldClone` — вместе с dry-run.
- Метрики OpenTelemetry по статусам (FR-10.5).

## 9. Открытые вопросы

1. Очистка `.tmp` без блокировки запуска (блокировка — фаза 3) или mutex в этой issue.
2. Добавить `System.CommandLine` и `Spectre.Console` сейчас или разбирать `backup` вручную.
3. Запуск без команды: прежнее поведение (код `0`) или справка и код `2`.
4. Один статус `Warning` на элемент при откате ветки вместе с новым архивом.
5. Уточнение FR-9.10: ошибка аргументов → `2` (PRD 0.14).
6. Имя `IRemoteUrlProvider` и формат URL с суффиксом `.git`.

## 10. Решения

Приняты на ревью плана 2026-11-11:

1. `.tmp` очищается без блокировки запуска. Блокировка (NFR-SYS-1) остаётся в фазе 3.
2. `System.CommandLine`, `Spectre.Console` и `Spectre.Console.Testing` добавляются в этой issue.
3. Запуск без команды сохраняет прежнее поведение: старт хоста, проверка настроек, код `0`.
4. Один статус на элемент: при откате ветки — `Warning`, имя нового архива при этом указано в сводке.
5. FR-9.10 уточняется в PRD 0.14: ошибка аргументов командной строки → код `2`.
6. Интерфейс называется `IRemoteUrlProvider`, адрес — `https://github.com/<owner>/<name>.git`.

## 11. Задачи

- [x] Согласовать уточнение FR-9.10 (PRD 0.14) и открытые вопросы 1–6
- [x] Core: `BackupItemStatus`, `BackupItemResult`, `BackupRunResult` + тесты
- [x] Core: `IRemoteUrlProvider`, `GitHubRemoteUrlProvider` + тесты
- [x] Core: сборка списка работ из `BackupConfig` + тесты
- [x] Core: `IBackupOrchestrator`, `BackupOrchestrator`, `BackupOrchestratorLog`
- [x] Core: регистрация в `AddGitHubBackupCore`, `CoreServiceCollectionExtensionsTests`
- [x] Core.Tests: `Runs/BackupOrchestratorTests`
- [x] IntegrationTests: `Runs/BackupOrchestratorTests`
- [x] Пакеты `System.CommandLine`, `Spectre.Console`, `Spectre.Console.Testing`, проверка `--vulnerable`
- [x] Cli: парсер с командой `backup`, `Program`
- [x] Cli: `BackupCommandHandler`, `BackupSummaryRenderer`, `CliApplication`, DI хоста
- [x] Cli.Tests: `BackupSummaryRendererTests`, `BackupCommandHandlerTests`, `CliApplicationTests`
- [x] README: команда `backup` и сводка
- [x] Сборка, тесты, формат; проверка новых комментариев
