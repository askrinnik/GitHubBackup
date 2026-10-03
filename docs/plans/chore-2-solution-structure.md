# План: #2 F0.1: Solution and project structure

| Поле | Значение |
|---|---|
| Issue | [#2](https://github.com/askrinnik/GitHubBackup/issues/2) |
| Заголовок | F0.1: Solution and project structure |
| Тип | chore (ветка процесса Feature) |
| Сложность | L |
| Дата | 2026-10-03 |

## 1. Цель

Создать `src/GitHubBackup.slnx` и все 11 проектов PRD §8.1 со ссылками `Cli`, `App` → `Infrastructure` → `Core` и тестовые → парные. Общие настройки сборки — в `Directory.Build.props` (NFR-6), версии пакетов — только в `Directory.Packages.props` (§8.3). `Cli` — пустое консольное приложение, `App` — пустое WPF-приложение. Код и сообщения на английском, документация на русском (NFR-7).

## 2. Критерии приёмки

Из issue:

- [x] `dotnet build src` и `dotnet test src` проходят без предупреждений (тесты — в форме `dotnet test --solution src/GitHubBackup.slnx`)
- [x] Структура папок совпадает с PRD §8.1 (тестовые проекты рядом с основными)
- [x] Версии пакетов указаны только в `Directory.Packages.props`
- [ ] Покрыто тестами, CI зелёный — тесты есть; «CI зелёный» в #2 не проверяется, его закрывает #6 (решение 2)

Добавлено:

- [x] Разделы *Build and test* и *Repository layout* в `CLAUDE.md` и «Сборка и запуск» в `README.md` описывают реально работающие команды.

Границы с соседними issues (проверено по списку открытых issues):

- CI на GitHub Actions — это #6 (F0.5). Критерий «CI зелёный» в #2 не проверяется, его нужно перенести в #6 (см. «Открытые вопросы», п. 2).
- Правила зависимостей слоёв (NetArchTest) — это #7 (F0.6). В #2 в `ArchitectureTests` только smoke-тест.

## 3. Изменения PRD

Не требуются. `net10.0-windows` для `Cli`, `ArchitectureTests` и тестов хостов вытекает из ссылки на `Infrastructure` (`net10.0-windows` по §8.1).

## 4. Затрагиваемые проекты и файлы

Корень репозитория:

- `global.json` (новый): SDK `10.0.100`, `rollForward: latestFeature`, `"test": { "runner": "Microsoft.Testing.Platform" }`.

`src/`:

- `GitHubBackup.slnx` — 11 проектов (`dotnet sln add`).
- `Directory.Build.props`:
  - `TargetFramework`: `net10.0` для `GitHubBackup.Core` и `GitHubBackup.Core.Tests`, `net10.0-windows` для остальных (условие по `$(MSBuildProjectName)`);
  - `Nullable`, `ImplicitUsings`, `LangVersion` 14, `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild`, `GenerateDocumentationFile`;
  - блок для проектов `*Tests`: `OutputType=Exe`, `PackageReference` на `xunit.v3.mtp-v2`, `Shouldly`, `NSubstitute`, глобальные `Using` для `Xunit` и `Shouldly`, `NoWarn CS1591` с комментарием (условие по имени, а не по `IsTestProject`: на момент вычисления `Directory.Build.props` оно ещё не задано).
- `Directory.Packages.props` — `ManagePackageVersionsCentrally`; `xunit.v3.mtp-v2`, `Shouldly`, `NSubstitute`. Последние стабильные версии (NuGet MCP).
- `.editorconfig` — `root = true`, стиль C# по `.claude/rules/csharp.md`; секция для тестовых проектов отключает CA1707 с комментарием.

Проекты:

- `Core` — пустая библиотека, `InternalsVisibleTo` для `Core.Tests`.
- `Infrastructure` → `Core`; `InternalsVisibleTo` для `Infrastructure.Tests`, `Infrastructure.IntegrationTests`.
- `Cli` → `Infrastructure`; `OutputType=Exe`; `Program.cs` с `return 0;`; `InternalsVisibleTo` для `Cli.Tests`.
- `App` → `Infrastructure`; `OutputType=WinExe`, `UseWPF`; `App.xaml`, `MainWindow.xaml` из шаблона с `///` у типов, текст окна на английском; `InternalsVisibleTo` для `App.Tests`.
- Тестовые: `Core.Tests` → `Core`; `Infrastructure.Tests`, `Infrastructure.IntegrationTests` → `Infrastructure`; `Cli.Tests` → `Cli`; `App.Tests`, `App.UITests` → `App`; `ArchitectureTests` → `Core`, `Infrastructure`, `Cli`, `App`.

## 5. Подход

1. `global.json`.
2. `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig` в `src/` (расположение задано PRD §8.1).
3. Проекты по слоям: Core → Infrastructure → Cli/App; `dotnet new`, затем из `.csproj` убирается всё, что есть в props; ссылки — `dotnet add reference`, решение — `dotnet sln add`.
4. Тестовые проекты: пустые `.csproj` и ссылка на парный проект; без `Version` в `PackageReference`, без `Microsoft.NET.Test.Sdk` и `xunit.runner.visualstudio`.
5. Smoke-тесты, затем `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`.
6. Документация и правки харнесса.

Ключевые решения:

- **Раннер — Microsoft.Testing.Platform** (`xunit.v3.mtp-v2`): родной режим xUnit v3 на SDK 10. В нём путь позиционным аргументом не поддерживается, команда — `dotnet test --solution src/GitHubBackup.slnx`. Альтернатива — VSTest (см. п. 1 вопросов).
- **TFM.** Проект `net10.0` не может ссылаться на `net10.0-windows`, поэтому только `Core` и `Core.Tests` остаются на `net10.0`. Это закрепляет «Core без зависимостей от ОС».
- **XML-doc.** `GenerateDocumentationFile=true`; CS1591 — ошибка в продакшен-проектах; при срабатывании на `*.g.cs` — подавление только через `[*.g.cs]` в `.editorconfig` с комментарием.
- **Анализаторы.** Каждое правило, сработавшее на шаблонах, разбирается отдельно: правка кода или severity в `.editorconfig` с причиной.
- **«Покрыто тестами» без поведения.** В каждом из 7 тестовых проектов один smoke-тест: парная сборка загружается по имени (`Assembly.Load(new AssemblyName(...))`), что проверяет ссылки и согласованность TFM. MTP завершается кодом 8 на проекте без тестов, так что тест нужен и технически.

## 6. Тесты

xUnit v3 + Shouldly, имена `Method_Scenario_ExpectedResult`:

- `Core.Tests/CoreAssemblyTests.Load_ByName_ReturnsCoreAssembly` и аналоги в `Infrastructure.Tests`, `Infrastructure.IntegrationTests`, `Cli.Tests`, `App.Tests`, `App.UITests`;
- `ArchitectureTests/SolutionAssemblyTests` — все четыре продакшен-сборки загружаются; правила слоёв приходят в #7.

## 7. Проверка

| Критерий | Как проверяется |
|---|---|
| build без предупреждений | `dotnet build src/GitHubBackup.slnx -t:Rebuild -clp:ErrorsOnly` |
| тесты | `dotnet test --solution src/GitHubBackup.slnx --no-build`, ненулевое число тестов в каждом из 7 проектов |
| структура §8.1 | сверка `src/` с деревом; `dotnet sln src/GitHubBackup.slnx list` — 11 проектов |
| версии только в CPM | `Grep` по `src/**/*.csproj` на `Version=` — пусто; restore проходит |
| формат | `dotnet format src/GitHubBackup.slnx --verify-no-changes` |
| документация | команды в `CLAUDE.md` и `README.md` совпадают с выполненными |

## 8. Вне рамок

- CI (#6), правила слоёв NetArchTest (#7), Generic Host, DI, Serilog.
- Пакеты Verify, WireMock.Net, FlaUI, NetArchTest, TimeProvider.Testing, System.IO.Abstractions.TestingHelpers — с первым использованием.
- Публикация (NFR-8), lock-файлы NuGet.

## 9. Открытые вопросы

1. **Режим `dotnet test`.** Рекомендация — MTP: команда `dotnet test --solution src/GitHubBackup.slnx`, критерий «dotnet test src» выполняется в этой форме. Альтернатива — VSTest, буквальное `dotnet test src`, но устаревший режим.
2. **Критерий «CI зелёный».** CI делает #6. Предлагаю не проверять этот пункт в #2 и оставить его неотмеченным с комментарием.
3. **Версия SDK в `global.json`:** `10.0.100` + `latestFeature` (рекомендация) или точно `10.0.401`.
4. **Правки харнесса в этом PR:** `.claude/rules/msbuild.md` (TFM), `.claude/rules/tests.md` (фильтры MTP), `.ai/prompts/implement-issue.md` (команды, оговорка «until #2»), `.claude/skills/_local.nuget-package-update/SKILL.md` (команда test) — входят в #2 или отдельно.
5. **`App.UITests` → `App` ссылкой на проект** — допустимо (рекомендация), иначе пустой проект без тестов.

## 10. Решения

1. Раннер — Microsoft.Testing.Platform; команда `dotnet test --solution src/GitHubBackup.slnx`.
2. Критерий «CI зелёный» в #2 не проверяется, его закрывает #6; пункт остаётся неотмеченным с пояснением.
3. `global.json`: SDK `10.0.100`, `rollForward: latestFeature`.
4. Правки харнесса (`msbuild.md`, `tests.md`, `implement-issue.md`, `_local.nuget-package-update`) входят в этот PR.
5. `App.UITests` ссылается на `App` и содержит smoke-тест.

## 11. Задачи

- [x] `global.json`
- [x] `src/Directory.Build.props`, `src/Directory.Packages.props`, `src/.editorconfig`
- [x] `Core`, `Infrastructure` (+ `InternalsVisibleTo`)
- [x] `Cli` (пустой console), `App` (пустой WPF, XML-doc)
- [x] 7 тестовых проектов со ссылками на парные
- [x] `src/GitHubBackup.slnx` со всеми 11 проектами
- [x] Smoke-тесты в каждом тестовом проекте
- [x] Сборка без предупреждений, тесты зелёные, `dotnet format --verify-no-changes`
- [x] `CLAUDE.md`: *Build and test*, *Repository layout*; убрать «solution does not exist yet»
- [x] `README.md`: «Сборка и запуск»
- [x] Харнесс: `msbuild.md`, `tests.md`, `implement-issue.md`, `_local.nuget-package-update`; `docs/ai-harness.md` не требует правок
- [x] Отметить пункты плана и критерии приёмки
