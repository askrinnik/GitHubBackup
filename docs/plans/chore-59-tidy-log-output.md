# План: Tidy log output: empty properties and host lifetime messages

| Поле | Значение |
|---|---|
| Issue | [#59](https://github.com/askrinnik/GitHubBackup/issues/59) |
| Lane | Feature (`type:chore`) |
| Сложность | S |
| Дата | 2026-11-15 |

## Цель

Текстовый лог не печатает хвост `{}` у записей без дополнительных свойств, а записи со свойствами (`Repository`, `Operation`, `Tool`, `Stream`…) сохраняют их. Служебные сообщения `Microsoft.Hosting.Lifetime` («Application started. Press Ctrl+C…», «Hosting environment», «Content root path») не попадают в лог по умолчанию.

Требования: FR-10.2, FR-10.3 ([§6.10](../PRD.md#610-логирование)), [§7.2](../PRD.md#72-appsettingsjson-через-iconfiguration-ioptionst-с-валидацией-при-старте).

## Критерии приёмки

- [x] Запись без свойств в текстовом логе не содержит `{}`
- [x] Запись со свойствами сохраняет их в текстовом логе
- [x] Решение по сообщениям `Microsoft.Hosting.Lifetime` принято и описано (`appsettings.json` или PRD §7.2)
- [x] Покрыто тестами, CI зелёный
- [x] (добавлено) Свойство, которое уже есть в тексте сообщения или в макете (`RunId`, `SourceContext`), не считается дополнительным: такая запись тоже без `{}`
- [x] (добавлено) Маскирование секретов в текстовом логе работает и для записей без хвоста свойств

## Решение по `Microsoft.Hosting.Lifetime`

Глушить. Сообщения пишет `ConsoleLifetime` хоста; в CLI фраза «Press Ctrl+C to shut down» появляется в логе каждого запуска, в том числе в тихом режиме из планировщика, а в UI — и в ленте лога, где Ctrl+C ничего не делает. Полезной информации для бэкапа в них нет.

Способ — `Serilog:MinimumLevel:Override:Microsoft.Hosting.Lifetime = Warning` в поставляемом `appsettings.json` (общий для CLI и UI). Так решение видно пользователю и отменяется правкой файла, без изменения кода. Альтернатива `ConsoleLifetimeOptions.SuppressStatusMessages` спрятала бы решение в коде; отклонена.

PRD §7.2: пример `appsettings.json` получает этот `Override` и одно предложение с причиной; версия 0.32. README повторяет пример и таблицу ключей.

## Подход

1. `Infrastructure/Logging/TextLogFormatter` (`ITextFormatter`, internal sealed): держит два `MessageTemplateTextFormatter` — с хвостом ` {Properties:j}` и без него. Выбирает форматтер по тому, есть ли у события свойство, которого нет ни в шаблоне сообщения, ни в макете (имена из разбора макета `MessageTemplateParser`). Это то же правило, по которому Serilog наполняет `{Properties}`, поэтому вывод со свойствами не меняется.
2. `FileLoggingServiceCollectionExtensions`: константа `TextOutputTemplate` уходит, макет живёт в `TextLogFormatter.Layout`; `WriteToDailyFiles` использует `TextLogFormatter` внутри `MaskingTextFormatter`. Ранний лог старта (`StartupLogging`) идёт через тот же `WriteToDailyFiles` и получает правку автоматически.
3. `src/GitHubBackup.Cli/appsettings.json`: `"Override": { "Microsoft.Hosting.Lifetime": "Warning" }`.
4. PRD §7.2 (версия, история), README (пример и таблица ключей).

Новых пакетов нет (Serilog.Expressions не нужен).

## Тесты

- `Infrastructure.Tests/Logging/TextLogFormatterTests` (новый):
  - без свойств — строка заканчивается сообщением, без `{}`;
  - свойство только в шаблоне сообщения — без `{}`;
  - `RunId` и `SourceContext` (в макете) — без `{}`;
  - дополнительное свойство — хвост ` {"Repository":"octo/app"}`.
- `MaskingTextFormatterTests`: существующие тесты с `TextOutputTemplate` переходят на `TextLogFormatter`, где нужен хвост свойств.
- `Infrastructure.IntegrationTests/Logging/SerilogFileLoggingTests`:
  - запись без свойств в реальном файле — строка без `{}`;
  - хост с `Override` для `Microsoft.Hosting.Lifetime` после `StartAsync`/`StopAsync` не пишет записей этого источника, а без `Override` пишет (подтверждает, что настройка действует).
- `Cli.Tests`: поставляемый `appsettings.json` (из папки вывода) задаёт `Override` `Microsoft.Hosting.Lifetime` = `Warning`; `ValidAppSettings` повторяет поставляемый файл.

## Вне области

- Свойство `EventId`, которое Serilog добавляет к записям с ненулевым идентификатором события (все записи через `[LoggerMessage]`): это настоящее дополнительное свойство, поэтому такие записи сохраняют хвост `{"EventId":{…}}`. Хвост `{}` исчезает у записей без идентификатора события: ранний лог старта, сообщения библиотек без `EventId`.

- Уровни других источников `Microsoft.*` (EF Core, `System.Net.Http`): на уровне `Information` в логе они сейчас не шумят.
- Формат JSON-лога (CLEF): там пустых свойств нет.

## Решения

- Пользователь разрешил выполнить задачу целиком без остановки на ревью плана, если нет вопросов; ревью — в pull request. Решение по `Microsoft.Hosting.Lifetime` и правка PRD §7.2 входят в формулировку issue.

## Чек-лист

- [x] `TextLogFormatter` и его тесты
- [x] Макет текстового лога без `{Properties:j}`, `WriteToDailyFiles`
- [x] `appsettings.json`: `Override`
- [x] PRD §7.2 0.32, README
- [x] Интеграционные тесты и тест поставляемого `appsettings.json`
- [x] Сборка, тесты, формат
