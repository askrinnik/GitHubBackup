# Бенчмарк AI Harness

Результаты скилла `harness-kit:harness-quality-check` из плагина `harness-kit`: сколько стоит харнесс
(контекст в начале сессии, токены, которые добавляет чтение файла, загруженные правила) и работает ли он
(ответы, оценённые судьёй по эталону). Подробное описание — в `docs/ai/README.md`.

- `fixtures/` — тесты: по папке на фикстуру (`prompt.txt`, при необходимости `fixture.json`, `seed.patch`,
  `ground-truth.md`).
- `run-history.csv` — по строке на сессию. Только дописывается: строки не правятся и не удаляются.
- `runs/` — ответ каждой сессии и манифест каждого прогона.
- `reports/` — отчёт по каждому прогону со сравнением с предыдущим прогоном каждой фикстуры.

Запуск через скилл (`/harness-kit:harness-quality-check bench-* "<что изменилось>"`; без фикстур запускаются все) или напрямую
из корня репозитория:
`pwsh "<папка плагина harness-kit>/skills/harness-quality-check/scripts/Run-Harness.ps1" -Fixture 'bench-*' -Note "<what changed>"`.
Скрипт и его описание — в плагине (`skills/harness-quality-check/README.md`); фикстуры, история и отчёты остаются здесь.

## Фикстуры

| Фикстура | Что проверяет |
|---|---|
| `bench-base` | Размер контекста в начале сессии по областям: инструменты, системный промпт, `CLAUDE.md`, память, скиллы, агенты, MCP |
| `bench-cs` | Сколько токенов добавляет чтение стабильного файла `Core` (`src/GitHubBackup.Core/ExitCode.cs`) вместе с правилами по путям, которые оно подтягивает (`csharp`, `security`) |
| `bench-test` | То же для стабильного файла тестового проекта (`src/GitHubBackup.Core.Tests/Processes/ProcessResultTests.cs`; правила `csharp`, `tests`) |
| `quality-prd-first` | Следует ли ассистент правилу PRD-first: на запрос новой функции предлагает сначала изменить PRD, а не пишет код |
| `quality-service-convention` | Проектирует ли ассистент новый сервис (очистка `<sourcesRoot>\.tmp\`, FR-7.4) по соглашениям репозитория: интерфейс в `Core`, регистрация в DI в методе слоя, `IFileSystem` и `TimeProvider`, XML-документация, тест в парном тестовом проекте. Когда сервис появится в решении, фикстуре нужна новая цель |
| `quality-security` | Находит ли ревью безопасности семь уязвимостей, которые `seed.patch` подбрасывает в `GitClient`, `GitEnvironment`, `RepositoryPathResolver` и `SevenZipArchiver`: токен в аргументах git, в логе и в URL remote, нет `GIT_TERMINAL_PROMPT=0`, путь из имени репозитория без проверки границ, ветка с `-` в аргументах git, имя архива с выходом за пределы папки. Патч нужно обновлять, когда эти файлы меняются |
| `quality-docs-sync` | Называет ли ассистент документы, которые нужно обновить, когда `seed.patch` добавляет параметр `backup --no-color` и настройку `Backup:CompressionLevel`: `docs/PRD.md` (FR-9.2, §7.2, версия и история) и `README.md`, без лишних документов. Патч нужно обновлять, когда меняются `CliCommandLine.cs`, `BackupOptions.cs`, `BackupOptionsValidator.cs` или `appsettings.json` CLI |

Фикстура, которой нужен код решения и которая ещё не добавлена, — отдельный issue: #49 (F4.8).
