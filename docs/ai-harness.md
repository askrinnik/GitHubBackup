# AI Harness проекта GitHubBackup

Документ описывает окружение AI-ассистента (Claude Code), по которому ведётся разработка: из чего оно состоит, как устроен рабочий цикл задачи, почему выбраны те или иные модели и как перенести харнесс в другой проект.

Харнесс построен на основе харнесса проекта TimeTracker и адаптирован: GitHub вместо Azure DevOps, стек .NET 10 + WPF + CLI вместо Blazor, только Claude Code (без синхронизации с GitHub Copilot).

## Состав

```
CLAUDE.md                          главные инструкции, загружаются в каждую сессию
.mcp.json                          MCP-серверы проекта
.claude/
  settings.json                    разрешения, MCP, отключённая подпись в коммитах
  settings.local.json              личные настройки (не в git)
  commands/                        implement-issue, next-issue
  agents/                          issue-planner, issue-developer, skill-runner, architect, security-reviewer
  rules/                           правила, которые подгружаются по путям файлов
  skills/                          _local.* (свои) и сторонние скиллы
.ai/
  prompts/implement-issue.md       тело рабочего цикла
  benchmarks/harness/              бенчмарк харнесса
docs/
  PRD.md                           требования (источник истины)
  plans/                           планы по issues
  ai-harness.md                    этот документ
```

Задел на будущую поддержку GitHub Copilot: тело рабочего цикла лежит отдельно от обёртки команды (`.ai/prompts/` и `.claude/commands/`), поэтому для Copilot достаточно добавить ещё одну тонкую обёртку. Скиллы в `.claude/skills/` и `CLAUDE.md` Copilot в VS Code тоже умеет читать (проверить при подключении).

## Языки

Файлы харнесса (`CLAUDE.md`, `.claude/**`, `.ai/**`) — на английском: они загружаются в контекст сессий, а кириллица занимает в 1,5–2,5 раза больше токенов. Заголовки issues, коммиты, PR и комментарии к ним — на английском (NFR-7). Документация в `docs/`, планы и тексты issues — на русском.

## Рабочий цикл задачи: `/implement-issue <n>`

Тело — `.ai/prompts/implement-issue.md`. Кратко:

| Шаг | Что происходит |
|---|---|
| 0 | Переключение на `main` и `git pull --ff-only`. При незакоммиченных изменениях — остановка и вопрос. Остаться на текущей ветке можно, только явно сказав об этом (`/implement-issue 5 stay on current branch`) |
| 1 | Чтение issue через `gh`. Без номера — рекомендация следующей задачи (`next-issue`) |
| 2 | Проверка зависимостей (открытый блокирующий issue — остановка), выбор ветки процесса по метке (`bug` → Bug, остальные → Feature), назначение на себя |
| 3 | Чтение только связанных разделов PRD. Bug: воспроизведение падающим тестом. Feature: список приёмки. **PRD-first:** если задача требует поведения, которого нет в PRD, — сначала предложение правки PRD |
| 4–7 | План от агента `issue-planner` (на русском, с оценкой сложности S/M/L), сохранение в `docs/plans/<type>-<n>-<slug>.md`, ревью с пользователем |
| 8 | Реализация агентом `issue-developer`: модель выбирается по сложности (S/M — Sonnet, L — Opus), тесты обязательны |
| 9 | Полная пересборка, тесты, `dotnet format --verify-no-changes`, проверка добавленных комментариев, `security-reviewer` для изменений в Infrastructure и CLI |
| 10–11 | Проверка каждого пункта приёмки, подтверждение пользователя |
| 12 | Ветка `<n>-<slug>` создаётся прямо перед коммитом от свежего `main`; коммит и push — с подтверждением |
| 13 | Комментарий в issue, отметка проверенных пунктов приёмки в теле issue, PR с `Closes #<n>`, одна проверка CI |
| 14 | Рекомендация следующей задачи: «смержите PR → новая сессия → `/implement-issue N`» |

Экономия контекста встроена в процесс: компактификация на трёх этапах с готовой командой `/compact keep: …`, ограничение на длину хода, «одна задача — одна сессия», узкое чтение, делегирование шумной работы агентам.

## Порядок задач: `/next-issue`

Зависимости между issues хранятся дважды: связи GitHub «Blocked by» (для машины) и раздел `## Зависимости` в тексте issue (для человека). При создании issue заполняются оба.

Скрипт `.claude/skills/_local.next-issue/scripts/Get-NextIssue.ps1` одним GraphQL-запросом получает открытые issues и выбирает готовые: без открытого PR и со всеми закрытыми блокирующими задачами. Порядок — по F-коду в заголовке (фаза, затем номер). Параметр `-AssumeClosed <n>` считает задачу закрытой: так `implement-issue` рекомендует следующую задачу до merge текущего PR.

## Агенты и модели

| Агент | Модель | Назначение и причина выбора модели |
|---|---|---|
| `issue-planner` | Opus | Только чтение, возвращает план. Ошибка плана стоит дороже всего, а объём вывода мал |
| `issue-developer` | Sonnet по умолчанию, Opus для сложности L | Самый «дорогой» по токенам агент (чтение, сборки, итерации); по согласованному детальному плану Sonnet обычно достаточно |
| `skill-runner` | Haiku | Пишет по шаблону тексты коммитов, комментариев и PR (на английском) |
| `architect` | Opus | Проектные вопросы: границы слоёв, отказоустойчивость, отмена |
| `security-reviewer` | Opus | Риски приложения: утечка токена, инъекция аргументов git, выход за пределы папок, архивы |

Цены за 1M токенов (вход/выход) на момент настройки: Opus 5.5 — $4/$20, Sonnet 5.5 — $2/$10, Haiku 4.5 — $1/$5, Fable 5.1 — $10/$50. По замерам TimeTracker около 88% затрат приходится на основную сессию с растущим контекстом, поэтому дисциплина контекста важнее выбора модели субагента.

Ревью кода — встроенные `/code-review` и `/security-review`; они читают правила проекта.

## Правила (`.claude/rules/`)

Правило подгружается в контекст, когда ассистент читает файл, подходящий под его `paths`.

| Правило | Пути | Содержание |
|---|---|---|
| `csharp.md` | `**/*.cs` | Слои, DI и options, TimeProvider и IFileSystem, async и отмена, ошибки, логирование, гигиена комментариев, стиль C# 14 |
| `tests.md` | тестовые проекты | Слои тестов, xUnit v3, Shouldly, NSubstitute, Verify, WireMock, FlaUI, детерминированность |
| `wpf-mvvm.md` | `GitHubBackup.App/**`, `*.xaml` | MVVM на CommunityToolkit.Mvvm, команды с отменой, виртуализация, AutomationId |
| `cli.md` | `GitHubBackup.Cli/**` | System.CommandLine, Spectre.Console, коды возврата, `--silent`, `--dry-run` |
| `external-processes.md` | `GitHubBackup.Infrastructure/**` | Запуск git и 7-Zip, передача токена через окружение, коды возврата, логирование вывода, отмена |
| `security.md` | все основные проекты | Каталог рисков с идентификаторами S/I/F/P/D и уровнями серьёзности |
| `msbuild.md` | `*.csproj`, `Directory.*.props`, `.slnx` | Общие свойства, Central Package Management, ссылки по слоям |
| `github-actions.md` | `.github/workflows/**` | Закреплённые версии actions, минимальные права, секреты, кэш |
| `update-docs-on-code-change.md` | CLI, настройки, файлы харнесса | Какой документ обновлять при каком изменении; PRD-first |
| `docs.md` | `docs/**`, `README.md` | Русский язык, правила правки PRD, формат планов |

## Скиллы (`.claude/skills/`)

Свои (`_local.*`):

| Скилл | Назначение |
|---|---|
| `_local.git-commit` | Ветки `<n>-<slug>`, формат коммита `#<n> <title>` + пункты, без подписи |
| `_local.pull-request` | PR в `main`, `Closes #<n>`, содержание описания, однократная проверка CI |
| `_local.post-issue-comment` | Комментарий в issue: RCA для бага, итоги реализации для остальных |
| `_local.write-tests` | Выбор слоя тестов, регрессионный тест сначала для бага |
| `_local.debug-issue` | Воспроизведение, разбор логов, поиск причины |
| `_local.refactor-code` | Рефакторинг без изменения поведения |
| `_local.nuget-package-update` | Обновление пакетов по семействам с проверкой сборкой и тестами |
| `_local.next-issue` | Рекомендация следующей задачи |
| `_local.harness-quality-check` | Бенчмарк харнесса (только ручной запуск) |

Сторонние (скопированы как есть, лицензия MIT; при обновлении — заменить папку целиком):

| Скилл | Источник |
|---|---|
| `mvvm-toolkit`, `mvvm-toolkit-di`, `mvvm-toolkit-messenger`, `system-commandline-cli` | [github/awesome-copilot](https://github.com/github/awesome-copilot/tree/main/skills) |
| `run-tests`, `test-anti-patterns`, `coverage-analysis` | [dotnet/skills](https://github.com/dotnet/skills), плагин `dotnet-test` |
| `directory-build-organization` | [dotnet/skills](https://github.com/dotnet/skills), плагин `dotnet-msbuild` |
| `dotnet-pinvoke` | [dotnet/skills](https://github.com/dotnet/skills), плагин `dotnet-advanced` (для Credential Manager) |
| `csharp-async`, `csharp-docs`, `ef-core`, `microsoft-docs` | из харнесса TimeTracker (исходно github/awesome-copilot) |

Правила `wpf-mvvm.md` и `github-actions.md` основаны на инструкциях `dotnet-wpf`, `mvvm-toolkit` и `github-actions-ci-cd-best-practices` из github/awesome-copilot.

## MCP-серверы (`.mcp.json`)

| Сервер | Назначение |
|---|---|
| `context7` | Документация сторонних библиотек (Octokit, Serilog, Spectre.Console, Verify, WireMock…) |
| `microsoft-learn` | Официальная документация Microsoft (.NET, WPF, System.CommandLine, EF Core, MSBuild) |
| `nuget` | Версии, уязвимости и сведения о пакетах NuGet; запускается через `dnx` из .NET 10 SDK |

Не подключены сознательно: GitHub MCP (всё нужное делает `gh` без дополнительного токена и без десятков инструментов в контексте), Playwright (только браузер, для WPF бесполезен), Figma. MCP для автоматизации WPF выбирается в #49.

## Разрешения (`.claude/settings.json`)

- Без вопросов: сборка, тесты, `dotnet format`, чтение через git и `gh`, инструменты MCP-серверов, скрипты скиллов `_local.*`, `gh api graphql`, локальные `git add`, `git switch`, `git pull --ff-only`, а также `git commit`, `git push`, `gh issue comment`, `gh issue edit`, `gh pr create`. Для последних пяти подтверждение даёт пользователь в чате: шлюзы процесса `implement-issue` требуют явного «да» перед каждым из этих действий, поэтому системный запрос не дублирует его.
- С системным вопросом: `gh pr merge`, `gh pr comment`, `gh pr edit`, `gh issue create`, `gh issue close`, REST-вызовы `gh api` (`repos/…`, `-X`, `--method`) — действия, которые трудно отменить или которые меняют чужие данные.
- Запрещено: `git push --force`, `git reset --hard`, `git clean`, `rm -rf`, чтение `.env`.
- Подпись `Co-Authored-By` в коммитах и PR отключена.
- Правило «спрашивать» сильнее правила «разрешено», поэтому разрешение для узкого шаблона не работает, если более широкий шаблон стоит в «спрашивать».
- Составные команды (`cd … &&`, конвейеры, циклы) нельзя разрешить навсегда, поэтому `CLAUDE.md` требует простых команд из корня репозитория.
- Разрешения меняет только пользователь: правку `.claude/settings.json` ассистентом автоматический режим блокирует как самоизменение прав.

## Бенчмарк харнесса

Скилл `_local.harness-quality-check` запускает фиксированные тестовые сессии (`claude -p`) и записывает метрики: контекст в начале, токены от чтения файла, загруженные правила, оценку судьи по эталону. История — `.ai/benchmarks/harness/run-history.csv`, отчёты — `.ai/benchmarks/harness/reports/`. Запускать после изменения `CLAUDE.md`, правил, скиллов, агентов, MCP или настроек.

Сейчас фикстуры: `bench-base` (стартовый контекст) и `quality-prd-first` (соблюдение правила PRD-first). Фикстуры, которым нужен код, — в отложенных задачах.

## Отложенные задачи

| Issue | Когда | Что |
|---|---|---|
| [#46](https://github.com/askrinnik/GitHubBackup/issues/46) F0.7 | после #2 | Хуки Claude Code: форматирование и сборка после правок |
| [#47](https://github.com/askrinnik/GitHubBackup/issues/47) F1.13 | после #10, #13 | Фикстуры для кода: `bench-cs`, `bench-test`, `quality-service-convention`, `quality-security` |
| [#48](https://github.com/askrinnik/GitHubBackup/issues/48) F3.11 | после #26 | Фикстура `quality-docs-sync` |
| [#49](https://github.com/askrinnik/GitHubBackup/issues/49) F4.8 | после #36, до #37 | MCP для автоматизации WPF, фикстура `bench-xaml` |

## Перенос в другой проект

Без изменений переносятся:
- механизм `implement-issue` (шаги, контрольные точки, экономия контекста);
- агенты `issue-planner`, `issue-developer`, `skill-runner`;
- скиллы `_local.git-commit`, `_local.pull-request`, `_local.post-issue-comment`, `_local.next-issue` — для любого репозитория на GitHub;
- гигиена комментариев (`csharp.md`);
- бенчмарк (`_local.harness-quality-check` и `bench-base`).

Нужно переписать под проект:
- `CLAUDE.md`;
- правила по стеку (`wpf-mvvm.md`, `cli.md`, `external-processes.md`, `security.md`, раздел «GitHubBackup patterns» в `csharp.md`);
- семейства пакетов в `_local.nuget-package-update`;
- эталоны фикстур `quality-*`.

Условия для переноса: PRD-документ с идентификаторами требований, issues с разделами «Источник требований», «Критерии приёмки» и «Зависимости», F-коды в заголовках и связи «Blocked by».

## Сопровождение

При изменении `CLAUDE.md`, `.claude/**`, `.ai/**` или `.mcp.json` этот документ обновляется в том же изменении (правило `update-docs-on-code-change.md`), после чего желательно запустить бенчмарк.
