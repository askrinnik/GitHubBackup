# План: #135 F3.16 — читаемая живая лента лога, окружение процесса в логе и логи по OTLP

| | |
|---|---|
| Issue | [#135](https://github.com/askrinnik/GitHubBackup/issues/135) |
| Заголовок | F3.16: Readable live log, process environment in the log and logs over OTLP |
| Сценарий | Feature |
| Сложность | L |
| Дата | 2026-11-15 |

## 1. Цель

Задача закрывает три пробела в логировании (FR-10.3, FR-10.4, FR-10.5, FR-10.6, FR-12.3, NFR-1):
- **Лента лога (FR-12.3).** Сейчас `RepositoriesView.ScrollToNewest` вызывает `ListBox.ScrollIntoView`. Этот вызов прокручивает ленту и по горизонтали, поэтому у длинной строки виден только конец. Нужно прокручивать только по вертикали.
- **Окружение процесса (FR-10.4).** Запись о запуске процесса показывает только имена переменных. Нужен вид `имя=значение`: секретные значения заменены на `***`, вся строка проходит через `ISecretMasker`.
- **Логи по OTLP (FR-10.5).** При включённой телеметрии те же записи, что идут в файлы, уходят по OTLP с контекстом FR-10.3. Секреты в них замаскированы (FR-10.6), минимальные уровни Serilog действуют и на них.

## 2. Критерии приёмки

Из issue, дословно:
- [ ] PRD уточнён (FR-10.4, FR-10.5, FR-12.3), версия поднята, строка в истории изменений добавлена
- [ ] Новая запись в ленте лога прокручивает её вниз и не меняет горизонтальное положение; длинная строка видна с начала
- [ ] Запись о запуске git содержит переменные в виде `имя=значение`, например `GIT_CONFIG_KEY_0=core.longpaths`
- [ ] Значение заголовка авторизации в записи о запуске git заменено на `***`; тест проверяет, что токен и его Base64 в записи отсутствуют
- [ ] При включённой телеметрии записи лога уходят по OTLP и видны в Aspire Dashboard в разделе Structured logs, с текстом сообщения, уровнем и свойствами `RunId`, `Repository`, `Operation`
- [ ] Тест: запись с токеном в сообщении, свойстве или тексте исключения уходит в OTLP-экспорт с `***` вместо токена
- [ ] При выключенной телеметрии провайдер логов OpenTelemetry не регистрируется; файлы логов и живая лента лога не изменились
- [ ] Минимальные уровни Serilog (`Serilog:MinimumLevel`) действуют и на записи, которые уходят по OTLP
- [ ] README обновлён
- [ ] Покрыто тестами, CI зелёный

Добавлено проверкой покрытия:
- [ ] (добавлено) Переменная с `IsSecret = true` выводится как `ИМЯ=***`, даже если её значение пустое. Несекретная переменная с пустым значением выводится как `ИМЯ=` (пример: `credential.helper`). Пустой список переменных даёт `[]`.
- [ ] (добавлено) Если токен попал в значение без пометки секрета, его всё равно маскирует `ISecretMasker` (маскирование применяется ко всей строке).
- [ ] (добавлено) Значения с пробелами, путями и запятыми выводятся как есть, без экранирования.
- [ ] (добавлено) В OTLP маскируются и `FormattedMessage`, и `Body`, и строковые атрибуты. Текст исключения (`exception.message`, `exception.stacktrace`) тоже уходит замаскированным.
- [ ] (добавлено) Существующий тест `ProcessRunnerLoggingTests.RunAsync_SecretPrintedByProcess_IsMaskedInBothLogFiles` меняется: несекретное значение теперь попадает в лог, тест ждёт `_plainName=_plainValue` и `_secretName=***`.

## 3. Изменения требований

Изменения PRD утверждены и входят в эту задачу:
- версия 0.35 → 0.36;
- новая строка истории от 2026-11-15;
- три текста дословно из issue: первый пункт FR-10.4, новый пункт FR-10.5 «логи: …», дополнение FR-12.3.

§8.3 не меняется: новых пакетов нет.

## 4. Затронутые проекты и типы

**docs**
- `docs/PRD.md`: FR-10.4, FR-10.5, FR-12.3, шапка, история.
- `README.md`: раздел Telemetry. Новый абзац «Logs» о том, что уходит и как маскируется. В «View telemetry in the .NET Aspire Dashboard» новый шаг про страницу **Structured logs**: фильтр по `RunId`, `Repository`, `Operation`.

**GitHubBackup.Infrastructure**
- `Processes/ProcessEnvironmentFormat.cs` (новый, `internal static`, по образцу `ProcessCommandLine`). Метод `Format(IReadOnlyList<ProcessEnvironmentVariable>)` возвращает строку `NAME=value, NAME2=***`.
- `Processes/ProcessRunner.cs`: в `RunAsync` вместо списка имён вызывается `secretMasker.Mask(ProcessEnvironmentFormat.Format(...))`.
- `Processes/ProcessRunnerLog.cs`: имя свойства `EnvironmentVariables` остаётся; текст шаблона — по решению ревью.
- `Logging/FileLoggingServiceCollectionExtensions.cs`: `services.AddSerilog(ConfigureLogger, preserveStaticLogger: true, writeToProviders: true)`; обновить `<remarks>`: события после фильтра уровней Serilog получают и зарегистрированные `ILoggerProvider`.
- `Telemetry/SecretMaskingLogRecordProcessor.cs` (новый, `internal sealed`, `BaseProcessor<LogRecord>`). В `OnEnd` маскирует через `ISecretMasker`:
  - `FormattedMessage` и `Body`;
  - строковые значения `Attributes`; нестроковые непримитивные значения заменяет замаскированным `ToString()`;
  - `Exception`: снимает его и добавляет атрибуты `exception.type`, `exception.message` и `exception.stacktrace` (замаскированный `exception.ToString()`).
- `Telemetry/TelemetryServiceCollectionExtensions.cs`: `.WithLogging(logging => logging.AddProcessor(<маскирующий процессор из DI>).AddOtlpExporter(), options => { IncludeFormattedMessage = true; IncludeScopes = true; ParseStateValues = true; })`; обновить summary.

**GitHubBackup.App**
- `Views/RepositoriesView.xaml.cs`: `ScrollToNewest` находит `ScrollViewer` шаблона `RunLogList` (обход `VisualTreeHelper` один раз, кеш в поле) и вызывает `ScrollToBottom()`. `HorizontalOffset` не меняется.

## 5. Подход

Порядок работы: PRD, затем Infrastructure (процессы, логи, телеметрия), затем App, затем README.

1. **PRD.** Три утверждённых текста, версия 0.36, строка истории.
2. **Окружение в логе.**
   - Формат: `имя=значение`, разделитель `, `, без кавычек и экранирования.
   - Секретное значение всегда `***`, даже пустое.
   - Готовая строка проходит через `secretMasker.Mask`: это ловит токен в несекретном значении.
   - Секреты регистрируются в маскере до записи в лог, как и раньше.
3. **Записи Serilog для провайдеров.**
   - `writeToProviders: true` подключает `LoggerProviderCollectionSink` за фильтром `MinimumLevel`, поэтому уровни Serilog действуют и на OTLP.
   - Свойства события (`RunId` от `RunIdEnricher`, `Repository` и `Operation` из scope, `SourceContext`) приходят провайдеру в состоянии записи и уходят в OTLP атрибутами.
   - Хост создаётся с `DisableDefaults = true`, других `ILoggerProvider` нет: при выключенной телеметрии коллекция провайдеров пуста.
   - Проверить при реализации: `Serilog.Extensions.Hosting` с `writeToProviders: true` добавляет `ILoggerProvider` из DI в коллекцию. Подтверждает интеграционный тест; иначе провайдеры передаются явно через `LoggerProviderCollection`.
4. **OTLP-логи.**
   - Маскирующий процессор регистрируется перед экспортёром: процессоры работают в порядке регистрации, batch-экспортёр хранит ссылку на уже изменённую `LogRecord`.
   - `ISecretMasker` — singleton из DI, общий с файлами: секреты, которые `ProcessRunner` регистрирует на лету, маскируются и в OTLP.
   - Эндпоинт — из общих `OtlpExporterOptions`.
   - Порядок регистрации в `GitHubBackupHostBuilder.Create` (Logging, затем Telemetry) сохраняется; фабрика Serilog остаётся главной. Оба хоста собираются через `GitHubBackupHostBuilder`, правка одна.
5. **Пакеты.** Новых нет: `WithLogging` есть в `OpenTelemetry.Extensions.Hosting`, `AddOtlpExporter` для логов — в `OpenTelemetry.Exporter.OpenTelemetryProtocol`. В тестах — свой `BaseExporter<LogRecord>`, подключённый через `SimpleLogRecordExportProcessor`.
6. **Лента лога.** `ScrollToBottom` прокручивает только по вертикали; батчинг через `_scrollToNewestQueued` остаётся.

## 6. Тесты

**GitHubBackup.Infrastructure.Tests**
- `Processes/ProcessEnvironmentFormatTests` (новый): `Format_PlainVariables_JoinsNameEqualsValue`, `Format_SecretVariable_ShowsStars`, `Format_EmptySecretValue_ShowsStars`, `Format_EmptyPlainValue_ShowsNameAndEquals`, `Format_NoVariables_ReturnsEmpty`, `[Theory] Format_ValueWithSpacesPathOrComma_KeepsValue`.
- `Telemetry/SecretMaskingLogRecordProcessorTests` (новый; `LoggerFactory` с `AddOpenTelemetry`, процессором и тестовым экспортёром): `OnEnd_TokenInMessage_ExportsStars`, `OnEnd_TokenInAttribute_ExportsStars`, `OnEnd_TokenInExceptionMessage_ExportsStarsInExceptionAttributes`, `OnEnd_NoSecret_KeepsMessageAndAttributes`.
- `Telemetry/TelemetryServiceCollectionExtensionsTests`: в тесте включённой телеметрии проверить `LoggerProvider`; `_NotEnabled_RegistersNothing` уже покрывает выключенную.

**GitHubBackup.Infrastructure.IntegrationTests**
- `Processes/ProcessRunnerLoggingTests`:
  - поправить `RunAsync_SecretPrintedByProcess_IsMaskedInBothLogFiles`;
  - `RunAsync_GitEnvironmentWithToken_LogsNameValuePairsWithoutToken`: `GitEnvironment.Create` с токеном, `FakeLogger`; запись `Starting` содержит `GIT_CONFIG_KEY_0=core.longpaths` и `GIT_CONFIG_VALUE_<n>=***`; нет токена и `GitEnvironment.EncodeCredentials(token)`;
  - `RunAsync_TokenInPlainVariable_IsMasked`.
- `Hosting/OpenTelemetryHostTests`: проверка `LoggerProvider` в существующих тестах.
- `Logging/OtlpLogExportTests` (новый; хост из `GitHubBackupHostBuilder.Create` во временном content root, тестовый экспортёр после маскирующего процессора):
  - `Log_TelemetryEnabled_ExportsMessageLevelAndContext`;
  - `Log_MinimumLevelWarning_ExportsOnlyWarningAndAbove`;
  - `Log_MinimumLevelOverride_AppliesToExport`;
  - `Log_TokenInMessagePropertyAndException_ExportsStars`;
  - `Log_TelemetryEnabled_FilesStillWritten`.
- Существующие `SerilogFileLoggingTests` и `LiveLogSinkTests` — регрессия файлов и ленты.

**GitHubBackup.App.UITests**
- `RunRowsTests.BackupAll_AfterRun_LogScrolledToBottomAndLeft`: после запуска `ScrollPattern` элемента `RunLog` даёт `HorizontalScrollPercent` 0 (или горизонтальная прокрутка недоступна) и `VerticalScrollPercent` 100.

## 7. Проверка

| Пункт | Как проверяется |
|---|---|
| PRD | Ревью diff `docs/PRD.md` |
| Лента | `RunRowsTests.BackupAll_AfterRun_LogScrolledToBottomAndLeft`; netwright во временном content root |
| `имя=значение` | `ProcessEnvironmentFormatTests`, `ProcessRunnerLoggingTests.RunAsync_GitEnvironmentWithToken_LogsNameValuePairsWithoutToken` |
| Заголовок авторизации `***` | тот же тест, `RunAsync_TokenInPlainVariable_IsMasked` |
| OTLP: сообщение, уровень, контекст | `OtlpLogExportTests.Log_TelemetryEnabled_ExportsMessageLevelAndContext`; вручную — Aspire Dashboard |
| Маскирование в OTLP | `SecretMaskingLogRecordProcessorTests`, `OtlpLogExportTests.Log_TokenInMessagePropertyAndException_ExportsStars` |
| Телеметрия выключена | `TelemetryServiceCollectionExtensionsTests`, `OpenTelemetryHostTests`, тесты файлов и ленты |
| Уровни Serilog | `OtlpLogExportTests.Log_MinimumLevel*` |
| README | Ревью раздела Telemetry |
| CI | сборка, тесты без smoke, `dotnet format --verify-no-changes` |

## 8. Вне рамок

- OTLP по HTTP/protobuf и отдельный адрес для логов.
- Отдельная настройка уровней для OTLP.
- Корреляция записей лога с трассами (`TraceId`/`SpanId`).
- Экранирование значений окружения.

## 9. Открытые вопросы

1. Разделитель пар — `, `, как сейчас для имён?
2. Секретная переменная с пустым значением выводится как `ИМЯ=***`?
3. Текст шаблона: `with environment variables [...]` или `with environment [...]`?
4. Исключение в OTLP — замаскированные атрибуты `exception.type`/`exception.message`/`exception.stacktrace`, исходный `LogRecord.Exception` снимается?
5. Свой тестовый экспортёр вместо пакета `OpenTelemetry.Exporter.InMemory`?

## 10. Решения

План утверждён 2026-11-15 с рекомендованными ответами на открытые вопросы:
1. Пары разделяются `, `, без экранирования.
2. Секретная переменная выводится как `ИМЯ=***`, даже с пустым значением.
3. Текст шаблона `with environment variables [...]` и имя свойства `EnvironmentVariables` не меняются.
4. В OTLP исключение уходит замаскированными атрибутами `exception.type`, `exception.message`, `exception.stacktrace`; исходный `LogRecord.Exception` снимается.
5. В тестах — свой экспортёр `BaseExporter<LogRecord>`, пакет `OpenTelemetry.Exporter.InMemory` не добавляется.

Пользователь разрешил пройти workflow до конца (коммит, push, комментарий, PR) без промежуточного подтверждения; ревью — в pull request.

## 11. Задачи

- [x] PRD: FR-10.4, FR-10.5, FR-12.3; версия 0.36; строка истории 2026-11-15
- [x] `ProcessEnvironmentFormat` + `ProcessEnvironmentFormatTests`
- [x] `ProcessRunner.RunAsync`: формат `имя=значение` через `ISecretMasker`; шаблон `ProcessRunnerLog.Starting` по решению
- [x] `ProcessRunnerLoggingTests`: поправить тест файлов; тесты с `GitEnvironment` и токеном в несекретной переменной
- [x] `FileLoggingServiceCollectionExtensions`: `writeToProviders: true`, обновить `<remarks>`
- [x] `SecretMaskingLogRecordProcessor` + `SecretMaskingLogRecordProcessorTests`
- [x] `TelemetryServiceCollectionExtensions`: `WithLogging` (процессор, затем OTLP-экспортёр, опции); doc и тесты
- [x] `OpenTelemetryHostTests`: проверка `LoggerProvider`
- [x] `OtlpLogExportTests`: контекст, уровни, переопределение, маскирование, файлы
- [x] `RepositoriesView.ScrollToNewest`: `ScrollViewer.ScrollToBottom` вместо `ScrollIntoView`
- [x] UI-тест `RunRowsTests.BackupAll_AfterRun_LogScrolledToBottomAndLeft`
- [ ] Проверка ленты лога через netwright (не сделана: в сессии разработчика не было инструментов netwright)
- [x] README: абзац Logs, шаг Structured logs в разделе Aspire Dashboard
- [x] Сборка, тесты без smoke, `dotnet format --verify-no-changes`; проверка добавленных комментариев
