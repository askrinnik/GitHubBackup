# План: #46 — F0.7: Claude Code hooks for format and build

- Issue: [#46](https://github.com/askrinnik/GitHubBackup/issues/46)
- Линия: Feature (`type:chore`, `area:ai`)
- Сложность: S
- Дата: 2026-11-10
- Источник: `docs/ai/README.md`, разделы «Хуки» и «Отложенные задачи» (issue ссылается на прежний файл `docs/ai-harness.md`)

## 1. Цель

После каждой правки файла `.cs` ассистентом хук сам выравнивает пробелы и отступы по `src/.editorconfig`. Ассистенту не нужно запускать `dotnet format` вручную, а `dotnet format --verify-no-changes` в `build-runner` и в CI реже падает из-за отступов.

## 2. Что уже сделано

- Инфраструктура хуков есть (#65): блок `hooks` в `.claude/settings.json`, папка `.claude/hooks/`, проверочные случаи без Pester, раздел «Хуки» в `docs/ai/README.md`.
- Бенчмарк есть: скилл `_local.harness-quality-check`, фикстура `bench-base`, история в `.ai/benchmarks/harness/run-history.csv`.
- Хуков форматирования и сборки нет. `docs/ai/README.md` прямо называет их отложенной задачей #46.

## 3. Критерии приёмки

Из issue, дословно:
1. Хуки закоммичены в `.claude/settings.json`
2. Хуки описаны в `docs/ai-harness.md` — этот файл заменён на `docs/ai/README.md`; описание идёт туда
3. Прогон бенчмарка `bench-base` не показывает регрессии

Добавлено:
4. Хук форматирует только изменённый файл `.cs` внутри `src/`; другие файлы, файлы вне репозитория и неудачный `dotnet format` не ломают ход (код возврата 0, без `block`)
5. Набор проверочных случаев для скрипта хука проходит

## 4. Подход

- `.claude/hooks/Format-EditedFileHook.ps1` — хук `PostToolUse` для `Edit|Write|MultiEdit`:
  - читает JSON со стандартного ввода, берёт `tool_input.file_path`;
  - выходит сразу, если файл не `.cs`, не существует или лежит вне `<repo>/src`;
  - запускает `dotnet format whitespace src --folder --include <file>` из корня репозитория (`$CLAUDE_PROJECT_DIR` или `cwd`); режим `--folder` не загружает MSBuild, замер — около 0,9 с на файл;
  - ошибку `dotnet format` пишет одной строкой в stderr и всё равно завершается с кодом 0: хук форматирования не блокирует работу.
- Логику выбора файла вынести в функцию модуля (`.claude/hooks/FormatHookRules.psm1`, `Test-FormattableFile`), чтобы проверять её без запуска `dotnet`.
- `.claude/hooks/tests/FormatHookRules.Cases.ps1` — проверочные случаи в стиле `CommitMessageRules.Cases.ps1`: `.cs` в `src` — да; `.xaml`, `.md`, `.cs` вне `src`, путь вне репозитория, пустой путь — нет. Плюс один прогон самого хука на временной копии файла с испорченным отступом.
- `.claude/settings.json` — второй элемент в `PostToolUse` с `matcher: "Edit|Write|MultiEdit"`, `timeout: 30`, `statusMessage`. Правку файла вы подтверждаете сами: автоматический режим блокирует её как самоизменение прав.
- Документы: `docs/ai/README.md` — строка в таблице «Хуки», короткое описание, команда проверочных случаев, убрать «в отложенной задаче #46» и строку #46 из «Отложенных задач»; дерево файлов в начале README, если оно перечисляет скрипты.
- Бенчмарк: прогон `_local.harness-quality-check` с фикстурой `bench-base` после правки `settings.json` и сравнение с последней строкой `run-history.csv`. Хук не меняет стартовый контекст, поэтому регрессии не ожидается.

## 5. Решения

- Только `whitespace`: `dotnet format style` грузит проект (около 4,4 с на Core, дольше на App) на каждую правку; стиль и анализаторы ловят сборка с `TreatWarningsAsErrors` и `build-runner`.
- Без сборки на `Stop`: сборка солюшна стоит десятки секунд на каждый ход и дублирует `build-runner` в `/implement-issue`.
- `.claude/settings.json` правится с подтверждением пользователя.
- После приёмки по решению пользователя в задачу добавлен запуск проверочных случаев хуков в CI: PRD 0.10 (§11), шаг `Hook test cases` в `.github/workflows/ci.yml` для всех `.claude/hooks/tests/*.Cases.ps1`, правило `github-actions.md`, `README.md`, `docs/ai/README.md`.

## 6. Чек-лист

- [x] Модуль правил и проверочные случаи
- [x] Скрипт хука и проверка на временном файле
- [x] Хук подключён в `.claude/settings.json`
- [x] `docs/ai/README.md`
- [x] Прогон `bench-base` без регрессии: рост стартового контекста против прогона `41cb2300` вызван изменениями за месяц, а не хуком
- [x] Проверочные случаи хуков в CI (PRD 0.10)

## Вне рамок

- `dotnet format style` и `analyzers` в хуке.
- Сборка и тесты в хуке `Stop`.
- Форматирование `.xaml`, `.csproj`, `.props` (`dotnet format whitespace --folder` обрабатывает только код).
- Правка текста issue #46 (ссылка на `docs/ai-harness.md`) — после согласия пользователя.
