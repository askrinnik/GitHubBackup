# План: #98 F3.13: Move the issue workflow to the issue-kit plugin

| Поле | Значение |
|---|---|
| Issue | [#98](https://github.com/askrinnik/GitHubBackup/issues/98) |
| Lane | Feature (метка `type:chore`) |
| Сложность | M |
| Дата | 2026-11-13 |

## Цель

Харнес репозитория берёт рабочий цикл задачи из плагина `issue-kit`, а бенчмарк — из плагина `harness-kit` маркетплейса `askrinnik/ai-plugins`. Локальные копии этих частей удалены. Проектные агенты `architect` и `security-reviewer`, скиллы `debug-issue`, `write-tests`, `refactor-code`, `nuget-package-update`, правила `.claude/rules/` и хук форматирования остаются в репозитории.

## Список приёмки

1. `issue-kit:init-config check` не находит ошибок в `issue-kit.config.json`.
2. Локальные копии удалены; `/issue-kit:implement-issue` и `/issue-kit:next-issue` работают в репозитории.
3. Хук проверки сообщения коммита из плагина срабатывает на коммите этой задачи.
4. Замеры M1 и M2 записаны в `run-history.csv`, отчёты закоммичены; оценки в пределах шума относительно M0.
5. `docs/ai/README.md` описывает харнес на плагинах и ссылается на документы команд в плагине; `docs/ai/implement-issue.md` и `docs/ai/implement-issues.md` удалены.
6. (добавлено) Хук проверки коммита запускается один раз: локальная запись в `.claude/settings.json` удалена.
7. (добавлено) Нигде в действующих файлах харнеса и документации не осталось ссылок на удалённые файлы. Старые планы в `docs/plans/` и записи бенчмарка не правятся: это история.

M3 — профиль первой задачи после слияния, вне рамок этой задачи.

## Изменения

### Новый файл `issue-kit.config.json` (корень)

Черновик из пробного прогона плагина без изменений: команды сборки, тестов и формата из `CLAUDE.md`; `prd.path` `docs/PRD.md` и `idPattern` для `FR-x.y`/`NFR-x`; `plans.language` `Russian`; русские заголовки разделов issue; `ordering.titleCodePattern` `^F(\d+)\.(\d+)`; `roles` — `architect`, `security-reviewer`, `debug-issue`, `write-tests`; триггеры security review как в старом шаге 9; `commentReview.globs` `*.cs`, `*.xaml`. Остальное совпадает с умолчаниями плагина (`main`, `{n}-{slug}`, `docs/plans`, `{type}-{n}-{slug}.md`, lane-метки `bug` и `type:test`). Планировщик и разработчик — агенты плагина.

### `.claude/settings.json`

- `extraKnownMarketplaces.ai-plugins` с источником GitHub `askrinnik/ai-plugins`.
- `enabledPlugins`: `issue-kit@ai-plugins`, `harness-kit@ai-plugins`.
- Удалить из `hooks.PostToolUse` запись `Test-CommitMessageHook.ps1`; хук форматирования остаётся.
- Правила `Bash/PowerShell(pwsh -NoProfile -File .claude/skills/_local.*)` заменить на `…*issue-kit*` и `…*harness-kit*` (скрипты плагинов) и узкое `….claude/skills/_local.nuget-package-update/*` (скрипт этого скилла остаётся).

### Удаление

- `.claude/commands/` (`implement-issue.md`, `implement-issues.md`, `next-issue.md`), `.ai/prompts/` (`implement-issue.md`, `implement-issues.md`).
- Скиллы `_local.git-commit`, `_local.open-pr`, `_local.github-issue`, `_local.next-issue`, `_local.harness-quality-check` (с их скриптами).
- Агенты `issue-planner`, `issue-developer`, `build-runner`, `skill-runner`.
- `.claude/hooks/Test-CommitMessageHook.ps1`, `.claude/hooks/CommitMessageRules.psm1`, `.claude/hooks/tests/CommitMessageRules.Cases.ps1`. Шаг CI `Hook test cases` запускает `*.Cases.ps1` по маске и продолжает работать с `FormatHookRules.Cases.ps1`.

### `profile-session.config.json`

- `workItemCommandPattern`: `/issue-kit:implement-issues?`.
- `activityRules`: агенты и скиллы с префиксом — `^Agent:issue-kit:skill-runner|^Skill:issue-kit:(git-commit|open-pr|github-issue)`, `^Agent:issue-kit:build-runner`, `^Agent:issue-kit:issue-developer`, `^Agent:(issue-kit:issue-planner|architect)`. Правила `e2e` и `review` без изменений. `excludeSessionManifests` и `lanePlanGlob` остаются (по `lanePlanGlob` профиль находит lane старых сессий).

### Документация и инструкции

- `CLAUDE.md`: раздел *Source of truth* — документ харнеса только `docs/ai/README.md`, документы команд в плагине; *Repository layout* — `issue-kit.config.json` вместо `.ai/prompts/`; *Git and GitHub* — скиллы `issue-kit:git-commit`, `issue-kit:open-pr`, `issue-kit:github-issue`, хук плагина, команды `/issue-kit:implement-issue`, `/issue-kit:implement-issues`, `/issue-kit:next-issue`, ссылка на шаг 0 скилла плагина; *Working style* — делегирование агентам `issue-kit:*`; *Custom agents* — только `architect` и `security-reviewer`, плюс одна строка о агентах плагина.
- `docs/ai/README.md`: остаются только части харнеса, которые принадлежат репозиторию: состав, языки, агенты `architect` и `security-reviewer`, правила, скиллы, MCP, разрешения, хук форматирования, бенчмарк (фикстуры), профиль сессий, отложенные задачи. Рабочие циклы — таблица со ссылками на документы плагина и абзац о проектных ролях и `issue-kit.config.json`. Описание локального `Get-NextIssue.ps1`, агентов плагина и хука проверки коммита уходит. «Перенос в другой проект» — три шага: плагины, `/issue-kit:init-config`, проектные правила и фикстуры.
- `docs/ai/implement-issue.md`, `docs/ai/implement-issues.md`: удалить. Схемы шагов и диаграммы — в плагине, значения репозитория — в `issue-kit.config.json` с описанием ключей в `CONFIGURATION.md` плагина.
- `.claude/rules/docs.md`: строка про `docs/ai/<command>.md` — документ ведётся только для локального рабочего цикла.
- `.claude/rules/update-docs-on-code-change.md`: строка про тело команды `.ai/prompts/<command>.md` и `docs/ai/<command>.md` — заменить: изменение `issue-kit.config.json` или ролей обновляет `docs/ai/README.md`.
- `.ai/benchmarks/harness/README.md`: запуск через `harness-kit:harness-quality-check` и путь к `Run-Harness.ps1` плагина.

## Проверка

| Пункт | Как |
|---|---|
| 1 | `issue-kit:init-config check` (скрипт `Test-IssueKitConfig.ps1`) — 0 ошибок |
| 2 | `git ls-files` не содержит удалённых путей; `issue-kit:next-issue` выполняется в репозитории; `/issue-kit:implement-issue` — этот цикл ведётся по тому же порядку; полная проверка — M3 |
| 3 | Коммит задачи на ветке `98-move-issue-workflow-to-issue-kit`; вывод хука плагина или ручной запуск `Test-CommitMessageHook.ps1` плагина на этом коммите |
| 4 | M2 через `Run-Harness.ps1` из `harness-kit`; сравнение с M0 и M1 по `quality-*` и `FirstCtx` с объяснением по областям |
| 5, 7 | `git grep` по удалённым именам в действующих файлах |
| 6 | Ручной просмотр `.claude/settings.json` |

Сборка и тесты (`build-runner` плагина) запускаются, хотя C# не меняется: так требует workflow. Security review не нужен: изменение не касается `Infrastructure`, `Cli`, CI и пакетов.

## Вне рамок

- M3 (первая задача после слияния).
- Правка старых планов в `docs/plans/` и записей бенчмарка.
- Изменения в репозитории `ai-plugins`.

## Риски

- Маркетплейс `ai-plugins` у пользователя зарегистрирован из локальной папки. Запись в проектных настройках с тем же именем, но источником GitHub, может перекрыть локальную регистрацию. Проверить после правки: `claude plugin marketplace list`.
- Агенты плагина не имеют MCP-инструментов `context7` и `microsoft-learn`, которые были у локальных `issue-planner` и `issue-developer`. Если это станет мешать, проектный агент указывается в `roles.planner` или `roles.developer`.
- Автоматический режим может заблокировать правку `.claude/settings.json` как изменение прав; тогда правку вносит пользователь.

## Решения

1. Схемы шагов и диаграммы `implement-issue` и `implement-issues` переносятся в плагин: [askrinnik/ai-plugins#22](https://github.com/askrinnik/ai-plugins/issues/22). Работа над #98 продолжается после слияния #22. #22 слит.
2. `docs/ai/implement-issue.md` и `docs/ai/implement-issues.md` удаляются, а не сокращаются: схемы есть в плагине, значения — в `issue-kit.config.json`. `docs/ai/README.md` остаётся как справочник по частям харнеса, которые принадлежат репозиторию. Тело #98 обновлено, в зависимости добавлен ai-plugins#22.

## Чек-лист

- [x] `issue-kit.config.json`
- [x] `.claude/settings.json`
- [x] Удаление локальных копий
- [x] `profile-session.config.json`
- [x] `CLAUDE.md`, `docs/ai/*`, `update-docs-on-code-change.md`, `.ai/benchmarks/harness/README.md`
- [x] `init-config check`, сборка и тесты
- [ ] Коммит, хук плагина
- [ ] M2 и сравнение с M0/M1
