# AI Harness проекта GitHubBackup

Документ описывает окружение AI-ассистента (Claude Code), по которому ведётся разработка: из чего оно состоит, как устроены рабочие циклы, почему выбраны те или иные модели и как перенести харнесс в другой проект.

Харнесс построен на основе харнесса проекта TimeTracker и адаптирован: GitHub вместо Azure DevOps, стек .NET 10 + WPF + CLI вместо Blazor, только Claude Code (без синхронизации с GitHub Copilot).

## Состав

```
CLAUDE.md                          главные инструкции, загружаются в каждую сессию
.mcp.json                          MCP-серверы проекта
.claude/
  settings.json                    разрешения, MCP, хуки, отключённая подпись в коммитах
  hooks/                           скрипты хуков и их проверочные случаи
  settings.local.json              личные настройки (не в git)
  commands/                        implement-issue, implement-issues, next-issue
  agents/                          issue-planner, issue-developer, build-runner, skill-runner, architect, security-reviewer
  rules/                           правила, которые подгружаются по путям файлов
  skills/                          _local.* (свои) и сторонние скиллы
.ai/
  prompts/implement-issue.md       тело рабочего цикла
  prompts/implement-issues.md      тело пакетного цикла (несколько задач, одна ветка)
  benchmarks/harness/              бенчмарк харнесса
docs/
  PRD.md                           требования (источник истины)
  plans/                           планы по issues
  ai/                              хаб харнесса (README.md) и по документу на каждую команду
```

Задел на будущую поддержку GitHub Copilot: тело рабочего цикла лежит отдельно от обёртки команды (`.ai/prompts/` и `.claude/commands/`), поэтому для Copilot достаточно добавить ещё одну тонкую обёртку. Скиллы в `.claude/skills/` и `CLAUDE.md` Copilot в VS Code тоже умеет читать (проверить при подключении).

## Языки

Файлы харнесса (`CLAUDE.md`, `.claude/**`, `.ai/**`) — на английском: они загружаются в контекст сессий, а кириллица занимает в 1,5–2,5 раза больше токенов. Заголовки issues, коммиты, PR и комментарии к ним — на английском (NFR-7). Документация в `docs/`, планы и тексты issues — на русском.

## Рабочие циклы

У каждой команды свой документ в `docs/ai/`: текстовая схема шагов и диаграмма Mermaid с указанием, кто что делает на каждом шаге — 💻 основная сессия, 🤖 агент, 🧩 скилл, 🙋 ожидание пользователя. Документы пишет и держит в соответствии с телами команд (`.ai/prompts/`) скилл `document-workflow`.

| Команда | Документ | Что делает |
|---|---|---|
| `/implement-issue <n>` | [docs/ai/implement-issue.md](implement-issue.md) | Одна задача от чтения до PR; метки выбирают Bug, Feature или Test-authoring lane; две контрольные точки с пользователем |
| `/implement-issues <n> <n> …` | [docs/ai/implement-issues.md](implement-issues.md) | Несколько небольших задач на одной ветке, по коммиту на задачу, один PR |

Рядом с циклами действует правило из `CLAUDE.md`: ветки не создаются, не переключаются, не переименовываются и не удаляются, а `git stash` не вызывается без просьбы пользователя; исключение — ветка задачи, создаваемая вместе с разрешённым коммитом.

Разделы ниже описывают то, что общее для всех команд: порядок задач, агентов и модели, правила, скиллы, MCP-серверы и разрешения.

## Порядок задач: `/next-issue`

Зависимости между issues хранятся дважды: связи GitHub «Blocked by» (для машины) и раздел `## Зависимости` в тексте issue (для человека). При создании issue заполняются оба.

Скрипт `.claude/skills/_local.next-issue/scripts/Get-NextIssue.ps1` одним GraphQL-запросом получает открытые issues и выбирает готовые: без открытого PR и со всеми закрытыми блокирующими задачами. Порядок — по F-коду в заголовке (фаза, затем номер). Параметр `-AssumeClosed <n>` считает задачу закрытой: так `implement-issue` рекомендует следующую задачу до merge текущего PR.

## Агенты и модели

| Агент | Модель | Назначение и причина выбора модели |
|---|---|---|
| `issue-planner` | Opus | Только чтение, возвращает план. Ошибка плана стоит дороже всего, а объём вывода мал |
| `issue-developer` | Sonnet по умолчанию, Opus для сложности L | Самый «дорогой» по токенам агент (чтение, сборки, итерации); по согласованному детальному плану Sonnet обычно достаточно |
| `build-runner` | Haiku | Шлюз сборки на шаге 9: полная пересборка, тесты, `dotnet format --verify-no-changes`. Возвращает дословные итоговые строки инструментов и первые ошибки, поэтому пересказ не может исказить результат. Основная сессия тратит на шлюз один вызов вместо 4–6, а шумный вывод сборки не попадает в её контекст. Проверяет независимо от `issue-developer`, ничего не исправляет |
| `skill-runner` | Haiku | Выполняет одно уже подтверждённое действие целиком по скиллу: коммит (`git-commit`), комментарий в issue (`github-issue`) или PR (`open-pr`) — пишет текст на английском, запускает команду `git`/`gh`, проверяет результат. Основная сессия тратит на действие один вызов и не загружает скилл в свой контекст. Push, правку тела issue и merge агент не делает |
| `architect` | Opus | Проектные вопросы: границы слоёв, отказоустойчивость, отмена |
| `security-reviewer` | Opus | Риски приложения: утечка токена, инъекция аргументов git, выход за пределы папок, архивы, CI-конвейеры (по правилам `.claude/rules/github-actions.md`) |

Черновые папки и файлы, которые основная сессия передаёт `build-runner` и `skill-runner`, лежат вне репозитория, а оба агента запускают одну простую команду за вызов: так в `git status` не попадает ничего лишнего, а каждая команда подходит под правила разрешений. Черновая папка — это scratchpad основной сессии, отдельная папка каждой сессии, которую Claude Code держит во временном каталоге системы; `build-runner` получает в ней свою подпапку. Внутри процесса файлы там по одному не удаляются: это стоило бы вызовов модели, а вне автоматического режима — запроса разрешения на каждый файл. Очистка старых папок сессий целиком остаётся за пользователем, вне процесса. `skill-runner` переносит числа, имена и перечни из переданных фактов дословно и не добавляет своих строк приёмки: формальные действия Haiku выполняет надёжно, а при пересказе плотных фактов в комментарии к issue ошибался.
Цены за 1M токенов (вход/выход) на момент настройки: Opus 5.5 — $4/$20, Sonnet 5.5 — $2/$10, Haiku 4.5 — $1/$5, Fable 5.1 — $10/$50. По замерам TimeTracker около 88% затрат приходится на основную сессию с растущим контекстом, поэтому дисциплина контекста важнее выбора модели субагента.

Ревью кода — встроенные `/code-review` и `/security-review`; они читают правила проекта.

## Правила (`.claude/rules/`)

Правило подгружается в контекст, когда ассистент читает файл, подходящий под его `paths`.

| Правило | Пути | Содержание |
|---|---|---|
| `csharp.md` | `**/*.cs` | Слои, DI и options, TimeProvider и IFileSystem, async и отмена, ошибки, логирование, гигиена комментариев, стиль C# 14 |
| `tests.md` | тестовые проекты | Слои тестов, xUnit v3, Shouldly, NSubstitute, WireMock, FlaUI, детерминированность |
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
| `_local.open-pr` | PR в `main`, `Closes #<n>`, содержание описания, merge commit, однократная проверка CI |
| `_local.github-issue` | Чтение issue (тело, комментарии, подзадачи, ссылки на PRD), выбор lane по меткам, список приёмки; комментарий в issue: анализ причины для бага, итоги реализации для остальных. Скрипт `Set-AcceptanceChecks.ps1` одним вызовом отмечает проверенные пункты раздела приёмки (`## Критерии приёмки`, `## Acceptance criteria` или `## Acceptance`) по их номерам; остальной текст issue он не меняет и отказывается писать, если изменилось бы что-то кроме отметок |
| `_local.write-tests` | Выбор слоя тестов, регрессионный тест сначала для бага |
| `_local.debug-issue` | Воспроизведение, разбор логов, поиск причины |
| `_local.refactor-code` | Рефакторинг без изменения поведения |
| `_local.nuget-package-update` | Обновление пакетов по семействам с проверкой сборкой и тестами |
| `_local.next-issue` | Рекомендация следующей задачи |
| `_local.harness-quality-check` | Бенчмарк харнесса (только ручной запуск) |
| `document-workflow` | Пишет или синхронизирует документ команды в `docs/ai/`: схема шагов и диаграмма Mermaid с маркерами 💻 🤖 🧩 🙋; диаграмму проверяет локальным скриптом `preview-mermaid.cjs` |

Сторонние (скопированы как есть, лицензия MIT; при обновлении — заменить папку целиком):

| Скилл | Источник |
|---|---|
| `mvvm-toolkit`, `mvvm-toolkit-di`, `mvvm-toolkit-messenger`, `system-commandline-cli` | [github/awesome-copilot](https://github.com/github/awesome-copilot/tree/main/skills) |
| `run-tests`, `test-anti-patterns`, `coverage-analysis` | [dotnet/skills](https://github.com/dotnet/skills), плагин `dotnet-test` |
| `directory-build-organization` | [dotnet/skills](https://github.com/dotnet/skills), плагин `dotnet-msbuild` |
| `dotnet-pinvoke` | [dotnet/skills](https://github.com/dotnet/skills), плагин `dotnet-advanced` (для Credential Manager) |
| `csharp-async`, `csharp-docs`, `ef-core`, `microsoft-docs` | из харнесса TimeTracker (исходно github/awesome-copilot) |
| `update-docs`, `code-review-checklist`, `security-owasp`, `dotnet-best-practices`, `csharp-xunit`, `create-implementation-plan`, `create-specification`, `dotnet-timezone` | из харнесса AddressBook2025 (исходно github/awesome-copilot) |

Общие чек-листы загружаются по требованию, а не лежат в каждом контексте. Редко нужные — `coverage-analysis`, `directory-build-organization`, `test-anti-patterns`, `create-specification`, `dotnet-timezone`, `_local.harness-quality-check` — помечены `disable-model-invocation: true`: их описания не попадают в стартовый контекст, и они запускаются только командой `/<имя>`.

Правила `wpf-mvvm.md` и `github-actions.md` основаны на инструкциях `dotnet-wpf`, `mvvm-toolkit` и `github-actions-ci-cd-best-practices` из github/awesome-copilot.

## MCP-серверы (`.mcp.json`)

| Сервер | Назначение |
|---|---|
| `context7` | Документация сторонних библиотек (Octokit, Serilog, Spectre.Console, WireMock…) |
| `microsoft-learn` | Официальная документация Microsoft (.NET, WPF, System.CommandLine, EF Core, MSBuild) |
| `nuget` | Версии, уязвимости и сведения о пакетах NuGet; запускается через `dnx` из .NET 10 SDK |

Не подключены сознательно: GitHub MCP (всё нужное делает `gh` без дополнительного токена и без десятков инструментов в контексте), Playwright (только браузер, для WPF бесполезен), Figma. MCP для автоматизации WPF выбирается в #49.

## Разрешения (`.claude/settings.json`)

- Без вопросов: сборка, тесты, `dotnet format`, чтение через git и `gh`, инструменты MCP-серверов, скрипты скиллов `_local.*`, `gh api graphql`, правка существующего комментария issue (`gh api -X PATCH repos/askrinnik/GitHubBackup/issues/comments/*`; у `gh` нет отдельной команды для неё), локальные `git add`, `git switch`, `git pull --ff-only`, а также `git commit`, `git push`, `gh issue comment`, `gh issue edit`, `gh pr create`. Для последних пяти подтверждение даёт пользователь в чате: шлюзы процесса `implement-issue` требуют явного «да» перед каждым из этих действий, поэтому системный запрос не дублирует его. То же относится к скрипту `Set-AcceptanceChecks.ps1`: он запускается без системного запроса, как все скрипты `_local.*`, но правит тело issue и потому вызывается только после «да» пользователя.
- С системным вопросом: `gh pr merge`, `gh pr comment`, `gh pr edit`, `gh issue create`, `gh issue close` — действия, которые трудно отменить или которые меняют чужие данные.
- Без правила: остальные REST-вызовы `gh api` (чтение `repos/…`, запись с `-X` или `--method`). Обычный режим спрашивает о них, автоматический решает по смыслу команды. Широкие шаблоны `gh api -X *` и `gh api repos/*` в списке «спрашивать» перекрыли бы разрешение на правку комментария.
- Запрещено: `git push --force`, `git reset --hard`, `git clean`, `rm -rf`, чтение `.env`.
- Подпись `Co-Authored-By` в коммитах и PR отключена.
- Правило «спрашивать» сильнее правила «разрешено», поэтому разрешение для узкого шаблона не работает, если более широкий шаблон стоит в «спрашивать».
- Составные команды (`cd … &&`, конвейеры, циклы) нельзя разрешить навсегда, поэтому `CLAUDE.md` требует простых команд из корня репозитория.
- Разрешения меняет только пользователь: правку `.claude/settings.json` ассистентом автоматический режим блокирует как самоизменение прав.

## Хуки (`.claude/settings.json`, `.claude/hooks/`)

| Событие | Скрипт | Что делает |
|---|---|---|
| `PostToolUse`, инструменты `Bash` и `PowerShell` | `Test-CommitMessageHook.ps1` | После каждой команды `git commit` проверяет сообщение последнего коммита по скиллу `_local.git-commit` и при нарушении возвращает ассистенту список ошибок с требованием исправить коммит через `git commit --amend` до push |
| `PostToolUse`, инструменты `Edit`, `Write` и `MultiEdit` | `Format-EditedFileHook.ps1` | После правки файла `.cs` в `src/` выравнивает в нём пробелы и отступы по `src/.editorconfig` командой `dotnet format whitespace src --folder --include <файл>` |

Хук проверки коммита:
- работает и в основной сессии, и в субагентах, в том числе в `skill-runner`;
- на ветке `<n>-<slug>` требует первую строку `#<n> <точный заголовок issue>`; заголовок берёт из GitHub через `gh`; на ветке пакета `<first>-<last>-<slug>` сверяет заголовок того issue, номер которого стоит в первой строке;
- на `main` и `master` разрешает коммит, но считает его работой вне задачи: сообщение состоит только из пунктов `- `, а заголовок `#<n> …` считается ошибкой;
- проверяет пустую вторую строку, пункты `- ` без пустых строк между ними и отсутствие трейлеров (`Co-Authored-By`, `Signed-off-by`, «Generated with»);
- пропускает merge-коммиты, отсоединённый HEAD (например, во время rebase) и неудачную команду `git commit` (последний коммит старше двух минут);
- если `gh` не прочитал заголовок, пишет предупреждение и проверяет остальные правила;
- запускается на каждую команду `Bash` и `PowerShell` и для команды без `git commit` завершается примерно за треть секунды.

Правила лежат в модуле `CommitMessageRules.psm1`, проверочные случаи — в `.claude/hooks/tests/CommitMessageRules.Cases.ps1`:

```
pwsh -NoProfile -File .claude/hooks/tests/CommitMessageRules.Cases.ps1
```

Хук форматирования:
- обрабатывает только существующий файл `.cs` внутри `src/`; XAML, проекты, Markdown и файлы вне `src/` не трогает, потому что режим `--folder` форматирует только код;
- в режиме `--folder` не загружает MSBuild и тратит около секунды на файл;
- исправляет только пробелы и отступы; стиль кода и анализаторы проверяют сборка (`TreatWarningsAsErrors`) и `dotnet format --verify-no-changes` в `build-runner` и CI;
- никогда не блокирует ассистента: ошибку `dotnet format` пишет одной строкой в stderr и завершается с кодом 0;
- сборку и тесты в конце хода (хук `Stop`) не запускает: сборка солюшна заняла бы десятки секунд на каждый ход и повторила бы шаг `build-runner` в `/implement-issue`.

Правила выбора файла лежат в модуле `FormatHookRules.psm1`, проверочные случаи — в `.claude/hooks/tests/FormatHookRules.Cases.ps1`. Им нужен .NET SDK: случаи запускают хук на временной папке с копией `src/.editorconfig`.

```
pwsh -NoProfile -File .claude/hooks/tests/FormatHookRules.Cases.ps1
```

CI (`.github/workflows/ci.yml`, шаг `Hook test cases`) запускает все файлы `.claude/hooks/tests/*.Cases.ps1` на каждом pull request и push в `main`. Новый набор проверочных случаев попадает в CI сам, если его имя оканчивается на `.Cases.ps1` и скрипт при сбое завершается с кодом 1.

## Бенчмарк харнесса

Скилл `_local.harness-quality-check` запускает фиксированные тестовые сессии (`claude -p`) и записывает метрики: контекст в начале, токены от чтения файла, загруженные правила, оценку судьи по эталону. История — `.ai/benchmarks/harness/run-history.csv`, отчёты — `.ai/benchmarks/harness/reports/`. Запускать после изменения `CLAUDE.md`, правил, скиллов, агентов, MCP или настроек.

Сейчас фикстуры: `bench-base` (стартовый контекст) и `quality-prd-first` (соблюдение правила PRD-first). Фикстуры, которым нужен код, — в отложенных задачах.

## Профиль реальных сессий

Бенчмарк выше гоняет фиксированные тестовые сессии; скилл `harness-kit:profile-session` из плагина `harness-kit` измеряет реальную, уже прошедшую сессию по её транскрипту: вызовы модели, взвешенную стоимость, рост контекста, стоимость по видам работы и долю недельного лимита между двумя показаниями `get_usage`. Настройки для этого репозитория — `profile-session.config.json` в корне.

- `/implement-issue` снимает стартовое показание лимита на шаге 0 и строит профиль на шаге 12, между коммитом и push; на «да» пользователя запись добавляется в коммит задачи через amend.
- `/implement-issues` делает то же один раз на пакет: показание в предполёте, профиль на выдаче перед push; запись идёт отдельным коммитом, потому что коммиты пакета не переписываются.
- Записи (по одной на сессию) — `.ai/benchmarks/sessions/records/`, коммитятся. Отчёт `.ai/benchmarks/sessions/report.html` строится из записей локально и в git не попадает.
- Без установленного плагина шаг профиля пропускается с одной строкой об этом.

## Отложенные задачи

| Issue | Когда | Что |
|---|---|---|
| [#47](https://github.com/askrinnik/GitHubBackup/issues/47) F1.13 | после #10, #13 | Фикстуры для кода: `bench-cs`, `bench-test`, `quality-service-convention`, `quality-security` |
| [#48](https://github.com/askrinnik/GitHubBackup/issues/48) F3.11 | после #26 | Фикстура `quality-docs-sync` |
| [#49](https://github.com/askrinnik/GitHubBackup/issues/49) F4.8 | после #36, до #37 | MCP для автоматизации WPF, фикстура `bench-xaml` |

## Перенос в другой проект

Без изменений переносятся:
- механизм `implement-issue` (шаги, контрольные точки, экономия контекста);
- агенты `issue-planner`, `issue-developer`, `skill-runner`, а `build-runner` — с заменой команд сборки (см. ниже);
- скиллы `_local.git-commit`, `_local.open-pr`, `_local.github-issue`, `_local.next-issue` — для любого репозитория на GitHub;
- хук проверки сообщения коммита (`.claude/hooks/`) вместе со скиллом `_local.git-commit`;
- хук форматирования — для .NET-репозитория, где код и `.editorconfig` лежат в `src/`;
- гигиена комментариев (`csharp.md`);
- бенчмарк (`_local.harness-quality-check` и `bench-base`).

Нужно переписать под проект:
- `CLAUDE.md`;
- команды сборки, тестов и формата в агенте `build-runner`;
- правила по стеку (`wpf-mvvm.md`, `cli.md`, `external-processes.md`, `security.md`, раздел «GitHubBackup patterns» в `csharp.md`);
- семейства пакетов в `_local.nuget-package-update`;
- эталоны фикстур `quality-*`.

Условия для переноса: PRD-документ с идентификаторами требований, issues с разделами «Источник требований», «Критерии приёмки» и «Зависимости», F-коды в заголовках и связи «Blocked by».

## Сопровождение

При изменении `CLAUDE.md`, `.claude/**`, `.ai/**` или `.mcp.json` этот документ обновляется в том же изменении (правило `update-docs-on-code-change.md`), после чего желательно запустить бенчмарк.
