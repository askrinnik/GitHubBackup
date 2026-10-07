# План: #4 — F0.3: Generic Host, DI and configuration in the CLI

- Issue: [#4](https://github.com/askrinnik/GitHubBackup/issues/4)
- Линия: Feature (`type:feature`, `area:infrastructure`, `area:cli`)
- Сложность: L
- Дата: 2026-10-07
- PRD: §7.2, §8.3 (также §8.1, NFR-6, FR-11.1, FR-9.10)

## Цель

Перевести `GitHubBackup.Cli` на Generic Host (`Microsoft.Extensions.Hosting`) с DI и конфигурацией по §7.2 и §8.3. Источники (по возрастанию приоритета): `appsettings.json` рядом с exe, user-secrets (только Development), переменные окружения `GitHubBackup__`, командная строка. Секции `GitHub`, `Tools`, `Backup`, `History`, `OpenTelemetry` привязываются к опциям с `ValidateOnStart`. Относительные пути считаются от папки exe. Регистрация вынесена в `AddGitHubBackupCore()` и `AddGitHubBackupInfrastructure()`.

## Критерии приёмки

Из issue:

- [x] Невалидная конфигурация приводит к понятной ошибке при старте
- [x] Значение из переменной окружения переопределяет `appsettings.json` (тест)
- [x] Регистрация сервисов вынесена в методы расширения
- [ ] Покрыто тестами, CI зелёный

Добавлено:

- [x] (FR-9.10) Ошибка конфигурации завершает процесс с кодом `2`; stderr — по строке на ошибку с именем ключа, без стека
- [x] (§7.2) Относительные `Backup:ConfigPath` и `History:DatabasePath` становятся абсолютными от папки exe
- [x] (FR-11.1, §9) Командная строка `--Section:Key=value` переопределяет переменную окружения и `appsettings.json`
- [x] (NFR-1) Значение `GitHub:Token` не попадает ни в одно сообщение об ошибке

## Изменения PRD

Требуется уточнение §7.2 (версия 0.4 → 0.5, строка в истории изменений), до начала кода:

1. Источники и приоритет: `appsettings.json` → user-secrets (только `Development`) → переменные `GitHubBackup__<Section>__<Key>` → командная строка. Переменные без префикса и `appsettings.{Environment}.json` не читаются. Окружение задаёт `DOTNET_ENVIRONMENT`, по умолчанию `Production`.
2. Правила проверки (ошибка → код `2`): `GitHub:ApiBaseUrl` — абсолютный http/https URI; `Tools:GitPath` и `Tools:SevenZipPath` — абсолютные, если заданы; `Backup:ConfigPath`, `History:DatabasePath` — не пустые; `Backup:ShortHashLength` — от 4 до 40; `OpenTelemetry:OtlpEndpoint` — абсолютный URI при `Enabled = true`.
3. `appsettings.json` обязателен.

## Затрагиваемые проекты и типы

- `src/Directory.Packages.props`: `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Options.ConfigurationExtensions`, `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.DependencyInjection` (последние два — для тестов Core).
- `GitHubBackup.Core`: `Configuration/BackupOptions`, `BackupOptionsValidator`, `ExitCode`, `CoreServiceCollectionExtensions.AddGitHubBackupCore`.
- `GitHubBackup.Infrastructure`: `GitHubOptions` (без `Token`), `ToolsOptions`, `HistoryOptions`, `OpenTelemetryOptions`, их валидаторы, `ContentRootPathPostConfigure`, `InfrastructureServiceCollectionExtensions.AddGitHubBackupInfrastructure`, `Hosting/GitHubBackupHostSettings`, `Hosting/GitHubBackupHostBuilder`.
- `GitHubBackup.Cli`: `Program.cs`, `CliApplication`, `appsettings.json` (копируется в output), `<UserSecretsId>`.

## Подход

1. PRD, затем пакеты.
2. Core: опции `Backup`, валидатор, `ExitCode`, расширение.
3. Infrastructure: остальные опции, валидаторы, post-configure путей, расширение, построитель хоста.
4. Хост: `DisableDefaults = true`, `Args = null`; источники добавляются явно в порядке выше, командная строка последней. `ValidateOnBuild` и `ValidateScopes` включены.
5. Папка exe — `ContentRootPath` в настройках хоста (`IHostEnvironment`); отдельный `IAppPaths` не нужен.
6. Ошибки старта: `OptionsValidationException`, ошибки привязки типа, битый JSON, отсутствие файла → строка `Configuration error: …` в stderr, `ExitCode.Critical`, без стека.
7. `Token` не входит в объект опций; провайдер токена читает `IConfiguration["GitHub:Token"]`.

## Тесты

- `Core.Tests`: `BackupOptionsValidatorTests` (границы 4/40, пустой путь), `CoreServiceCollectionExtensionsTests`.
- `Infrastructure.Tests`: валидаторы всех секций, `ContentRootPathPostConfigureTests`, `InfrastructureServiceCollectionExtensionsTests`.
- `Cli.Tests`: `InMemoryFileProvider`, `CliApplicationTests` (успех, диапазон, неверный тип, битый JSON, нет файла, командная строка, относительный путь, токен не в stderr), `EnvironmentVariableOverrideTests` в непараллельной коллекции.

## Вне рамок

- Serilog (#5); архитектурные правила (#7); `ITokenProvider`, `IToolLocator`, `IFileSystem`; `System.CommandLine` и команды; хост WPF; публикация single-file.

## Решения

Предложены планировщиком, ждут подтверждения:

- `appsettings.json` обязателен.
- Общий `UserSecretsId` `GitHubBackup` для Cli и App.
- `ExitCode` добавляется в Core здесь.
- `Token` исключён из `GitHubOptions`.
- `ShortHashLength` в диапазоне 4–40.
- `Backup` в Core, остальные секции в Infrastructure.

## Задачи

- [x] Согласовать и внести изменения PRD §7.2 (версия 0.5, строка истории)
- [x] Версии пакетов в `src/Directory.Packages.props`, проверка `--vulnerable`
- [x] Core: `BackupOptions`, `BackupOptionsValidator`, `ExitCode`, `AddGitHubBackupCore`
- [x] Infrastructure: опции и валидаторы `GitHub`, `Tools`, `History`, `OpenTelemetry`
- [x] Infrastructure: `ContentRootPathPostConfigure`
- [x] Infrastructure: `AddGitHubBackupInfrastructure`
- [x] Infrastructure: `GitHubBackupHostSettings`, `GitHubBackupHostBuilder`
- [x] Cli: `appsettings.json`, `UserSecretsId`, `CliApplication`, `Program.cs`
- [x] Тесты Core.Tests
- [x] Тесты Infrastructure.Tests
- [x] Тесты Cli.Tests
- [x] Сборка, тесты, `dotnet format`; проверка комментариев
- [ ] Передать в #7, какие ссылки Core на `Microsoft.Extensions.*` разрешены
