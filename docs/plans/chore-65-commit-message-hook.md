# План: #65 — F0.8: Hook that checks commit messages against the git-commit skill

- Issue: [#65](https://github.com/askrinnik/GitHubBackup/issues/65)
- Линия: Feature (`type:chore`, `area:ai`)
- Сложность: S
- Дата: 2026-11-10
- Источник: `docs/ai/README.md`, скилл `_local.git-commit`

## 1. Цель

Сообщение каждого нового коммита проверяется автоматически по правилам скилла `git-commit`. Ассистент получает список ошибок сразу после коммита, до push. Workflow и скилл явно требуют загрузить скилл перед коммитом без `skill-runner`.

## 2. Критерии приёмки

Из issue, дословно:
1. Хук подключён в `.claude/settings.json` и срабатывает после `git commit` из Bash и PowerShell
2. Неверная первая строка, пустая строка в списке, строка без `- ` и трейлер дают ошибку с понятным текстом
3. Верное сообщение, merge-коммит и неудачная команда `git commit` не дают ошибки
4. Набор проверочных случаев для скрипта хука проходит
5. `/implement-issue`, `/implement-issues` и скилл `git-commit` дополнены, документы в `docs/ai/` обновлены

## 3. Подход

- `.claude/hooks/CommitMessageRules.psm1` — правила: распознавание команды `git commit`, вид ветки (`<n>-<slug>`, `<first>-<last>-<slug>`, `main`, другая), проверка сообщения. Заголовок issue передаётся функцией, поэтому правила проверяются без сети.
- `.claude/hooks/Test-CommitMessageHook.ps1` — хук `PostToolUse` для `Bash|PowerShell`: читает JSON со стандартного ввода, для `git commit` берёт последний коммит, заголовок issue через `gh`, при нарушении печатает `{"decision":"block","reason":…}`. Пропускает merge-коммиты, отсоединённый HEAD и коммит старше двух минут (команда не удалась).
- `.claude/hooks/tests/CommitMessageRules.Cases.ps1` — проверочные случаи без Pester (в системе только Pester 3.4); код возврата 1 при сбое.
- `.claude/settings.json` — блок `hooks`.
- Документы: `.ai/prompts/implement-issue.md` (шаг 12), `.ai/prompts/implement-issues.md`, скилл `git-commit` (несколько коммитов на issue, ссылка на хук), `CLAUDE.md` (*Git and GitHub*), `docs/ai/README.md` (раздел «Хуки», состав, перенос), `docs/ai/implement-issue.md`, `docs/ai/implement-issues.md`.

## 4. Чек-лист

- [x] Правила и проверочные случаи
- [x] Скрипт хука и проверка на временном репозитории
- [x] Хук подключён в `.claude/settings.json`
- [x] Workflow, скилл, `CLAUDE.md`, документы `docs/ai/`

## Вне рамок

- Хуки форматирования и сборки — #46.
- Исправление сообщений коммитов #63 (PR #64 уже смержен).
- Git-хук `commit-msg` для коммитов вне Claude Code.
