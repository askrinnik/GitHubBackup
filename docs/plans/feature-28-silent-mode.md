# План: #28 — F3.3: Silent mode `--silent`

| | |
|---|---|
| Issue | [#28 F3.3: Silent mode --silent](https://github.com/askrinnik/GitHubBackup/issues/28) |
| Лейн | Feature |
| Сложность | S |
| Дата | 2026-11-12 |

## 0. Что уже сделано (сверка с кодом)

Политика тихого режима уже реализована в #25, #26 и #27. В #28 почти не остаётся production-кода:

| Требование | Где реализовано |
|---|---|
| Новые: добавить с веткой и путём по умолчанию, бэкап в этом же запуске | `AutomaticDiscoveryDecisionHandler.DecideNewAsync` → `NewRepositoryDecision.Add()`; `BackupOrchestrator` бэкапит добавленные |
| Пропавшие: `Unavailable` | `DiscoveryDecisionApplier` (обработчик не участвует) |
| Переименованные: вариант «б», старый путь пишется в `path` (П-1) | `AutomaticDiscoveryDecisionHandler.DecideRenameAsync` → `RenameDecision.KeepArchiveFolder`; запись `path` в `DiscoveryDecisionApplier`; клон переносится всегда (П-12, `CloneRelocator`) |
| Выбор обработчика по `--silent` | `BackupCommandHandler.RunAsync` |
| FR-9.6 (перенаправленный ввод без `--silent` → `2`) | `BackupCommandHandler.RunAsync` |
| Код `3` | `BackupRunResult.ExitCode`; тесты в `BackupRunResultTests`, `BackupOrchestratorTests`, `BackupCommandHandlerTests` |
| DI | `CoreServiceCollectionExtensions` регистрирует `AutomaticDiscoveryDecisionHandler` |
| README | Тихий режим и код `3` уже описаны |

Пробелы:

1. Нет теста на первый критерий приёмки: в тихом режиме нет обращений к консольному вводу.
2. Нет сквозного теста политики `--silent` для пропавшего и переименованного репозитория.
3. Запись решений в историю как автоматических (FR-8.4) требует хранилища истории, которого ещё нет (см. *Решения*).

## 1. Цель

В `backup --silent` нет вопросов, все решения принимаются автоматически (FR-9.4). Новые репозитории добавляются с веткой и путём по умолчанию и бэкапятся. Пропавшие получают `Unavailable` (FR-2.4). Для переименованных выбирается вариант «б» из FR-2.5 (П-1), клон переносится под новое имя (П-12). Если такие события были и ошибок нет, код возврата `3` (FR-9.10). Поведение уже реализовано, поэтому #28 закрывает его тестами по критериям приёмки.

## 2. Критерии приёмки

Из issue:

1. Тест: в тихом режиме нет обращений к консольному вводу.
2. Код возврата 3 при событиях обнаружения.
3. Покрыто тестами, CI зелёный.

Добавлено при сверке с кодом:

- a) (FR-9.4, П-1, П-12) Сквозной тест с переименованным репозиторием в тихом режиме: новое имя и `path` = старая папка в конфиге, новый архив в старой папке, клон под новым именем, код `3`.
- b) (FR-9.4, FR-2.4) Сквозной тест с пропавшим репозиторием в тихом режиме: `Unavailable`, элемент `Skipped`, клон и архивы на месте, код `3`.
- c) (FR-9.6) Тихий режим с перенаправленным вводом решает без вопросов (уже есть тест; новый тест из п. 1 тоже его проверяет).
- d) (FR-2.7) `--repo` без обнаружения и `--account` уже покрыты, изменений нет.
- e) Приоритет кодов `1` и `2` над `3` уже покрыт в `BackupRunResultTests`.

## 3. Изменения PRD

Не нужны.

## 4. Затрагиваемые проекты и типы

Production-код не меняется.

- `GitHubBackup.Cli.Tests` / `BackupCommandHandlerTests`: новый тест на решения без консольного ввода; перегрузка `CreateHandler` с выводом `IAnsiConsole` (интерактивный `TestConsole` с пустой очередью ввода).
- `GitHubBackup.Infrastructure.IntegrationTests` / `Runs/BackupOrchestratorTests`: два новых теста (переименование и пропажа); при необходимости `FakeGitHubApi.RemoveRepository`.

## 5. Подход

1. **Тест «нет консольного ввода».** Подставной `IBackupOrchestrator` берёт `BackupRunRequest.DecisionHandler` и вызывает `DecideNewAsync` и `DecideRenameAsync`, как настоящий оркестратор, затем возвращает результат с событиями обнаружения. Консоль интерактивна, но без строк ввода: любой вопрос бросил бы исключение. Проверки: решения `Add` и `KeepArchiveFolder`, код `ExitCode.DiscoveryEvents`, в выводе нет вопросов. Тест параметризуется `isInputRedirected`.
2. **Интеграционные тесты политики** через реальные `DiscoveryDecisionApplier`, `CloneRelocator`, `JsonBackupConfigStore`, git и 7-Zip.
3. Сборка, тесты, `dotnet format --verify-no-changes`.

## 6. Тесты

- `BackupCommandHandlerTests.RunAsync_SilentWithDiscoveredRepositories_DecidesWithoutReadingConsoleInput(bool isInputRedirected)`.
- `Runs/BackupOrchestratorTests.RunAsync_RenamedRepositorySilent_KeepsArchiveFolderAndMovesClone`.
- `Runs/BackupOrchestratorTests.RunAsync_MissingRepositorySilent_MarksUnavailableAndKeepsFiles`.

## 7. Вне рамок

- Запись событий обнаружения и автора решения в историю SQLite (FR-8.4) — #33 («события обнаружения с решением и его автором»).
- `--dry-run` (FR-9.5) — #29.
- Сводка и коды возврата в сводке — #30.
- Ctrl+C (NFR-4), блокировка запуска.

## 8. Решения

- Пункт «решения записываются в историю как автоматические» отложен до #33: хранилища истории нет, а #33 уже включает «события обнаружения с решением и его автором». Спекулятивный контракт (`DiscoveryDecisionSource`) без потребителя не вводится. Альтернатива: заложить перечисление и свойство в `IDiscoveryDecisionHandler` сейчас.
- Критерий 1 проверяется на уровне `BackupCommandHandler`, а не сквозным тестом `CliApplication`: у `CliApplicationTests` нет подмены `IGitHubApi`. Сквозная политика проверяется интеграционными тестами оркестратора.

## 9. Задачи

- [x] `BackupCommandHandlerTests`: перегрузка `CreateHandler` с выводом; тест `RunAsync_SilentWithDiscoveredRepositories_DecidesWithoutReadingConsoleInput`
- [x] `BackupOrchestratorTests` (интеграционные): `RunAsync_RenamedRepositorySilent_KeepsArchiveFolderAndMovesClone`
- [x] `BackupOrchestratorTests` (интеграционные): `RunAsync_MissingRepositorySilent_MarksUnavailableAndKeepsFiles`
- [x] Проверить, что `README.md` по-прежнему верен
- [ ] Build, тесты, `dotnet format --verify-no-changes`
