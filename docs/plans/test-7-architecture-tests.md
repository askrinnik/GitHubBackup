# План: #7 — F0.6: Architecture tests for layer dependencies

- Issue: [#7](https://github.com/askrinnik/GitHubBackup/issues/7)
- Линия: Feature (`type:test`, `area:build`)
- Сложность: M
- Дата: 2026-10-07
- PRD: §8.1, §11

## Цель

Добавить в `GitHubBackup.ArchitectureTests` проверку правила слоёв из PRD §8.1: `Cli` и `App` → `Infrastructure` → `Core`; ссылки из `Core` наружу запрещены. Тесты идут в основном CI.

## Критерии приёмки

Из issue:

- [x] Нарушение правила роняет тест
- [ ] Тесты входят в основной CI
- [ ] Покрыто тестами, CI зелёный

Добавлено из PRD и issue:

- [x] `Core` не ссылается на `GitHubBackup.Infrastructure`, `GitHubBackup.Cli`, `GitHubBackup.App`, Octokit, EF Core (включая `Microsoft.Data.Sqlite`), Serilog и WPF
- [x] `Infrastructure` не ссылается на `GitHubBackup.Cli` и `GitHubBackup.App`
- [x] Абстракции `Microsoft.Extensions.*` в `Core` разрешены, правило на них не срабатывает

## Изменения PRD

Не требуются.

## Затрагиваемые проекты и типы

- `src/Directory.Packages.props`: `NetArchTest.Rules` в группе `Tests`.
- `GitHubBackup.ArchitectureTests`:
  - `LayerRules` — единственное описание правил (сборка, запрещённые пространства имён, запрещённые сборки);
  - `ForbiddenReferenceFinder` — совпадение имени сборки по точному имени или по префиксу `имя.`;
  - `CoreDependencyTests`, `InfrastructureDependencyTests` — theory по каждому правилу;
  - `Fixtures/HostDependentFixture` — намеренный нарушитель только в тестовом проекте;
  - `RuleDetectionTests` — негативные тесты;
  - `SolutionAssemblyTests` — без изменений.
- Продакшен-проекты не меняются.

## Подход

1. Пакет `NetArchTest.Rules` через Central Package Management.
2. Два механизма: зависимости типов (NetArchTest) и прямые ссылки сборки (`GetReferencedAssemblies`). Второй ловит типы в глобальном пространстве имён и WPF-сборки.
3. Только прямые ссылки, без транзитивных.
4. «Нарушение роняет тест» доказывают негативные тесты: те же правила из `LayerRules` применяются к `HostDependentFixture` (ссылка на `GitHubBackup.App.MainWindow`) и к синтетическим `AssemblyName`.
5. `Microsoft.Extensions.*` и `System.Windows` в запретный список не входят (`System.Windows.Input.ICommand` лежит не в WPF).
6. Пустые сборки: NetArchTest проходит без проверки; работоспособность механизма доказывают негативные тесты.

## Тесты

- `CoreDependencyTests`: theory по запрещённым пространствам имён и по запрещённым сборкам.
- `InfrastructureDependencyTests`: то же для `GitHubBackup.Cli` и `GitHubBackup.App`.
- `RuleDetectionTests`: нарушитель падает по пространству имён; нарушитель ссылается на `GitHubBackup.App` и `PresentationFramework`; запрещённые имена (с суффиксом) находятся; разрешённые (`Microsoft.Extensions.*`, `System.Runtime`, `GitHubBackup.Core`, `SerilogX`) не находятся.

## Вне рамок

- Транзитивные и пакетные зависимости.
- Независимость `Cli` и `App` друг от друга, правила пар тестовых проектов.
- Исправление `ci.yml` (закрепление actions, `timeout-minutes`).

## Решения

- Список «библиотек ввода-вывода» для `Core`: Octokit, EF Core, `Microsoft.Data.Sqlite`, `SQLitePCLRaw`, Serilog, WPF. `System.IO.Abstractions` разрешён.
- Пакет — оригинальный `NetArchTest.Rules` (PRD §8.3).
- Проверка «правило увидело хотя бы один тип» не добавляется.
- Проверяются только прямые ссылки.

## Задачи

- [x] Добавить `NetArchTest.Rules` в `src/Directory.Packages.props` и `PackageReference` в `GitHubBackup.ArchitectureTests.csproj`
- [x] `LayerRules` для `Core` и `Infrastructure` с XML-документацией
- [x] `ForbiddenReferenceFinder`
- [x] `CoreDependencyTests`
- [x] `InfrastructureDependencyTests`
- [x] `Fixtures/HostDependentFixture`
- [x] `RuleDetectionTests`
- [x] Сборка, тесты проекта, тесты решения как в CI, `dotnet format --verify-no-changes`
- [x] Прогнать после #4 и #5 на ветке пакета
- [x] Отметить задачи в этом файле
