# План: #58 Handle unexpected start-up exceptions in the CLI without a stack trace

| Поле | Значение |
|---|---|
| Задача | [#58](https://github.com/askrinnik/GitHubBackup/issues/58) |
| Направление | Feature (метка `type:chore`) |
| Сложность | L |
| Дата | 2026-11-15 |

## 1. Цель

Сейчас `CliApplication.RunAsync` ловит только исключения, которые распознаёт `ConfigurationErrorMessages.TryGet`. Любое другое исключение при запуске уходит в среду выполнения. Тогда в stderr попадают полный текст исключения и стек, без маскирования. Это нарушает FR-9.10 (код `2` при критической ошибке), FR-10.6 и NFR-1 (токен никогда не выводится) и правило «без стека» из §7.2.

План добавляет последний `catch` на трёх этапах запуска: создание builder, `Build()`, `StartAsync()`. Он выводит одну строку `Startup error: …` с путём к логу и возвращает код `2`. Текст строки проходит через `ISecretMasker` с токеном из конфигурации. Полное исключение пишется только в файлы лога, в замаскированном виде.

## 2. Критерии приёмки

- [ ] Тест: неожиданное исключение при построении хоста даёт одну строку в stderr и код `2`, без стека
- [ ] Тест: токен в тексте исключения не попадает в stderr
- [ ] Тест: полное исключение записано в файл лога, токен в нём замаскирован
- [ ] Покрыто тестами, CI зелёный

Добавлено проверкой покрытия:

- [ ] (добавлено) Каждый этап по отдельности: исключение при создании builder, в `Build()` и в `StartAsync()` даёт одну строку `Startup error:`, код `2` и запись в лог.
- [ ] (добавлено) Токен, заданный только через `GitHub:Token` (не по шаблону `ghp_…`), маскируется в stderr и в обоих файлах лога (`.log` и `.json`).
- [ ] (добавлено) Многострочное сообщение исключения сворачивается в одну строку; пустое сообщение заменяется именем типа исключения.
- [ ] (добавлено) Если записать лог не удалось, строка всё равно выводится, код `2`, исключение наружу не выходит.
- [ ] (добавлено) Поведение не меняется для `Configuration error:` и для `OperationCanceledException` при отменённом токене.
- [ ] (добавлено) Смешанный `AggregateException` (ошибка валидации плюс другое исключение) даёт одну строку `Startup error:`; в логе есть все внутренние исключения.

## 3. Изменения требований

PRD описывает код `2` и вывод «без стека» только для ошибок правил валидации (§7.2, «Проверка при старте»). Вывод при неожиданной ошибке запуска PRD не описывает. Предложение (версия 0.31, строка в истории изменений): в §7.2 после абзаца «Проверка при старте» добавить:

> Любая другая ошибка при сборке или старте хоста CLI тоже завершает запуск с кодом `2`: в stderr выводится одна строка `Startup error: <сообщение> (log: <путь к логу>)` без стека, токен в ней заменён на `***`; полное исключение пишется только в лог (FR-10.6).

После этого в `README.md`, раздел «Exit codes» (строка про `2`): одно предложение о строке `Startup error: …` с путём к логу.

## 4. Затронутые проекты и типы

**GitHubBackup.Infrastructure**

- `Logging/FileLoggingServiceCollectionExtensions` (изменение): два вызова `WriteToDailyFile` выносятся в `internal static void WriteToDailyFiles(LoggerConfiguration, string contentRootPath, ISecretMasker)`. Его используют `ConfigureLogger` и запасной логгер.
- `Logging/StartupLogging` (новый, `public static`): `CreateLoggerFactory(string contentRootPath, ISecretMasker masker) : ILoggerFactory` — `SerilogLoggerFactory` (`dispose: true`) поверх `WriteToDailyFiles` и `RunIdEnricher(new RunContext())`, уровень Information. Нужен, когда логгера хоста ещё нет.
- `Hosting/HostSecretMasker` (новый, `public static`): `Create(IConfiguration configuration) : ISecretMasker`; сюда переносится логика `CreateSecretMasker`.
- `InfrastructureServiceCollectionExtensions` (изменение): регистрирует `ISecretMasker` через `HostSecretMasker.Create`; приватный `CreateSecretMasker` удаляется.

**GitHubBackup.Cli**

- `CliApplication` (изменение):
  - первичный конструктор `(CliConsole console, TimeProvider timeProvider, Func<GitHubBackupHostSettings, HostApplicationBuilder> createHostBuilder)`;
  - дополнительный конструктор `(CliConsole console) : this(console, TimeProvider.System, GitHubBackupHostBuilder.Create)`, так что `Program` и существующие тесты не меняются;
  - этапы создания builder, `Build()` и `StartAsync()` разделены, у каждого свой последний `catch`;
  - константа `StartupErrorPrefix = "Startup error: "` и метод `ReportStartupErrorAsync`;
  - `BuildHost(settings, console)` остаётся (его используют тесты).
- `CliLog` (изменение): событие `EventId = 3`, `LogLevel.Critical`, `"GitHubBackup.Cli failed to start"`, с исключением.

**Тесты**

- `GitHubBackup.Cli.Tests/CliApplicationTests` (изменение).
- `GitHubBackup.Infrastructure.Tests/Logging/StartupLoggingTests` (новый).
- `GitHubBackup.Infrastructure.Tests/Hosting/HostSecretMaskerTests` (новый).

**Документы:** `docs/PRD.md`, `README.md`, этот план.

## 5. Подход

1. PRD (§3), после одобрения.
2. Infrastructure: `WriteToDailyFiles`, `HostSecretMasker.Create`, `StartupLogging.CreateLoggerFactory`. Существующие тесты логирования остаются зелёными.
3. `CliApplication.RunAsync`: разбор командной строки без изменений, затем три этапа:

```
CreateBuilder ──(config)──> Configuration error (как сейчас)
     │        └─(other)──> Startup error; masker из fallback-конфигурации; лог через StartupLogging
     v
builder.Build() ─(config)─> Configuration error
     │          └─(other)─> Startup error; masker из builder.Configuration; лог через StartupLogging
     v
host.StartAsync ─(config)─> Configuration error
                └─(other, кроме отмены)─> Startup error; masker из builder.Configuration; лог через ILogger<CliApplication> хоста
```

- **Configuration error или Startup error.** `Configuration error:` — только то, что распознаёт `ConfigurationErrorMessages.TryGet`; эти строки не меняются. Всё остальное, включая смешанный `AggregateException` и `ArgumentException` от `Path.GetFullPath`, — `Startup error:`. `ConfigurationErrorMessages` общий с App и не меняется.
- **Текст строки:** `Startup error: <сообщение> (log: <путь>)`.
  - Сообщение — `exception.Message`; для `AggregateException` — сообщение первого внутреннего исключения, которое `TryGet` не распознаёт.
  - Переводы строк заменяются пробелами; пустое сообщение заменяется `exception.GetType().Name`.
  - Затем текст проходит через `masker.Mask`.
  - Путь — `LogFiles.GetTextLogPath(settings.ContentRootPath, timeProvider)`.
- **Токен для masker:**
  - builder создан → `HostSecretMasker.Create(builder.Configuration)`;
  - упал сам `createHostBuilder` → `HostSecretMasker.Create` над конфигурацией из переменных окружения с префиксом `GitHubBackupHostBuilder.EnvironmentVariablePrefix` и `settings.ConfigurationArguments`; файлы не перечитываются.
  - В обоих случаях `SecretMasker` ещё маскирует шаблоны `ghp_…`, `github_pat_…` и заголовки `Authorization`.
- **Лог:**
  - этап `StartAsync` — логгер хоста (создан в `Build()`); `using (host)` сбрасывает файлы;
  - этапы builder и `Build()` — `using var factory = StartupLogging.CreateLoggerFactory(...)` и `CliLog.StartupFailed`.
- **Ошибка логирования:** запись в лог обёрнута в `try/catch`; при сбое строка выводится без `(log: …)`, код `2`.
- **Отмена:** фильтр исключает `OperationCanceledException when cancellationToken.IsCancellationRequested`.
- **Шов для тестов:** делегат `createHostBuilder` по образцу `AppStartup`: бросить из делегата; вернуть builder с фабрикой контейнера, которая бросает; вернуть builder с `IHostedService`, который бросает в `StartAsync`.

## 6. Тесты

**`CliApplicationTests`:**

- `RunAsync_UnexpectedExceptionWhileBuildingHost_WritesOneStartupErrorLineAndReturnsCritical` — theory по трём этапам.
- `RunAsync_TokenInExceptionMessage_NeverWritesTokenToErrorOutput` — токен не по шаблону через `--GitHub:Token=`; плюс `ghp_…` на этапе builder.
- `RunAsync_UnexpectedStartupException_WritesMaskedExceptionToBothLogFiles`.
- `RunAsync_UnexpectedStartupException_LogEntryHasRunId`.
- `RunAsync_MultiLineExceptionMessage_WritesOneLine`, `RunAsync_EmptyExceptionMessage_WritesExceptionTypeName`.
- `RunAsync_InvalidHistoryPath_WritesStartupError` — реальный путь без шва.
- `RunAsync_InvalidHistoryPathAndInvalidSetting_WritesOneStartupErrorLine` — смешанный случай.
- `RunAsync_LogFolderUnavailable_StillWritesOneLineAndReturnsCritical`.
- `RunAsync_OperationCanceledDuringStartWithCancelledToken_Propagates`.
- Все существующие тесты `Configuration error:` проходят без изменений.

**`StartupLoggingTests`:** оба дневных файла создаются; секрет замаскирован в обоих; после `Dispose` файл полный.

**`HostSecretMaskerTests`:** токен из конфигурации маскируется; без токена текст не меняется; пустой или пробельный токен игнорируется.

## 7. Проверка

| Критерий | Как проверяется |
|---|---|
| Одна строка, код 2, без стека | `RunAsync_UnexpectedExceptionWhileBuildingHost_WritesOneStartupErrorLineAndReturnsCritical` |
| Токен не попадает в stderr | `RunAsync_TokenInExceptionMessage_NeverWritesTokenToErrorOutput` |
| Полное исключение в логе, токен замаскирован | `RunAsync_UnexpectedStartupException_WritesMaskedExceptionToBothLogFiles`, `StartupLoggingTests` |
| Добавленные пункты | тесты из §6 |
| CI зелёный | сборка, тесты, `dotnet format --verify-no-changes`; CI pull request |

## 8. Вне рамок

- Неожиданные исключения из самой команды и из `host.StopAsync`: задача говорит только о запуске.
- То же для WPF (`AppStartup.StartAsync`).
- Запись распознанных ошибок `Configuration error:` в лог.
- Изменение `ConfigurationErrorMessages`.

## 9. Открытые вопросы

1. Формат строки: `Startup error: <msg> (log: <path>)` в одну строку, хотя `validate` пишет путь отдельной строкой `Log: <path>`.
2. Смешанный `AggregateException`: одна строка `Startup error:` или сначала строки `Configuration error:` для распознанных частей.
3. Текст изменения PRD и версия 0.31.
4. Если упал сам `createHostBuilder`, токен из `appsettings.json` не регистрируется (остаётся маскирование по шаблону).

## 10. Решения

Обзор плана 2026-11-15, все рекомендации приняты:

1. Изменение PRD §7.2 принято в предложенной редакции, версия 0.31.
2. Строка в одну строку: `Startup error: <сообщение> (log: <путь>)`.
3. Смешанный `AggregateException` — одна строка `Startup error:`, детали в логе.
4. Если упало создание builder, токен из `appsettings.json` не регистрируется; остаётся маскирование по шаблону и токен из переменных окружения и аргументов.

## 11. Задачи

- [x] Изменение PRD §7.2 (версия 0.31, строка истории)
- [x] Infrastructure: `WriteToDailyFiles`
- [x] Infrastructure: `HostSecretMasker.Create`, регистрация `ISecretMasker` через него
- [x] Infrastructure: `StartupLogging.CreateLoggerFactory`
- [x] Тесты `StartupLoggingTests`, `HostSecretMaskerTests`
- [x] Cli: `CliLog.StartupFailed`
- [x] Cli: конструкторы `CliApplication`
- [x] Cli: этапы builder, `Build`, `StartAsync`; последний `catch`; `ReportStartupErrorAsync`
- [x] Тесты в `CliApplicationTests`
- [x] README: раздел «Exit codes»
- [x] Сборка, тесты, формат
- [x] Проверка добавленных комментариев
