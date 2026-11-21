# План: #165 F3.22: Per-endpoint log levels with namespace overrides

- Issue: https://github.com/askrinnik/GitHubBackup/issues/165
- Лейн: Feature
- Сложность: L
- Дата: 2026-11-21

## 1. Цель

Вывод лога разделяется на четыре приёмника (далее — «эндпоинты»): File, Console (только CLI, stderr), LiveLog (только UI) и OpenTelemetry (только при `OpenTelemetry:Enabled`). У каждого эндпоинта свой `Level` и свой `Override` в `Serilog:Endpoints:<name>`. Уровень выбирается по четырём правилам: Override эндпоинта, затем глобальный Override, затем Level эндпоинта, затем `Serilog:MinimumLevel:Default`. Редактор настроек UI показывает и сохраняет четыре значения Level.

Требования: новое FR-10.7, а также FR-10.2, FR-10.5, FR-10.6, FR-12.3, FR-12.5 и §6.10, §6.12, §7.2.

## 2. Критерии приёмки

Из issue:
- PRD содержит текст из раздела «Изменение PRD»; версия PRD повышена, строка в истории изменений добавлена
- Уровень каждого из четырёх приёмников задаётся отдельно; незаданный уровень берётся из `Serilog:MinimumLevel:Default`
- `Override` приёмника перекрывает общий `Override` для своего приёмника; побеждает самый длинный префикс namespace; `Off` выключает приёмник или namespace
- Файл получает записи `Debug`/`Verbose`, даже если остальные приёмники и `Default` выше (общий порог)
- CLI пишет записи лога в stderr тем же форматом, что файл `.log`, с замаскированными секретами; stdout со сводкой не меняется
- OTLP получает записи по своим уровням; маскирование и атрибут `level` сохраняются
- Ошибки конфигурации `Serilog:Endpoints` останавливают запуск с кодом `2` и сообщением с именем ключа
- Раздел «Logging» редактора показывает и сохраняет четыре уровня; Save сохраняет `Override` и остальные ключи `appsettings.json`
- Раздел «Logging» выглядит правильно в темах Light и Dark
- README описывает `Serilog:Endpoints`, порядок правил и консольный вывод CLI
- Покрыто тестами (выбор уровня, общий порог, подлоггеры файла и ленты, фильтр OTLP через `CapturingLogExporter`, stderr CLI, проверка опций, ViewModel и Save редактора, FlaUI), CI зелёный
- Работа разбита на два коммита: часть 1 и часть 2

Добавлено по проверке покрытия:
- (добавлено) `Off` в `Serilog:MinimumLevel:Default` или в глобальном `Override` — ошибка конфигурации с текущим текстом сообщения.
- (добавлено) Строка вместо объекта — ошибка в трёх местах: `Serilog:Endpoints`, `Serilog:Endpoints:<name>`, `Serilog:Endpoints:<name>:Override` (в том числе из переменных окружения и командной строки).
- (добавлено) Пустой `Level` или `null` означает «не задан»; секция эндпоинта со значением `null` тоже. Редактор пишет `null` для пункта «Default».
- (добавлено) Совпадение Override учитывает границу по точке: `GitHubBackup` не подходит к `GitHubBackupX`. Запись без `SourceContext` проходит по правилам 3 и 4.
- (добавлено) Если OpenTelemetry выключено, OTLP-эндпоинт не подключается и не понижает общий порог. В UI не подключается Console, в CLI не подключается LiveLog.
- (добавлено) Ключи эндпоинта, который в этом хосте не подключается, всё равно проверяются при старте: оба exe читают один `appsettings.json`.
- (добавлено) Существующие тесты маскирования и атрибута `level` в OTLP остаются зелёными без изменений.

## 3. Затронутые проекты и типы

### Часть 1 (коммит 1)

**docs/PRD.md** — текст «Изменение PRD» из issue переносится дословно; версия 0.54, строка истории.

**GitHubBackup.Core**
- `Logging/LogEndpoint.cs` — enum `File, Console, LiveLog, OpenTelemetry`.
- Ключи секций `Serilog…`/`OpenTelemetry…` не заводить в Core как `const` (правило слоёв ловит строки с именем пространства имён SDK).

**GitHubBackup.Infrastructure/Logging**
- `SerilogEndpointOptions` — `Level` (`string?`), `Override` (`Dictionary<string,string?>`, `OrdinalIgnoreCase`).
- `SerilogEndpointsOptions` — `SectionName = "Serilog:Endpoints"`, свойства `File`, `Console`, `LiveLog`, `OpenTelemetry`.
- `SerilogEndpointsOptionsValidator : IValidateOptions<SerilogEndpointsOptions>` с внедрённым `IConfiguration`: структуру (объект, а не строка; известные имена) проверяет по сырой секции. Сообщения начинаются с полного ключа, например `Serilog:Endpoints:File:Level must be one of Verbose, Debug, Information, Warning, Error, Fatal, Off.`
- `LogLevels` (internal static) — имена уровней с `Off` и без, разбор строки в `LogEventLevel?` (`null` = Off). `SerilogOptionsValidator` переходит на него, тексты не меняются.
- `EndpointLevelResolver` (internal sealed) — четыре правила; `Resolve(string? sourceContext)` с кешем, `IsEnabled(LogEvent)`, статический `LowestLevel(...)` для общего порога (без `Off` и без неподключённых эндпоинтов).
- `TextWriterLogSink : ILogEventSink` — `MaskingTextFormatter(new TextLogFormatter(), masker)` в `TextWriter` под lock.
- `ConsoleLogTarget` (internal record с `TextWriter`) — маркер, который регистрирует CLI.
- `LiveLogOptions.Enabled` — выставляет `AddGitHubBackupLiveLogBuffer` (хост с лентой).
- `FileLoggingServiceCollectionExtensions`: регистрация опций, валидатора, `ValidateOnStart`; `AddGitHubBackupConsoleLog(TextWriter)`; `ConfigureLogger` — корень `MinimumLevel.Is(lowest)` без `Override`, `RunIdEnricher`, по одному `WriteTo.Logger(...)` на подключённый эндпоинт; `AddSerilog(..., writeToProviders: false)`.
- `StartupLogging` не меняется.

**GitHubBackup.Infrastructure/Telemetry** — `TelemetryServiceCollectionExtensions`: только `<remarks>`.

**GitHubBackup.Cli** — `AddGitHubBackupCli` вызывает `services.AddGitHubBackupConsoleLog(console.Error)`.

**README.md** — ключи `Serilog:Endpoints:*`, порядок правил, пример с Override эндпоинта, выключение консоли, stderr CLI; исправить фразу об отсутствии отдельного уровня OTLP.

### Часть 2 (коммит 2)

**GitHubBackup.Core/Logging** — `EndpointLogLevel` (семь значений с `Off`), `LogLevelSettings` (четыре `EndpointLogLevel?`), `ILogLevelSettingsStore` (`Current`, `DefaultLevel`, `SaveAsync`).

**GitHubBackup.Infrastructure/Configuration**
- `AppSettingsFile`: вложенный путь секции через `:` с созданием промежуточных объектов; запись нескольких секций одной атомарной записью (один `.bak`); `WriteSectionValuesAsync` делегирует ей.
- `JsonLogLevelSettingsStore` по образцу `JsonTelemetrySettingsStore`; регистрация в `InfrastructureServiceCollectionExtensions`.

**GitHubBackup.App**
- `SettingsSection.Logging` между `Telemetry` и `Token`.
- `LogLevelOption(EndpointLogLevel? Value, string Label)` по образцу `ThemeOption`.
- `SettingsViewModel`: `ILogLevelSettingsStore`, `LoggingSection` (значок Segoe Fluent), `LogLevelOptions` (первый пункт `Default (<DefaultLevel>)`), четыре `[ObservableProperty]`, флаг изменений; Save пишет раздел после телеметрии и до токена; `IOException` → существующий диалог.
- `SettingsView.xaml`: карточка по образцу «Telemetry», заметка о перезапуске и об Override только в `appsettings.json`, четыре ComboBox «File», «Console (CLI)», «Live log (UI)», «OpenTelemetry». AutomationId: `SettingsLoggingGroup`, `SettingsLoggingCard`, `LogLevelFileComboBox`, `LogLevelConsoleComboBox`, `LogLevelLiveLogComboBox`, `LogLevelOpenTelemetryComboBox`, `LoggingRestartNotice`.

**README.md** — раздел Logging редактора настроек.

## 4. Подход

1. PRD: перенос текста, версия, история.
2. Опции и валидатор с тестами. Ошибки доходят до кода 2 по существующему пути (`OptionsValidationException` → `ConfigurationErrorMessages.TryGet`). Ошибки `Serilog:MinimumLevel` и `Serilog:Endpoints` собираются вместе в одно исключение.
3. `EndpointLevelResolver` с unit-тестами.
4. Конвейер Serilog:
   - корень `MinimumLevel.Is(lowest)` без `Override`;
   - эндпоинт — `WriteTo.Logger(lc => lc.MinimumLevel.Verbose().Filter.ByIncludingOnly(resolver.IsEnabled)...)` (`Verbose()` обязателен: у вложенного логгера свой порог);
   - обогащение в корне;
   - `Level = Off` — `WriteTo.Logger` не вызывается;
   - File — один вложенный логгер на `.log` и `.json`;
   - LiveLog — только при `LiveLogOptions.Enabled`;
   - Console — только если в DI есть `ConsoleLogTarget`;
   - OTLP — только при `OpenTelemetryOptions.Enabled` и наличии `ILoggerProvider`: `LoggerProviderCollection` + `WriteTo.Providers`; коллекцию не диспозить (провайдеры принадлежат DI); проверить, что провайдеры не вызываются дважды.
5. CLI: регистрация консольного эндпоинта; сводка и вопросы остаются в stdout.
6. README. Коммит 1.
7. Часть 2: `AppSettingsFile`, store, ViewModel, XAML, UI-тесты, README. Коммит 2.

## 5. Тесты

**Infrastructure.Tests/Logging**
- `EndpointLevelResolverTests`: приоритет правил 1 > 2 > 3 > 4, самый длинный ключ, граница `.`, регистр, нет `SourceContext`, Override `Off`, `LowestLevel`.
- `SerilogEndpointsOptionsValidatorTests`: неверный Level и значение Override, `Off` разрешён, строка вместо секции (три места), неизвестное имя, `null` и `""`, порядок ошибок.
- `SerilogOptionsValidatorTests`: `Off` в `Default` и в `Override` → ошибка.
- `TextWriterLogSinkTests`: формат `.log`, маскирование.
- `FileLoggingServiceCollectionExtensionsTests`: binding, `LiveLogOptions.Enabled`.

**Infrastructure.IntegrationTests/Logging**
- `SerilogFileLoggingTests`: общий порог (File Debug при Default Warning), File `Off`, Override эндпоинта сильнее общего, LiveLog со своим уровнем; существующий тест ленты получает `AddGitHubBackupLiveLogBuffer`.
- `OtlpLogExportTests` (`CapturingLogExporter`): OTLP `Error` при `Default: Information`; Override `Off`; существующие тесты маскирования и `level` без изменений.

**Cli.Tests**
- `CliApplicationTests`: stderr по умолчанию (формат `.log`, токен замаскирован), Console `Off` → пустой stderr, `--silent` → лог в stderr, ошибка `Serilog:Endpoints` → код 2.
- Существующие тесты, которые ждут пустой stderr или точный набор строк, получают `"Console": { "Level": "Off" }` или фильтр строк лога.
- `CliEndToEndTests`: настоящий exe пишет лог в stderr, stdout не меняется.

**App.Tests** — `AppHostTests`: в UI-хосте есть LiveLog и нет Console.

**Часть 2**
- `AppSettingsFileTests`: вложенный путь, сохранение чужих ключей и Override, регистр ключей, строка на месте секции, одна запись и один `.bak`.
- `JsonLogLevelSettingsStoreTests`: `Current` до и после Save, `DefaultLevel`, «Default» → `null`.
- `SettingsViewModelTests`: порядок навигации, подпись `Default (...)`, начальный выбор, Save с изменениями и без, `IOException`.
- FlaUI `SettingsEditorTests`: выбор уровней, Save, проверка файла.
- `SettingsEditorScreenshotTests`: раздел Logging в Light и Dark.

## 6. Проверка

| Пункт | Как проверить |
|---|---|
| PRD | diff `docs/PRD.md` |
| Отдельные уровни и Default | `EndpointLevelResolverTests`, `SerilogFileLoggingTests` |
| Override эндпоинта, longest prefix, Off | `EndpointLevelResolverTests`, интеграционные тесты File, LiveLog, OTLP |
| Общий порог | `SerilogFileLoggingTests` |
| CLI stderr | `CliApplicationTests`, `CliEndToEndTests` |
| OTLP | `OtlpLogExportTests` |
| Код 2 | `SerilogEndpointsOptionsValidatorTests`, `CliApplicationTests` |
| Редактор | `SettingsViewModelTests`, `JsonLogLevelSettingsStoreTests`, `AppSettingsFileTests`, FlaUI |
| Light и Dark | `SettingsEditorScreenshotTests` |
| README | ревью diff |
| CI | build, тесты без Smoke, format, UI-тесты; CI пул-реквеста |
| Два коммита | `git log` |

## 7. Вне объёма

- Перечитывание уровней без перезапуска.
- Редактирование Override в UI.
- `StartupLogging`.
- Цвет и отдельный формат консольного вывода.

## 8. Решения

- Неизвестное имя эндпоинта (`Serilog:Endpoints:Fiel`) — ошибка конфигурации, код 2. Основание: одобренный текст §7.2 («имя — одно из `File`, `Console`, `LiveLog`, `OpenTelemetry`»).
- Ключи Override сравниваются без учёта регистра, как ключи конфигурации .NET и словарь `SerilogOptions`.
- Ошибки `Serilog:MinimumLevel` и `Serilog:Endpoints` выводятся вместе за один запуск.
- Консоль CLI не выводит стек исключения: после записи идёт строка `<тип>: <сообщение>`, полное исключение только в файле. Так stderr при ошибке старта не противоречит §7.2 и FR-9.10 («без стека»). Решение пользователя после реализации части 1; фраза добавлена в FR-10.7.
- Корень Serilog без `Override`; общий порог — минимум всех уровней подключённых эндпоинтов.
- Пользователь поручил пройти все шаги без остановок, если у планирования нет открытых вопросов; открытые вопросы планировщика решены выше по PRD и соглашениям проекта.

## 9. Задачи

**Часть 1**
- [x] Перенести текст «Изменение PRD» в `docs/PRD.md`, поднять версию, добавить строку истории
- [x] `LogEndpoint`; `LogLevels`, опции эндпоинтов, валидатор; тесты
- [x] `EndpointLevelResolver` с `LowestLevel`; тесты
- [x] `TextWriterLogSink`, `ConsoleLogTarget`, `AddGitHubBackupConsoleLog`, `LiveLogOptions.Enabled`
- [x] Переписать `ConfigureLogger`: общий порог, вложенные логгеры, OTLP через `LoggerProviderCollection`
- [x] CLI: консольный эндпоинт на stderr
- [x] Интеграционные тесты, тесты CLI, правка тестов с пустым stderr
- [x] README
- [x] Сборка, тесты, format; коммит 1

**Часть 2**
- [x] `EndpointLogLevel`, `LogLevelSettings`, `ILogLevelSettingsStore`
- [x] `AppSettingsFile`: вложенный путь и запись нескольких секций; тесты
- [x] `JsonLogLevelSettingsStore` и регистрация; тесты
- [x] `SettingsSection.Logging`, `LogLevelOption`, `SettingsViewModel`; тесты
- [x] Раздел Logging в `SettingsView.xaml`
- [x] FlaUI и скриншот-тесты (Light, Dark)
- [x] README: раздел Logging редактора
- [x] Сборка, тесты, format, UI-тесты; коммит 2
