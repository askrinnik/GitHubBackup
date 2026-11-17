# План: #147 F3.19: Log level attribute for OTLP log records

| Поле | Значение |
|---|---|
| Issue | [#147](https://github.com/askrinnik/GitHubBackup/issues/147) |
| Заголовок | F3.19: Log level attribute for OTLP log records |
| Лейн | Feature |
| Сложность | S |
| Дата | 2026-11-17 |

## 1. Цель

FR-10.5 (PRD 0.45). Каждая запись лога, которую программа отправляет по OTLP, получает атрибут `level`. Его значение — имя уровня `Microsoft.Extensions.Logging.LogLevel` (`Trace` … `Critical`), то же, что в `LogRecord.SeverityText`. Если атрибут `level` уже пришёл со свойствами записи, программа его не перезаписывает и не добавляет второй. Если уровень неизвестен, атрибут не ставится. Файловые логи (FR-10.2) и живой лог UI (FR-12.3) не меняются.

## 2. Критерии приёмки

1. PRD (FR-10.5) описывает атрибут `level` записи лога; версия PRD повышена, строка в истории изменений добавлена.
2. Каждая запись лога, отправленная по OTLP, несёт атрибут `level` с именем уровня.
3. Существующий атрибут `level` записи не перезаписывается и не дублируется.
4. Текстовый и JSON-логи в файлах не меняются.
5. Тесты в `GitHubBackup.Infrastructure.Tests` проверяют атрибут для нескольких уровней и случай с уже существующим `level`.
6. README и урок 6 курса New Relic обновлены.
7. CI зелёный.
8. *(добавлено)* Если у записи нет уровня (`LogLevel.None`), атрибут `level` не добавляется; остальные атрибуты остаются без изменений.
9. *(добавлено)* Атрибут есть на реальном пути хоста `ILogger` → Serilog → провайдер OpenTelemetry: процессор зарегистрирован в конвейере.
10. *(добавлено)* Маскирование секретов и атрибуты `exception.*` работают как раньше.

## 3. Затронутые проекты и типы

- **GitHubBackup.Infrastructure**
  - *новый* `Telemetry/LevelLogRecordProcessor.cs`: `internal sealed class LevelLogRecordProcessor : BaseProcessor<LogRecord>`, константа `internal const string LevelAttribute = "level"`.
  - *изменён* `Telemetry/TelemetryServiceCollectionExtensions.cs`: в `WithLogging` добавляется `.AddProcessor(new LevelLogRecordProcessor())` после `SecretMaskingLogRecordProcessor` и до `AddOtlpExporter()`; XML-doc `AddGitHubBackupTelemetry` упоминает атрибут уровня.
- **GitHubBackup.Infrastructure.Tests**
  - *новый* `Telemetry/LevelLogRecordProcessorTests.cs`.
  - *изменён* `Telemetry/ProcessorTestLog.cs`: новые записи для тестов уровня.
- **GitHubBackup.Infrastructure.IntegrationTests**
  - *изменён* `Logging/OtlpLogExportTests.cs`: утверждение `level` в `Log_TelemetryEnabled_ExportsMessageLevelAndContext`.
- **Документация:** `README.md` (раздел Telemetry, Logs), `docs/new-relic-course/06-where-to-look.md` (раздел Logs).

## 4. Подход

1. **Процессор.** `OnEnd(LogRecord data)`:
   - если `data.LogLevel` равен `LogLevel.None` — выход (неизвестное значение не ставится);
   - если в `data.Attributes` уже есть ключ `"level"` (`StringComparison.Ordinal`) — выход;
   - иначе новый `List<KeyValuePair<string, object?>>` ёмкостью `(Count ?? 0) + 1`: все существующие атрибуты и `new(LevelAttribute, severityText)`; список присваивается `data.Attributes`, как в `SecretMaskingLogRecordProcessor`. Исходный список не меняется.
2. **Источник значения — `LogRecord.LogLevel`.** `OpenTelemetryLogger` ставит в `SeverityText` `nameof(LogLevel.X)` для уровней 0..5 и `null` для `None` (проверено по документации OpenTelemetry .NET через Context7). В версии 1.21 `SeverityText` внутреннее свойство, поэтому процессор берёт `LogLevel.ToString()`: значение то же, `None` означает «нет уровня».
3. **Ключ сравнивается ordinal, с учётом регистра.** В OTLP и New Relic `Level` и `level` — разные атрибуты.
4. **Порядок: маскирование → уровень → экспортёр.** Значение уровня из фиксированного списка, маскировать не нужно; маскирующий процессор пересобирает список атрибутов, поэтому атрибут уровня добавляется после него. Свойство `level` из записи по-прежнему проходит маскирование.
5. **Файловые логи.** Меняется только конвейер OpenTelemetry. Код Serilog, `TextLogFormatter`, `CompactJsonFormatter` и приёмник живого лога не меняются; атрибут добавляется к `LogRecord` провайдера OpenTelemetry, а не к `LogEvent` Serilog.
6. **Документация.**
   - README, Telemetry → Logs: пункт об атрибуте `level` (то же значение, что severity text; свойство `level` самой записи сохраняется).
   - Урок 6, раздел Logs: колонка `level` показывает уровень записи (`Warning`, `Error` и т. д.); у записей, отправленных до обновления программы, уровень есть только в `severity.text`. NRQL с `severity.text IN ('Warning', 'Error', 'Fatal')` остаётся.

## 5. Тесты

`LevelLogRecordProcessorTests` повторяет устройство `SecretMaskingLogRecordProcessorTests` (`LoggerFactory.Create` + `AddOpenTelemetry`, `ParseStateValues = true`, `IncludeFormattedMessage = true`, минимальный уровень `Trace`). Конвейер как в продукте: `SecretMaskingLogRecordProcessor` → `LevelLogRecordProcessor` → `SimpleLogRecordExportProcessor(CapturingLogExporter)`.

- `OnEnd_LogLevel_AddsLevelAttributeWithLevelName` — `[Theory]` по шести уровням; ровно один `level`, равный `level.ToString()`; `Repository` сохранён.
- `OnEnd_LevelPropertyPresent_KeepsItAndAddsNoSecond` — свойство `{level}` = `"custom"`; ровно один `level` = `"custom"`.
- `OnEnd_LevelPropertyDiffersInCase_AddsLevelAttribute` — свойство `{Level}` = `"custom"`; `Level == "custom"`, `level == "Warning"`.
- `OnEnd_NoLevel_AddsNoLevelAttribute` — тестовый процессор перед `LevelLogRecordProcessor` ставит `LogLevel = LogLevel.None`; атрибута `level` нет, остальные на месте.
- `OnEnd_ExceptionEntry_KeepsMaskedExceptionAttributesAndAddsLevel` — запись с исключением (`Error`): `exception.type` на месте, `level == "Error"`.

`OtlpLogExportTests.Log_TelemetryEnabled_ExportsMessageLevelAndContext` получает утверждение `level == "Warning"`: оно проверяет регистрацию в `AddGitHubBackupTelemetry` и путь через Serilog.

## 6. Проверка

| # | Как проверяется |
|---|---|
| 1 | PRD 0.45, FR-10.5 и строка истории |
| 2 | `OnEnd_LogLevel_AddsLevelAttributeWithLevelName`, `OtlpLogExportTests` |
| 3 | `OnEnd_LevelPropertyPresent_KeepsItAndAddsNoSecond`, `OnEnd_LevelPropertyDiffersInCase_AddsLevelAttribute` |
| 4 | Код Serilog и форматтеров не меняется; существующие тесты логов зелёные |
| 5 | `LevelLogRecordProcessorTests` |
| 6 | Ревью диффа документации |
| 7 | Сборка, тесты, `dotnet format`, CI на PR |
| 8 | `OnEnd_NoLevel_AddsNoLevelAttribute` |
| 9 | `OtlpLogExportTests.Log_TelemetryEnabled_ExportsMessageLevelAndContext` |
| 10 | `SecretMaskingLogRecordProcessorTests`, `OtlpLogExportTests`, `OnEnd_ExceptionEntry_…` |

## 7. Вне объёма

- Атрибут уровня для трейсов и метрик.
- Уровни Serilog `Verbose`/`Fatal`: значение берётся из `LogLevel`, поэтому в колонке `level` будет `Critical`, а не `Fatal`. NRQL урока с `'Fatal'` остаётся по решению пользователя.
- Уже отправленные записи в New Relic.

## 8. Решения

- Изменение PRD (FR-10.5, версия 0.45) одобрено пользователем до планирования.
- Обработчик OpenTelemetry, а не enricher Serilog: enricher изменил бы файловые логи.
- Значение — имя `LogLevel`; берётся из `LogRecord.LogLevel`, потому что `SeverityText` в OpenTelemetry 1.21 внутреннее (отклонение от первой версии плана, найдено при реализации; значение совпадает с `SeverityText`).
- Порядок процессоров: маскирование → уровень → экспортёр.
- Ключ `level` сравнивается ordinal, с учётом регистра; свойство `Level` не мешает добавить `level`.
- Пользователь разрешил перейти к реализации сразу после планирования, если нет вопросов.

## 9. Задачи

- [x] Добавить `LevelLogRecordProcessor`.
- [x] Зарегистрировать процессор в `AddGitHubBackupTelemetry`; обновить XML-doc.
- [x] Расширить `ProcessorTestLog`.
- [x] Добавить `LevelLogRecordProcessorTests`.
- [x] Добавить утверждение `level` в `OtlpLogExportTests`.
- [x] Обновить `README.md` (Telemetry → Logs).
- [x] Переписать раздел Logs урока 6.
- [x] Сборка, тесты, `dotnet format`, проверка комментариев.
