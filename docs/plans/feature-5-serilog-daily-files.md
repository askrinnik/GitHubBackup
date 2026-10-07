# План: #5 — F0.4: Serilog logging to daily files

- Issue: [#5](https://github.com/askrinnik/GitHubBackup/issues/5)
- Линия: Feature (`type:feature`, `area:infrastructure`)
- Сложность: L
- Дата: 2026-10-07
- PRD: §6.10 (FR-10.1, FR-10.2, FR-10.3, FR-10.6), §7.2, §8.3, П-9, NFR-1

## Цель

Подключить Serilog как провайдер `ILogger<T>` в общем построителе хоста. Файлы `logs\githubbackup-YYYYMMDD.log` (текст) и `logs\githubbackup-YYYYMMDD.json` (CLEF) в папке exe, по файлу в день, без автоудаления. Уровень берётся из `Serilog:MinimumLevel:Default`. В каждой записи есть `RunId`; есть scope для `Repository` и `Operation`. Токен и заголовки `Authorization` маскируются во всём выводе. Механизм маскирования общий, его позже использует `IProcessRunner`.

## Критерии приёмки

Из issue:

- [x] При запуске CLI создаются оба файла за текущую дату
- [x] Записи содержат ID запуска
- [x] Тест: токен в сообщении лога маскируется
- [ ] Покрыто тестами, CI зелёный

Добавлено:

- [x] (FR-10.2, П-9) Имена файлов точно `githubbackup-YYYYMMDD.log` и `.json`, папка `logs\` от content root, автоудаления нет
- [x] (§7.2) `Serilog:MinimumLevel:Default` меняет уровень
- [x] (FR-10.3) Свойства scope `Repository` и `Operation` попадают в оба файла
- [x] (FR-10.6) Маскируется заголовок `Authorization: Bearer|Basic|token <значение>` в шаблоне, свойствах и тексте исключения
- [x] (§7.2) Недопустимый уровень даёт `Configuration error: …` с именем ключа и код `2`

## Изменения PRD

§7.2, версия 0.5 → 0.6, строка в истории:

1. В «Проверку при старте»: `Serilog:MinimumLevel:Default` — одно из `Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal`.
2. Необязательно: `Serilog:MinimumLevel:Override:<SourceContext>` с теми же значениями.
3. Необязательно: секреты в логе заменяются на `***`.

## Затрагиваемые проекты и типы

- `src/Directory.Packages.props`: `Serilog.Extensions.Hosting`, `Serilog.Sinks.File`, `Serilog.Formatting.Compact`, `Microsoft.Extensions.Logging.Abstractions`.
- `GitHubBackup.Core` (без Serilog): `Runs/IRunContext`, `Runs/RunContext`, `Security/ISecretMasker`, `Security/SecretMasker`, `Logging/LogProperties`, `Logging/LoggerScopeExtensions`; регистрация `IRunContext` в `AddGitHubBackupCore`.
- `GitHubBackup.Infrastructure`: `GitHubOptions.TokenKey`, `Logging/SerilogOptions`, `LogFiles`, `MaskingTextFormatter`, `RunIdEnricher`, `LoggingServiceCollectionExtensions.AddGitHubBackupLogging`; регистрация `ISecretMasker` с токеном из конфигурации; вызов из `GitHubBackupHostBuilder.Create`.
- `GitHubBackup.Cli`: секция `Serilog` в `appsettings.json`, `CliLog`, события старта и завершения в `CliApplication`.

## Подход

1. PRD, затем пакеты.
2. Маскирование — чистая логика в Core: зарегистрированные значения (длинные первыми), regex заголовка `Authorization`, regex токенов GitHub (`gh[pousr]_…`, `github_pat_…`).
3. Маскирование на уровне `ITextFormatter`: обёртка форматирует событие в буфер, пропускает через `ISecretMasker.Mask` и пишет результат; покрывает оба файла, шаблон, свойства и исключение.
4. `RollingInterval.Day` даёт `githubbackup-yyyyMMdd.log`; `retainedFileCountLimit: null`, `shared: true`.
5. Уровни — собственная привязка `SerilogOptions` через `BindSection`, без `Serilog.Settings.Configuration`.
6. `AddSerilog(..., preserveStaticLogger: true)`, без статического `Log.Logger`; ошибка уровня всплывает при `Build()`.
7. Файлы появляются при старте: `CliApplication` пишет событие о старте запуска.
8. `RunId` — `Guid` на хост; enricher не перезаписывает значение из scope.
9. Тесты Cli переходят на временный content root, удаляемый в `Dispose`.

## Тесты

- `Core.Tests`: `SecretMaskerTests`, `RunContextTests`, `LoggerScopeExtensionsTests`, регистрация `IRunContext`.
- `Infrastructure.Tests`: `MaskingTextFormatterTests`, `RunIdEnricherTests`, `SerilogOptionsTests`, `ISecretMasker` из DI.
- `Infrastructure.IntegrationTests`: `SerilogFileLoggingTests` (имена файлов, `RunId`, маскирование, scope, уровни).
- `Cli.Tests`: `TemporaryContentRoot`, перевод существующих тестов, создание обоих файлов, `RunId`, токен не в файлах, недопустимый уровень → код 2.

## Вне рамок

- Логирование процессов (FR-10.4), регистрация токенов из Credential Manager и `gh`, OpenTelemetry, консольный sink, очистка логов, маскирование URL с учётными данными, `RunId` на каждый запуск внутри процесса UI.

## Решения

Предложены планировщиком, ждут подтверждения (вопросы 1–8):

1. Поддержать `MinimumLevel:Override` — да.
2. Собственная привязка уровней, без `Serilog.Settings.Configuration` — да.
3. Формат `RunId` — `Guid` в форме `D`.
4. Маскировать токены по формату GitHub — да.
5. Маскировать учётные данные в URL — нет.
6. Минимальная длина секрета — не вводить.
7. Текстовый шаблон: `{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {RunId} {SourceContext}: {Message:lj}{NewLine}{Exception}` плюс `{Properties:j}`.
8. Тесты с реальными файлами остаются под общим трейтом сборки.

## Задачи

- [x] Внести изменения PRD §7.2 (версия 0.6, строка истории)
- [x] Пакеты, ссылки в `.csproj`, `--vulnerable`
- [x] Core: `ISecretMasker`, `SecretMasker`
- [x] Core: `IRunContext`, `RunContext`, регистрация
- [x] Core: `LogProperties`, `LoggerScopeExtensions`
- [x] Infrastructure: `GitHubOptions.TokenKey`, регистрация `ISecretMasker`
- [x] Infrastructure: `SerilogOptions`, `LogFiles`, `MaskingTextFormatter`, `RunIdEnricher`
- [x] Infrastructure: `AddGitHubBackupLogging`, вызов из `GitHubBackupHostBuilder.Create`, проверка освобождения логгера
- [x] Cli: секция `Serilog`, `CliLog`, события старта и завершения
- [x] Тесты Core.Tests
- [x] Тесты Infrastructure.Tests
- [x] Тесты Infrastructure.IntegrationTests
- [x] Cli.Tests: `TemporaryContentRoot`, перевод тестов, новые тесты
- [x] Сборка, тесты, `dotnet format`; проверка комментариев
- [x] Ручная проверка: запуск CLI, оба файла в `logs\`
- [ ] Передать в #7: Core ссылается на `Microsoft.Extensions.Logging.Abstractions`, Serilog только в Infrastructure
