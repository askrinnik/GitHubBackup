# План: #1 F0.0: Set up AI harness (Claude Code)

| Поле | Значение |
|---|---|
| Issue | [#1](https://github.com/askrinnik/GitHubBackup/issues/1) |
| Тип | chore (ветка процесса Feature) |
| Сложность | M |
| Дата | 2026-10-03 |

## Цель

Настроить окружение AI-ассистента до начала остальных задач, чтобы вся работа шла по единым правилам (NFR-6, NFR-7). За основу взят харнесс проекта TimeTracker с адаптацией под GitHub и стек из PRD §8. Итоговое описание — [docs/ai-harness.md](../ai-harness.md).

## Критерии приёмки

- [x] `CLAUDE.md` ссылается на `docs/PRD.md` как на главный документ
- [x] Настройки разрешений закоммичены в `.claude/settings.json` (локальные — в `.gitignore`)
- [ ] Рабочий цикл задачи описан и проверен на одном issue — проверка прогоном `/implement-issue 2` после merge этого PR

## Решения (согласованы с пользователем)

- Только Claude Code, без синхронизации с Copilot; тело процесса в `.ai/prompts/`, обёртка в `.claude/commands/`.
- Планы — `docs/plans/`, на русском. Файлы харнесса — на английском.
- GitHub через `gh` CLI, без GitHub MCP. MCP: Context7, Microsoft Learn, NuGet. Playwright и Figma не подключаются.
- Ветка `<n>-<slug>` от `main`, создаётся перед первым коммитом; PR с `Closes #<n>`, merge commit; подпись в коммитах отключена.
- Перед началом задачи — переключение на `main` и pull, если пользователь явно не попросил остаться на текущей ветке.
- NFR-7 (PRD 0.4): заголовки issues, коммиты, PR и комментарии — на английском; заголовки 45 issues переведены.
- Агенты: `issue-planner` (Opus), `issue-developer` (Sonnet, Opus для сложности L), `skill-runner` (Haiku), `architect` и `security-reviewer` (Opus). Ревью кода — встроенные `/code-review` и `/security-review`.
- Учёт часов (report-time), Figma, Azure DevOps, синхронизация с Copilot — не переносятся.
- Механизм следующей задачи: связи «Blocked by» для всех issues и скилл `next-issue`.
- Отложенная работа оформлена issues #46–#49 со связями зависимостей.

## Задачи

- [x] PRD 0.4 (NFR-7), README, перевод заголовков issues
- [x] Новые issues #46–#49, связи «Blocked by» для всех issues, #37 зависит от #49
- [x] Каркас: `CLAUDE.md`, `.claude/settings.json`, `.mcp.json`, `.gitignore`
- [x] Правила `.claude/rules/`
- [x] Агенты и скиллы
- [x] `implement-issue` и `next-issue`
- [x] Бенчмарк: скрипты, фикстуры `bench-base` и `quality-prd-first`, README
- [x] `docs/ai-harness.md`
- [ ] Базовый прогон бенчмарка
- [ ] PR, merge, проверка цикла на #2
