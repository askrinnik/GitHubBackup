# План: F5.1: Application publishing

| Поле | Значение |
|---|---|
| Issue | [#43](https://github.com/askrinnik/GitHubBackup/issues/43) |
| Лейн | Feature (`area:build`, `type:chore`) |
| Сложность | L |
| Дата | 2026-11-14 |
| Требования | [PRD §7.1](../PRD.md#71-раскладка-папки-приложения), [§10](../PRD.md#10-нефункциональные-требования): NFR-8 |

## 1. Цель

Оба хоста публикуются в одну папку приложения по раскладке PRD §7.1: `GitHubBackup.exe` (WPF), `GitHubBackup.Cli.exe` и один `appsettings.json`. Оба exe — framework-dependent single-file: им нужен только установленный .NET 10 (NFR-8). Папку собирает скрипт `scripts/publish.ps1`. CI вызывает тот же скрипт и выкладывает папку артефактом.

## 2. Критерии приёмки

- [ ] Опубликованная папка запускается на чистой машине с .NET 10
- [ ] Покрыто тестами, CI зелёный
- [x] (добавлено) В корне папки ровно три файла: `GitHubBackup.exe`, `GitHubBackup.Cli.exe`, `appsettings.json`. Подпапок, `.dll`, `.pdb`, `.xml`, `*.runtimeconfig.json` и `*.deps.json` нет.
- [x] (добавлено) Опубликованный `appsettings.json` совпадает с `src/GitHubBackup.Cli/appsettings.json` байт в байт.
- [x] (добавлено) Опубликованный `GitHubBackup.Cli.exe` выполняет `--help` и полный прогон `backup --silent`; он создаёт `data\history.db` (миграции EF Core и нативный SQLite работают в single-file) и `logs\` рядом с exe.
- [x] (добавлено) Опубликованный `GitHubBackup.exe` запускается сценарием `AppLaunchTests` в workflow UI-тестов.
- [ ] (добавлено) Если папка вывода существует и не пуста, а `-Clean` не передан, скрипт завершается с ненулевым кодом. При ошибке `dotnet publish` код тоже ненулевой.

## 3. Изменения требований

Не нужны. §7.1 уже называет `GitHubBackup.exe`, NFR-8 описывает вид публикации.

## 4. Затронутые проекты и типы

**Корень репозитория**
- `scripts/publish.ps1` (новый, pwsh). Параметры: `-OutputPath` (по умолчанию `artifacts\publish` от корня репозитория), `-Configuration` (по умолчанию `Release`), `-Clean`. Публикует `GitHubBackup.Cli` и `GitHubBackup.App` с `-r win-x64` в одну папку.
- `.github/workflows/ci.yml`: шаг `Publish` перед `Test`, переменная `PUBLISHED_APP_PATH` для `Test`, загрузка артефакта `GitHubBackup` (действие закреплено по SHA, `if-no-files-found: error`).
- `.github/workflows/ui-smoke-tests.yml`: шаг `Publish` и та же переменная, чтобы UI-тесты запускали опубликованный `GitHubBackup.exe`.

**src/**
- `GitHubBackup.Cli/GitHubBackup.Cli.csproj`, `GitHubBackup.App/GitHubBackup.App.csproj`: `PublishSingleFile=true`, `SelfContained=false`, `IncludeNativeLibrariesForSelfExtract=true`, без doc-файлов; `ExcludeFromSingleFile` на `appsettings.json`. Сборка App остаётся `GitHubBackup.App`; имя `GitHubBackup.exe` даёт скрипт.
- `Directory.Build.props`: `DebugType=embedded` для продуктовых проектов; `EnableSingleFileAnalyzer=true` для `Core` и `Infrastructure`.
- `GitHubBackup.Cli.Tests`: `AssemblyMetadata` `GitHubBackupRepositoryRoot`; фикстура `PublishedApplication` (берёт `PUBLISHED_APP_PATH` или запускает скрипт во временную папку); `PublishedApplicationTests`; `CliInstallation` принимает исходную папку.
- `GitHubBackup.App.UITests/AppInstallation.cs`: при `PUBLISHED_APP_PATH` копирует опубликованную папку и запускает `GitHubBackup.exe`.

**Документация**
- `README.md`: раздел «Публикация», требование .NET 10 Desktop Runtime, строки CI-таблицы, абзац о тестах публикации.
- `CLAUDE.md`: команда публикации в «Build and test», `scripts/` в «Repository layout».

## 5. Подход

1. `RuntimeIdentifier` в csproj не задаётся: его передаёт скрипт (`-r win-x64`). Иначе путь вывода сборки получит суффикс RID и сломает пути к папкам сборки в тестах.
2. `AssemblyName` не меняется (на имя сборки опираются архитектурные тесты и лог). SDK не учитывает `PublishedSingleFileName` для framework-dependent single-file, поэтому скрипт переименовывает `GitHubBackup.App.exe` в `GitHubBackup.exe` после публикации.
3. `e_sqlite3.dll` входит в bundle (`IncludeNativeLibrariesForSelfExtract`) и распаковывается при запуске. `IncludeAllContentForSelfExtract` не включается: content root (`AppContext.BaseDirectory`) должен оставаться папкой приложения.
4. В `src` нет `Assembly.Location`; Serilog настроен в коде; миграции EF Core ищутся отражением без trimming — это совместимо с single-file.
5. Скрипт: `Set-StrictMode`, `$ErrorActionPreference = 'Stop'`, проверка `$LASTEXITCODE`; `--artifacts-path` во временную папку изолирует obj/bin от сборки разработчика. Непустую папку вывода скрипт не перезаписывает без `-Clean`: это защищает установленную папку с `backup-config.json`, `data\` и `sources\`. `-Clean` удаляет только файлы публикации и отказывается работать с папкой, где есть что-то ещё (по итогам проверки безопасности). Скрипт падает, если опубликованный `appsettings.json` задаёт `GitHub:Token`.
6. CI: Build → Format → Hooks → Publish → Test (с `PUBLISHED_APP_PATH`) → результаты тестов → артефакт. Артефакт выкладывается на каждом запуске, включая PR.

## 6. Тесты

`GitHubBackup.Cli.Tests`, класс `PublishedApplicationTests` (`Category=EndToEnd`, коллекция `BackupRunLockTestGroup`):
- `PublishedFolder_TopLevelEntries_AreTwoExecutablesAndAppSettings`
- `PublishedFolder_AppSettings_EqualsSourceAppSettings`
- `CliExecutable_Help_ExitsSuccessWithUsage`
- `CliExecutable_SilentBackupTrackedRepository_ArchivesAndCreatesHistoryAndLogs`
- `PublishScript_NonEmptyOutputWithoutClean_FailsAndKeepsFiles`

`GitHubBackup.App.UITests`: новых тестов нет; с `PUBLISHED_APP_PATH` существующие сценарии гоняют опубликованный `GitHubBackup.exe`.

## 7. Проверка

| Пункт | Как |
|---|---|
| Раскладка, `appsettings.json` | `PublishedFolder_*` |
| CLI, SQLite, миграции, логи | `CliExecutable_*` |
| WPF из опубликованной папки | `ui-smoke-tests.yml` (категория UI) после публикации |
| Чистая машина с .NET 10 | вручную: артефакт в Windows Sandbox или ВМ только с .NET 10 Desktop Runtime |
| CI зелёный | `ci.yml` на PR, артефакт виден в запуске |

## 8. Вне рамок

- Self-contained, trimming, ReadyToRun, x86 и arm64 — цель только `win-x64`.
- Установщик, подпись кода, релизы GitHub, версионирование по тегам.
- Автоматическая проверка на машине без SDK (раннер содержит SDK).
- Smoke-тесты против реального GitHub.

## 9. Решения

- Целевая платформа — только `win-x64`.
- `e_sqlite3.dll` входит в bundle, рядом с exe его нет.
- Артефакт CI выкладывается на каждом запуске, включая PR.
- Пользователь разрешил сразу переходить к реализации, если по плану нет вопросов; вопросы выше — допущения, а не блокеры.

## 10. Задачи

- [x] Хост-csproj: свойства single-file, `ExcludeFromSingleFile` на `appsettings.json`; переименование exe App в скрипте
- [x] `Directory.Build.props`: `DebugType=embedded`, `EnableSingleFileAnalyzer` для `Core` и `Infrastructure`
- [x] `scripts/publish.ps1`
- [x] Ручная публикация: раскладка совпадает с §7.1
- [x] `CliInstallation`, `AssemblyMetadata` `GitHubBackupRepositoryRoot`
- [x] `PublishedApplication` и `PublishedApplicationTests`
- [x] `AppInstallation`: опубликованная папка при `PUBLISHED_APP_PATH`
- [x] `ci.yml`: Publish, env для Test, артефакт
- [x] `ui-smoke-tests.yml`: Publish и env для Test
- [x] README и CLAUDE.md
- [x] Сборка, формат, тесты решения без Smoke
