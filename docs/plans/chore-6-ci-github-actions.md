# План: #6 F0.5: CI with GitHub Actions

| Поле | Значение |
|---|---|
| Issue | [#6](https://github.com/askrinnik/GitHubBackup/issues/6) |
| Заголовок | F0.5: CI with GitHub Actions |
| Тип | chore |
| Сложность | S |
| Дата | 2026-10-03 |

## 1. Цель

Workflow на `windows-latest` для push и pull request: restore, build, проверка форматирования, тесты без UI и smoke, публикация результатов. Отдельный workflow с ручным запуском для UI- и smoke-тестов. Категории тестов — трейты xUnit (PRD §11).

## 2. Критерии приёмки

- [x] CI зелёный на `main` — локально прогнаны те же команды; зелёный запуск на GitHub появится на PR (проверяется `gh pr checks`)
- [x] UI/smoke-тесты не запускаются в основном workflow — `--filter-not-trait "Category=UI"` и `"Category=Smoke"`
- [x] Покрыто тестами — `AppAssemblyTests.TestAssembly_HasUiCategoryTrait_SoMainCiWorkflowSkipsIt`

## 3. Подход

- `src/Directory.Build.props`: каждому проекту `*Tests` проставляется сборочный трейт `Category` = `Unit` | `Integration` | `Architecture` | `UI` по имени проекта. Контрактные и snapshot-тесты живут в `*.Tests` и получают `Unit`. Категория `Smoke` зарезервирована для будущих тестов реального GitHub.
- `.github/workflows/ci.yml`: push и pull request; restore, build, `dotnet format --verify-no-changes`, `dotnet test` с исключением `UI` и `Smoke`, `--ignore-exit-code 8` (проект, где все тесты отфильтрованы, возвращает 8), TRX-отчёты как артефакт `test-results`.
- `.github/workflows/ui-smoke-tests.yml`: `workflow_dispatch` с выбором категории (`UI` или `Smoke`); пока UI-тест один, Smoke-тестов нет — заглушка.
- `.gitignore`: `TestResults/`.
- Документация: `README.md` (раздел CI, категории), `CLAUDE.md` (команда как в CI).

## 4. Вне рамок

Публикация (single-file), покрытие кода, кэширование NuGet, реальные UI- и smoke-тесты.

## 5. Задачи

- [x] Трейты `Category` в `Directory.Build.props`
- [x] `ci.yml`
- [x] `ui-smoke-tests.yml`
- [x] Тест трейта в `App.UITests`
- [x] `.gitignore`, `README.md`, `CLAUDE.md`
- [x] Build, тесты, format локально
- [ ] Зелёный CI на PR
