# AI Harness проекта GitHubBackup

Документ описывает окружение AI-ассистента (Claude Code), по которому ведётся разработка: из чего оно состоит, какие его части дают плагины, а какие принадлежат репозиторию, и как перенести харнесс в другой проект.

Рабочий цикл задачи и бенчмарк харнесса приходят из плагинов `issue-kit` и `harness-kit` маркетплейса [askrinnik/ai-plugins](https://github.com/askrinnik/ai-plugins). Плагины описаны своими документами; здесь — только то, что относится к GitHubBackup.

## Состав

```
CLAUDE.md                          главные инструкции, загружаются в каждую сессию
.mcp.json                          MCP-серверы проекта
issue-kit.config.json              значения репозитория для плагина issue-kit
profile-session.config.json        настройки профиля сессий (harness-kit)
.claude/
  settings.json                    маркетплейс и плагины, разрешения, MCP, хук форматирования, отключённая подпись в коммитах
  hooks/                           скрипт хука форматирования и его проверочные случаи
  settings.local.json              личные настройки (не в git)
  agents/                          architect, security-reviewer
  rules/                           правила, которые подгружаются по путям файлов
  skills/                          _local.* (свои) и сторонние скиллы
.ai/
  benchmarks/harness/              фикстуры, история и отчёты бенчмарка харнесса
  benchmarks/sessions/records/     записи профилей реальных сессий
docs/
  PRD.md                           требования (источник истины)
  plans/                           планы по issues
  ai/README.md                     этот документ
```

Плагины подключены в `.claude/settings.json`: `extraKnownMarketplaces` указывает маркетплейс `ai-plugins` на GitHub-репозиторий `askrinnik/ai-plugins`, `enabledPlugins` включает `issue-kit@ai-plugins` и `harness-kit@ai-plugins`. Каждый, кто открывает репозиторий в Claude Code, получает предложение установить их.

## Языки

Файлы харнесса (`CLAUDE.md`, `.claude/**`, `.ai/**`, конфигурации плагинов) — на английском: они загружаются в контекст сессий, а кириллица занимает в 1,5–2,5 раза больше токенов. Заголовки issues, коммиты, PR и комментарии к ним — на английском (NFR-7). Документация в `docs/`, планы и тексты issues — на русском; язык планов задаёт ключ `plans.language` в `issue-kit.config.json`.

## Рабочие циклы

Команды, их шаги, агенты и контрольные точки описаны в документах плагина `issue-kit`:

| Команда | Документ плагина | Что делает |
|---|---|---|
| `/issue-kit:implement-issue <n>` | [implement-issue](https://github.com/askrinnik/ai-plugins/blob/main/plugins/issue-kit/docs/implement-issue.md) | Одна задача от чтения до PR; метки выбирают Bug, Feature или Test-authoring lane; две контрольные точки с пользователем |
| `/issue-kit:implement-issues <n> <n> …` | [implement-issues](https://github.com/askrinnik/ai-plugins/blob/main/plugins/issue-kit/docs/implement-issues.md) | Несколько небольших задач на одной ветке, по коммиту на задачу, один PR |
| `/issue-kit:next-issue` | [README плагина](https://github.com/askrinnik/ai-plugins/blob/main/plugins/issue-kit/README.md) | Рекомендация следующей готовой задачи |

Значения GitHubBackup для этих команд лежат в `issue-kit.config.json`; каждый ключ описан в [CONFIGURATION.md](https://github.com/askrinnik/ai-plugins/blob/main/plugins/issue-kit/CONFIGURATION.md) плагина. Файл задаёт команды сборки, тестов и формата из `CLAUDE.md`, PRD `docs/PRD.md` с идентификаторами `FR-x.y` и `NFR-x`, русские заголовки разделов issue и порядок задач по F-коду в заголовке. В `roles` он называет проектные агенты `architect` и `security-reviewer` и скиллы `debug-issue` и `write-tests`; в `securityReview.triggers` — пути, изменение которых требует ревью безопасности (`Infrastructure`, `Cli`, CI, `Directory.Packages.props`). Проверка файла: `/issue-kit:init-config check`.

Рядом с циклами действует правило из `CLAUDE.md`: ветки не создаются, не переключаются, не переименовываются и не удаляются, а `git stash` не вызывается без просьбы пользователя; исключение — ветка задачи, создаваемая вместе с разрешённым коммитом.

Зависимости между issues хранятся дважды: связи GitHub «Blocked by» (по ним работает `/issue-kit:next-issue`) и раздел `## Зависимости` в тексте issue (для человека). При создании issue заполняются оба.

## Агенты

Агенты рабочего цикла (`issue-kit:issue-planner`, `issue-kit:issue-developer`, `issue-kit:build-runner`, `issue-kit:skill-runner`) и выбор их моделей описаны в README плагина. У агентов плагина нет MCP-инструментов проекта (`context7`, `microsoft-learn`); если они понадобятся планировщику или разработчику, проектный агент указывается в `roles.planner` или `roles.developer`.

Проектные агенты (`.claude/agents/`):

| Агент | Модель | Назначение |
|---|---|---|
| `architect` | Opus | Проектные вопросы: границы слоёв, отказоустойчивость, отмена |
| `security-reviewer` | Opus | Риски приложения: утечка токена, инъекция аргументов git, выход за пределы папок, архивы, CI-конвейеры (по правилам `.claude/rules/github-actions.md`) |

Ревью кода — встроенные `/code-review` и `/security-review`; они читают правила проекта.

## Правила (`.claude/rules/`)

Правило подгружается в контекст, когда ассистент читает файл, подходящий под его `paths`.

| Правило | Пути | Содержание |
|---|---|---|
| `csharp.md` | `**/*.cs` | Слои, DI и options, TimeProvider и IFileSystem, async и отмена, ошибки, логирование, гигиена комментариев, стиль C# 14 |
| `tests.md` | тестовые проекты | Слои тестов, xUnit v3, Shouldly, NSubstitute, WireMock, FlaUI, проверка логов через `FakeLogger`, детерминированность |
| `wpf-mvvm.md` | `GitHubBackup.App/**`, `*.xaml` | MVVM на CommunityToolkit.Mvvm, команды с отменой, виртуализация, AutomationId, проверка запущенного приложения через `netwright` |
| `cli.md` | `GitHubBackup.Cli/**` | System.CommandLine, Spectre.Console, коды возврата, `--silent`, `--dry-run` |
| `external-processes.md` | `GitHubBackup.Infrastructure/**` | Запуск git и 7-Zip, передача токена через окружение, коды возврата, логирование вывода, отмена |
| `security.md` | все основные проекты | Каталог рисков с идентификаторами S/I/F/P/D и уровнями серьёзности |
| `msbuild.md` | `*.csproj`, `Directory.*.props`, `.slnx` | Общие свойства, Central Package Management, ссылки по слоям |
| `github-actions.md` | `.github/workflows/**` | Закреплённые версии actions, минимальные права, секреты, кэш |
| `update-docs-on-code-change.md` | CLI, настройки, файлы харнесса | Какой документ обновлять при каком изменении; PRD-first |
| `docs.md` | `docs/**`, `README.md` | Русский язык, правила правки PRD, формат планов |

## Скиллы (`.claude/skills/`)

Скиллы рабочего цикла (`issue-kit:git-commit`, `issue-kit:open-pr`, `issue-kit:github-issue`, `issue-kit:next-issue`, `issue-kit:init-config`) и бенчмарк (`harness-kit:harness-quality-check`) дают плагины.

Свои (`_local.*` и `document-workflow`):

| Скилл | Назначение |
|---|---|
| `_local.write-tests` | Выбор слоя тестов, регрессионный тест сначала для бага (роль `roles.testsSkill`) |
| `_local.debug-issue` | Воспроизведение, разбор логов, поиск причины (роль `roles.debugSkill`) |
| `_local.refactor-code` | Рефакторинг без изменения поведения |
| `_local.nuget-package-update` | Обновление пакетов по семействам с проверкой сборкой и тестами |
| `document-workflow` | Пишет или синхронизирует документ рабочего цикла: схема шагов и диаграмма Mermaid с маркерами 💻 🤖 🧩 🙋; диаграмму проверяет локальным скриптом `preview-mermaid.cjs` |

Сторонние (скопированы как есть, лицензия MIT; при обновлении — заменить папку целиком):

| Скилл | Источник |
|---|---|
| `mvvm-toolkit`, `mvvm-toolkit-di`, `mvvm-toolkit-messenger`, `system-commandline-cli` | [github/awesome-copilot](https://github.com/github/awesome-copilot/tree/main/skills) |
| `run-tests`, `test-anti-patterns`, `coverage-analysis` | [dotnet/skills](https://github.com/dotnet/skills), плагин `dotnet-test` |
| `directory-build-organization` | [dotnet/skills](https://github.com/dotnet/skills), плагин `dotnet-msbuild` |
| `dotnet-pinvoke` | [dotnet/skills](https://github.com/dotnet/skills), плагин `dotnet-advanced` (для Credential Manager) |
| `csharp-async`, `csharp-docs`, `ef-core`, `microsoft-docs` | из харнесса TimeTracker (исходно github/awesome-copilot) |
| `update-docs`, `code-review-checklist`, `security-owasp`, `dotnet-best-practices`, `csharp-xunit`, `create-implementation-plan`, `create-specification`, `dotnet-timezone` | из харнесса AddressBook2025 (исходно github/awesome-copilot) |

Общие чек-листы загружаются по требованию, а не лежат в каждом контексте. Редко нужные — `coverage-analysis`, `directory-build-organization`, `test-anti-patterns`, `create-specification`, `dotnet-timezone` — помечены `disable-model-invocation: true`: их описания не попадают в стартовый контекст, и они запускаются только командой `/<имя>`.

Правила `wpf-mvvm.md` и `github-actions.md` основаны на инструкциях `dotnet-wpf`, `mvvm-toolkit` и `github-actions-ci-cd-best-practices` из github/awesome-copilot.

## MCP-серверы (`.mcp.json`)

| Сервер | Назначение |
|---|---|
| `context7` | Документация сторонних библиотек (Octokit, Serilog, Spectre.Console, WireMock…) |
| `microsoft-learn` | Официальная документация Microsoft (.NET, WPF, System.CommandLine, EF Core, MSBuild) |
| `nuget` | Версии, уязвимости и сведения о пакетах NuGet; запускается через `dnx` из .NET 10 SDK |
| `netwright` | Автоматизация WPF-приложения на шаге проверки UI-задач: запуск `GitHubBackup.App` из `.csproj`, проверки элементов по `AutomationId`, скриншоты; запускается через `dnx`, версия закреплена (`Netwright@2.1.0`), флаг `--allow GitHubBackup*` не даёт трогать другие приложения |

Не подключены сознательно: GitHub MCP (всё нужное делает `gh` без дополнительного токена и без десятков инструментов в контексте), Playwright (только браузер, для WPF бесполезен), Figma.

### Автоматизация WPF: почему Netwright

Сравнивались [Netwright](https://github.com/ricfanin/netwright) 2.1.0 и [FlaUI-MCP](https://github.com/shanselman/FlaUI-MCP) 0.2.1, оба под лицензией MIT.

| Критерий | Netwright | FlaUI-MCP |
|---|---|---|
| Установка | пакет NuGet через `dnx`, как сервер `nuget` | zip из GitHub Releases, нужен .NET 8 runtime |
| Запуск приложения | из `.csproj`: собирает и запускает | по пути к `.exe` |
| Действия | в фоне, без захвата мыши и фокуса | реальным вводом |
| Ограничение приложений | `--allow <маска>` | нет |
| Вывод приложения | stdout, stderr, `Trace`, исключение со стеком | нет |

Netwright ставится так же, как уже подключённый сервер, не мешает пользователю работать во время проверки и не может управлять чужими окнами.

Ограничение версии 2.1.0: сервер кладёт в `structuredContent` только `{"ok":true}`, а Claude Code показывает модели его вместо текста ([ricfanin/netwright#1](https://github.com/ricfanin/netwright/issues/1), пункт 01). Снимки окна и отчёты об изменениях до модели не доходят, ошибки доходят полностью. Поэтому правило `wpf-mvvm.md` проверяет UI через `desktop_expect`: успех возвращает `{"ok":true}`, провал — `EXPECTATION_FAILED` с фактическим состоянием элемента. Скриншоты (`desktop_screenshot`) работают. Снятие обхода после исправления бага — [#106](https://github.com/askrinnik/GitHubBackup/issues/106).

Проверка не заменяет UI-тесты: слой UI-тестов — FlaUI (`tests.md`), пакет `Netwright.Testing` не используется.

## Разрешения (`.claude/settings.json`)

- Без вопросов: сборка, тесты, `dotnet format`, чтение через git и `gh`, инструменты MCP-серверов, скрипты плагинов (`pwsh -NoProfile -File *issue-kit*` и `*harness-kit*`) и скрипт скилла `_local.nuget-package-update`, `gh api graphql`, правка существующего комментария issue (`gh api -X PATCH repos/askrinnik/GitHubBackup/issues/comments/*`; у `gh` нет отдельной команды для неё), локальные `git add`, `git switch`, `git pull --ff-only`, а также `git commit`, `git push`, `gh issue comment`, `gh issue edit`, `gh issue create`, `gh pr create`. Для последних шести подтверждение даёт пользователь в чате: правила `CLAUDE.md` и шлюзы рабочего цикла требуют явного «да» перед каждым из этих действий, поэтому системный запрос не дублирует его. То же относится к скрипту отметки пунктов приёмки из `issue-kit:github-issue`: он запускается без системного запроса, как все скрипты плагинов, но правит тело issue и потому вызывается только после «да» пользователя.
- С системным вопросом: `gh pr merge`, `gh pr comment`, `gh pr edit`, `gh issue close` — действия, которые трудно отменить или которые меняют чужие данные.
- Без правила: остальные REST-вызовы `gh api` (чтение `repos/…`, запись с `-X` или `--method`). Обычный режим спрашивает о них, автоматический решает по смыслу команды. Широкие шаблоны `gh api -X *` и `gh api repos/*` в списке «спрашивать» перекрыли бы разрешение на правку комментария.
- Запрещено: `git push --force`, `git reset --hard`, `git clean`, `rm -rf`, чтение `.env`.
- Подпись `Co-Authored-By` в коммитах и PR отключена: формат коммита `issue-kit:git-commit` не допускает трейлеров.
- Правило «спрашивать» сильнее правила «разрешено», поэтому разрешение для узкого шаблона не работает, если более широкий шаблон стоит в «спрашивать».
- Составные команды (`cd … &&`, конвейеры, циклы) нельзя разрешить навсегда, поэтому `CLAUDE.md` требует простых команд из корня репозитория.
- Разрешения меняет только пользователь: правку `.claude/settings.json` ассистентом автоматический режим блокирует как самоизменение прав.

## Хуки

Хук проверки сообщения коммита даёт плагин `issue-kit`: после каждой команды `git commit` он сверяет сообщение со скиллом `issue-kit:git-commit`. Хук работает только в репозитории с `issue-kit.config.json`; его правила и проверочные случаи живут в плагине.

Свой хук репозитория (`.claude/settings.json`, `.claude/hooks/`):

| Событие | Скрипт | Что делает |
|---|---|---|
| `PostToolUse`, инструменты `Edit`, `Write` и `MultiEdit` | `Format-EditedFileHook.ps1` | После правки файла `.cs` в `src/` выравнивает в нём пробелы и отступы по `src/.editorconfig` командой `dotnet format whitespace src --folder --include <файл>` |

Хук форматирования:
- обрабатывает только существующий файл `.cs` внутри `src/`; XAML, проекты, Markdown и файлы вне `src/` не трогает, потому что режим `--folder` форматирует только код;
- в режиме `--folder` не загружает MSBuild и тратит около секунды на файл;
- исправляет только пробелы и отступы; стиль кода и анализаторы проверяют сборка (`TreatWarningsAsErrors`) и `dotnet format --verify-no-changes` в `issue-kit:build-runner` и CI;
- никогда не блокирует ассистента: ошибку `dotnet format` пишет одной строкой в stderr и завершается с кодом 0;
- сборку и тесты в конце хода (хук `Stop`) не запускает: сборка солюшна заняла бы десятки секунд на каждый ход и повторила бы шлюз `build-runner` в рабочем цикле.

Правила выбора файла лежат в модуле `FormatHookRules.psm1`, проверочные случаи — в `.claude/hooks/tests/FormatHookRules.Cases.ps1`. Им нужен .NET SDK: случаи запускают хук на временной папке с копией `src/.editorconfig`.

```
pwsh -NoProfile -File .claude/hooks/tests/FormatHookRules.Cases.ps1
```

CI (`.github/workflows/ci.yml`, шаг `Hook test cases`) запускает все файлы `.claude/hooks/tests/*.Cases.ps1` на каждом pull request и push в `main`. Новый набор проверочных случаев попадает в CI сам, если его имя оканчивается на `.Cases.ps1` и скрипт при сбое завершается с кодом 1.

## Бенчмарк харнесса

Скилл `harness-kit:harness-quality-check` запускает фиксированные тестовые сессии (`claude -p`) и записывает метрики: контекст в начале, токены от чтения файла, загруженные правила, оценку судьи по эталону. Скрипт живёт в плагине, а фикстуры, история (`.ai/benchmarks/harness/run-history.csv`) и отчёты (`.ai/benchmarks/harness/reports/`) — в репозитории. Запускать после изменения `CLAUDE.md`, правил, скиллов, агентов, MCP, настроек или версии плагинов. Запуск и описание фикстур — в `.ai/benchmarks/harness/README.md`.

| Фикстура | Что измеряет |
|---|---|
| `bench-base` | Стартовый контекст по областям |
| `bench-cs` | Сколько токенов добавляет чтение файла `Core` вместе с правилами по путям (`csharp`, `security`) |
| `bench-test` | То же для файла тестового проекта (`csharp`, `tests`) |
| `bench-xaml` | То же для файла XAML приложения (`wpf-mvvm`) |
| `quality-prd-first` | Соблюдение правила PRD-first на запрос новой функции |
| `quality-service-convention` | Соблюдение соглашений при проектировании нового сервиса: слои, DI, `IFileSystem` и `TimeProvider`, XML-документация, тест в парном проекте |
| `quality-security` | Ревью безопасности: находит ли ассистент семь уязвимостей, подброшенных `seed.patch` |
| `quality-docs-sync` | Синхронизация документов: называет ли ассистент документы, которые нужно обновить для нового параметра CLI и новой настройки из `seed.patch`, без лишних |

## Профиль реальных сессий

Бенчмарк выше гоняет фиксированные тестовые сессии; скилл `harness-kit:profile-session` измеряет реальную, уже прошедшую сессию по её транскрипту: вызовы модели, взвешенную стоимость, оценочную сумму в USD по ценам API (не списание по подписке), рост контекста, стоимость по видам работы и долю недельного лимита. Рабочий цикл `issue-kit` вызывает его на шаге коммита (ключ `profile` в конфигурации).

- `profile-session.config.json` в корне сопоставляет инструменты и агентов с видами работы. Агенты и скиллы плагина указаны с префиксом (`^Agent:issue-kit:build-runner`); команда задачи — `/issue-kit:implement-issues?`.
- Записи (по одной на сессию) — `.ai/benchmarks/sessions/records/`, коммитятся. Отчёт `.ai/benchmarks/sessions/report.html` строится из записей локально и в git не попадает.

## Перенос в другой проект

1. Подключить плагины `issue-kit` и `harness-kit` (README маркетплейса и плагинов).
2. Запустить `/issue-kit:init-config`: скилл составит `issue-kit.config.json` по файлам проекта, меткам и issues и выведет добавки для `.claude/settings.json`.
3. Переписать под проект то, что принадлежит репозиторию: `CLAUDE.md`, правила по стеку (`wpf-mvvm.md`, `cli.md`, `external-processes.md`, `security.md`, раздел «GitHubBackup patterns» в `csharp.md`), проектных агентов, семейства пакетов в `_local.nuget-package-update`, фикстуры бенчмарка и эталоны `quality-*`.

Без изменений переносятся гигиена комментариев (`csharp.md`) и хук форматирования — для .NET-репозитория, где код и `.editorconfig` лежат в `src/`.

## Сопровождение

При изменении `CLAUDE.md`, `.claude/**`, `.ai/**`, `.mcp.json`, `issue-kit.config.json` или `profile-session.config.json` этот документ обновляется в том же изменении (правило `update-docs-on-code-change.md`), после чего желательно запустить бенчмарк.
