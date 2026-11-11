# План: F3.2 — Interactive console mode

| | |
|---|---|
| Issue | [#27](https://github.com/askrinnik/GitHubBackup/issues/27) F3.2: Interactive console mode |
| Лейн | Feature |
| Сложность | M |
| Дата | 2026-11-11 |

## 1. Цель

В `backup` без `--silent` консоль задаёт вопросы о каждом новом и переименованном репозитории (FR-9.3, FR-2.6, FR-2.5). Для этого появляется реализация `IDiscoveryDecisionHandler` на Spectre.Console. Пользователь видит данные репозитория по FR-2.6. Он может добавить репозиторий (с правкой ветки и пути), пропустить его или исключить. Для переименованного он выбирает вариант а/б. Если ввод перенаправлен и `--silent` не указан, запуск завершается с кодом `2` и подсказкой использовать `--silent` (FR-9.6, П-7). Признак перенаправления передаётся в логику через DI. С `--silent` всё остаётся как сейчас: `AutomaticDiscoveryDecisionHandler`, вариант «б» (П-1).

## 2. Критерии приёмки

Из issue:

1. Тесты с эмулированным вводом Spectre.Console (`Spectre.Console.Testing`, `TestConsole`).
2. Перенаправленный ввод без `--silent` даёт код 2.
3. Покрыто тестами, CI зелёный.

Добавлено из PRD и сверки с кодом:

- a) (FR-2.6) Для нового репозитория выводится таблица: `owner/name`, URL, owner, видимость, Fork, Archived, ветка по умолчанию, Wiki, аккаунт (или «standalone»); предлагаемая запись: имя, ID, ветка (`(default)` = `null`), итоговая папка архивов.
- b) (FR-2.6) Действия: Add; Add with changes (ввод ветки и пути); Skip this time; Exclude from account (только для репозитория аккаунта); Exclude globally.
- c) (FR-2.5) Переименованный: таблица со старым и новым `owner/name` и обеими папками архивов; выбор «а» `RenameArchiveFolder` или «б» `KeepArchiveFolder`; пометка, что клон переносится в любом варианте. Если папки совпадают, вопрос не задаётся, возвращается `KeepArchiveFolder`.
- d) (FR-2.4) Пропавшие: вопроса нет. `DiscoveryDecisionApplier` помечает их `Unavailable`, `BackupOrchestrator` сообщает о них в сводке. Изменений не нужно.
- e) (FR-9.6) Проверка перенаправления выполняется в `BackupCommandHandler` до `orchestrator.RunAsync`, то есть до обнаружения и любых записей.
- f) (FR-9.5) `--dry-run` вопросов не задаёт; ветка dry-run проверяется первой.
- g) Пустой ввод ветки даёт `Branch = null`. Пустой путь или путь, равный предложенному, даёт `Path = null`.
- h) Пробелы по краям ветки и пути обрезаются; ветка из одних пробелов означает ветку по умолчанию. Недопустимый ввод не принимается, вопрос повторяется: ветка с ведущим `-`; путь не абсолютный, `\\?\`, `\\.\`, с недопустимыми символами. Правила те же, что у `BackupConfigValidator`.
- i) Отмена: `OperationCanceledException` уходит наверх; конфигурация не меняется.
- j) Значения из GitHub и конфигурации выводятся как текст, без разбора разметки Spectre.

## 3. Изменения PRD

Не нужны.

## 4. Затрагиваемые проекты и типы

**GitHubBackup.Core**
- `BackupConfigValidator`: `IsAbsolutePath` становится `public static`; правило ветки выносится в `public static bool IsValidBranch(string)`.

**GitHubBackup.Cli**
- `ConsoleDiscoveryDecisionHandler` (новый, `internal sealed`).
- `CliConsole`: новый параметр `bool IsInputRedirected`.
- `Program`: передаёт `Console.IsInputRedirected`.
- `BackupCommandHandler`: порядок dry-run → FR-9.6 → выбор обработчика по `Silent`.
- `CliServiceCollectionExtensions`: регистрация `ConsoleDiscoveryDecisionHandler`.

**Тесты**
- `TestCliConsole`: `isInputRedirected`, `CreateInteractiveOutput()`.
- `ConsoleDiscoveryDecisionHandlerTests` (новый), `BackupCommandHandlerTests`, `CliApplicationTests`, `BackupConfigValidatorTests`.

## 5. Подход

1. Core: правила пути и ветки становятся публичными; поведение `Validate` не меняется.
2. `CliConsole.IsInputRedirected` — единственное чтение `Console.IsInputRedirected` находится в `Program`.
3. `BackupCommandHandler.RunAsync`: dry-run → если `!Silent && IsInputRedirected`, `BackupRunResult.Stopped([RedirectedInputMessage], [])` (код 2, `Error:` в stderr) → иначе оркестратор с `Silent ? automatic : console`. Текст подсказки: `Input is redirected, so the backup cannot ask questions. Run it again with --silent to decide automatically.`
4. `DecideNewAsync`: проверка отмены; таблица `Field`/`Value` из `Text`-ячеек; `SelectionPrompt` с вариантами; «Add with changes» задаёт ветку и путь через `TextPrompt` с валидацией.
5. `DecideRenameAsync`: при равных папках — строка и `KeepArchiveFolder`; иначе таблица и выбор, первым идёт «б».
6. Путь, прошедший проверку при вводе, но конфликтующий с другой записью, отклоняется при сохранении (`BackupConfigException`, код 2, файл не меняется).

## 6. Тесты

- `ConsoleDiscoveryDecisionHandlerTests`: таблица (точный вывод), standalone, Add, Add with changes (значения, пустой ввод, предложенный путь), недопустимый путь и ветка, Skip, Exclude (Account/Global), скобки в пути, отмена; переименование: вывод, выбор а/б, равные папки, отмена.
- `BackupCommandHandlerTests`: обработчик по `Silent`; перенаправленный ввод без `--silent` → код 2 и подсказка, оркестратор не вызван; с `--silent` — запуск; с `--dry-run` — сообщение dry-run.
- `CliApplicationTests`: сквозной тест — перенаправленный ввод даёт код 2 и подсказку до чтения конфигурации.
- `BackupConfigValidatorTests`: `IsAbsolutePath`, `IsValidBranch`.

## 7. Вне рамок

- Связь Ctrl+C с `CancellationToken` (NFR-4).
- Итоги переименования, добавленные и исключённые репозитории в сводке.
- Общая таблица и действия «для всех» — FR-12.2 (UI).
- Поведение `--dry-run` (FR-9.5) — #29.
- Полная проверка конфликтов пути при вводе.

## 8. Решения

1. FR-9.6 не применяется при `--dry-run`: ветка dry-run идёт первой (FR-9.5 — вопросов нет).
2. FR-9.6 применяется при любом фильтре, включая `--repo`: PRD исключений не делает.
3. Вариант по умолчанию: для нового репозитория — «Add», для переименованного — «б» (как П-1).
4. При вводе проверяются только абсолютность пути и правило ветки; конфликты отклоняются при сохранении.
5. Явно введённая ветка записывается как есть, даже если равна ветке по умолчанию.

## 9. Задачи

- [x] Core: `BackupConfigValidator.IsAbsolutePath` сделать `public static`; вынести `IsValidBranch`; тесты
- [x] Cli: `CliConsole.IsInputRedirected`; `Program` передаёт `Console.IsInputRedirected`
- [x] Cli: `ConsoleDiscoveryDecisionHandler`
- [x] Cli: регистрация в `CliServiceCollectionExtensions`
- [x] Cli: `BackupCommandHandler`: проверка FR-9.6, константа подсказки, выбор обработчика
- [x] Tests: `TestCliConsole`
- [x] Tests: `ConsoleDiscoveryDecisionHandlerTests`
- [x] Tests: `BackupCommandHandlerTests`, `CliApplicationTests`
- [x] `README.md`: описание интерактивного режима и FR-9.6, если затронуто
- [x] Build, тесты, `dotnet format --verify-no-changes`
