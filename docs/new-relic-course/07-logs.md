# Урок 7. Логи: поиск и анализ

**Коротко.** Логи можно искать двумя способами: строкой поиска в разделе **Logs** и запросом NRQL `FROM Log`. Строка поиска удобна, чтобы найти запись. NRQL удобен, чтобы посчитать, сгруппировать и построить график. Как выполнить NRQL-запрос, описано в уроке 6.

```
Logs (раздел)
  ├─ строка поиска      — найти записи по слову, фразе, атрибуту
  ├─ панель атрибутов   — фильтр щелчком по значению
  ├─ Patterns           — похожие строки, собранные в шаблоны
  └─ Live tail          — записи в реальном времени
FROM Log (NRQL)         — счёт, группировка, графики, разбор текста
```

## Где открыть логи

- В сервисе `GitHubBackup` откройте **Logs**. Там только логи этого сервиса.
- Раздел **Logs** в левом меню показывает логи всего аккаунта.

Колонка `level` в списке показывает уровень записи. Программа кладёт уровень в атрибут `level`; OpenTelemetry передаёт его ещё и в `severity.text`. У записей, отправленных до появления атрибута `level`, уровень есть только в `severity.text`, и колонка `level` у них пустая. Чтобы видеть уровень и у таких записей, нажмите `+` над таблицей и добавьте колонку `severity.text`.

## Какие атрибуты есть у логов GitHubBackup

| Атрибут | Что содержит |
|---|---|
| `message` | Текст записи |
| `level` | Уровень: `Information`, `Warning`, `Error` |
| `severity.text` | Тот же уровень по стандарту OpenTelemetry |
| `Repository` | Репозиторий `owner/name`, если запись о нём |
| `Operation`, `RunId` | Операция и запуск, если запись в их контексте |
| `trace.id`, `span.id` | Связь записи с трейсом |

Полный список покажет запрос:

```sql
FROM Log SELECT keyset() WHERE service.name = 'GitHubBackup' SINCE 1 day ago
```

## Строка поиска

**Правила.** Строка поиска использует синтаксис Lucene.

| Что нужно | Как писать | Пример |
|---|---|---|
| Слово или фраза в тексте | в кавычках | `"was cancelled"` |
| Значение атрибута | `атрибут:значение` | `level:Warning` |
| Значение с пробелом или `/` | значение в кавычках | `Repository:"ViraSkrynnik/api-node-js"` |
| Часть значения | `*` в начале, в середине или в конце | `Repository:*test*` |
| Оба условия | `AND` (или пробел) | `level:Warning AND "git"` |
| Любое из условий | `OR` | `level:Warning OR level:Error` |
| Исключить | `-` перед условием | `level:Warning -Repository:*test*` |
| Атрибут есть / нет | `has:` / `missing:` | `has:Repository` |
| Числовое сравнение | `:>`, `:>=`, `:<`, `:<=` | `http_response_time_ms:>500` (пример из документации; у логов GitHubBackup числовых атрибутов почти нет) |

**Регистр букв.** Имена атрибутов чувствительны к регистру: `level` и `Level` — разные атрибуты. Значения тоже чувствительны к регистру: `level:warning` не найдёт `Warning`. Исключение — поиск с `*`: он регистр значения не учитывает.

**Примеры для GitHubBackup:**

| Задача | Строка поиска |
|---|---|
| Все отмены запусков | `"backup run was cancelled"` |
| Всё по одному репозиторию | `Repository:"ViraSkrynnik/api-node-js"` |
| Предупреждения, кроме тестовых репозиториев | `level:Warning -Repository:*test*` |
| Ненулевой код выхода git | `"git exited with code" -"exited with code 0"` |
| Записи одного запуска | `RunId:"<id>"` |

**Переход к NRQL.** Кнопка **NRQL** рядом со строкой поиска переключает раздел на запрос NRQL. Строка поиска и NRQL — разные языки: запрос не переводится один в один.

## Панель атрибутов

Нажмите на запись лога. Справа откроются её атрибуты. Нажмите на значение атрибута, например `Repository` или `level`. New Relic добавит фильтр по этому значению в строку поиска, например `"level":"Warning"`. Так удобно строить фильтр, не вспоминая синтаксис.

## Логи в контексте трейса

Записи GitHubBackup связаны с трейсами через `trace.id` и `span.id`. Связь работает в обе стороны:

- **От трейса к логам.** Откройте трейс `backup.run` (урок 6). Вкладка **Logs** рядом с **Trace details** показывает все записи этого запуска.
- **От лога к трейсу.** Откройте запись лога. В её подробностях есть раздел **Distributed trace** со ссылкой на трейс.

Так проще всего разобрать один сбойный запуск: найдите трейс со статусом `Failed` и откройте его логи.

## Patterns

**Patterns** собирает похожие строки в шаблоны. Переменные части (имена, числа) New Relic заменяет на маску. Так видно, какие сообщения пишутся чаще всего, и какой шаблон появился впервые.

- Откройте **Patterns** в разделе Logs.
- Если видно сообщение «Patterns are turned off», нажмите **Configure patterns**. Первые шаблоны могут появиться только через сутки.
- Шаблон записывается в атрибут `newrelic.logPattern`. По нему можно группировать в NRQL.

## Live tail

**Live tail** показывает новые записи по мере поступления. Удобно смотреть запуск, пока он идёт.

1. Введите фильтр, например `level:Warning OR level:Error`.
2. Нажмите **Query logs**.
3. Справа включите **Live tail**.
4. Запустите GitHubBackup.

## NRQL по логам

**Поиск текста.** `LIKE` ищет по шаблону: `%` — любые символы. `LIKE` не учитывает регистр.

```sql
FROM Log SELECT timestamp, level, Repository, message
WHERE service.name = 'GitHubBackup' AND message LIKE '%was cancelled%'
SINCE 7 days ago
```

**Регулярное выражение.** `RLIKE` проверяет строку целиком по синтаксису RE2. Пример: git завершился с ненулевым кодом.

```sql
FROM Log SELECT timestamp, Repository, message
WHERE service.name = 'GitHubBackup' AND message RLIKE r'git exited with code [1-9].*'
SINCE 7 days ago
```

**Предупреждения и ошибки:**

```sql
FROM Log SELECT timestamp, level, message, Repository, Operation
WHERE service.name = 'GitHubBackup' AND level IN ('Warning', 'Error', 'Critical')
SINCE 1 day ago LIMIT 100
```

Если запрос пуст, посмотрите реальные значения уровня: `FROM Log SELECT uniques(level) WHERE service.name = 'GitHubBackup' SINCE 1 day ago`.

**Уровни по дням:**

```sql
FROM Log SELECT count(*) WHERE service.name = 'GitHubBackup'
FACET level SINCE 7 days ago TIMESERIES 1 day
```

**Разбор текста: `aparse()`.** Достаёт часть строки по шаблону. `*` — часть, которую нужно достать. `%` — часть, которую нужно пропустить. Пример: коды выхода git.

```sql
FROM Log SELECT count(*) WHERE service.name = 'GitHubBackup' AND message LIKE 'git exited with code%'
FACET aparse(message, 'git exited with code * after %') SINCE 7 days ago
```

**Разбор текста: `capture()`.** То же по регулярному выражению с именованной группой `(?P<имя>...)`. Выражение должно совпасть со всей строкой.

```sql
FROM Log SELECT count(*) WHERE service.name = 'GitHubBackup' AND message LIKE 'git exited with code%'
FACET capture(message, r'git exited with code (?P<code>\d+) after .*') SINCE 7 days ago
```

**Какие записи занимают больше места.** Логи — основная часть расхода (урок 10). `bytecountestimate()` оценивает объём записей в байтах. Запрос сравнивает строки вывода git с остальными:

```sql
FROM Log SELECT bytecountestimate() / 1e6 AS 'MB'
WHERE service.name = 'GitHubBackup'
FACET if(message LIKE 'git stdout:%', 'git stdout', 'other') SINCE 7 days ago
```

## Как сократить объём логов

Если логов станет слишком много, лишние записи можно отбрасывать на стороне New Relic до сохранения. Отброшенные записи не восстановить, и правило не действует на уже принятые данные.

Старые правила **Drop filters** на основе NRQL New Relic вывел из работы 31 августа 2026 года. Им на смену пришёл **Pipeline Control**. Перед настройкой сверьтесь с документацией.

Для GitHubBackup проще уменьшить объём в самой программе: в настройках логирования поднять минимальный уровень для записей, которые не нужны в New Relic. Запрос `bytecountestimate()` выше покажет, какие записи стоит убрать первыми.

## Источники

- Синтаксис строки поиска: https://docs.newrelic.com/docs/logs/ui-data/query-syntax-logs/
- Patterns: https://docs.newrelic.com/docs/logs/ui-data/find-unusual-logs-log-patterns/
- Live tail: https://docs.newrelic.com/docs/logs/troubleshooting/view-log-messages-real-time-live-tail/
- Логи в контексте: https://docs.newrelic.com/docs/logs/logs-context/get-started-logs-context/
- Отбрасывание данных: https://docs.newrelic.com/docs/logs/ui-data/drop-data-drop-filter-rules/
- Функции NRQL (`aparse`, `capture`, `LIKE`, `RLIKE`): https://docs.newrelic.com/docs/nrql/nrql-syntax-clauses-functions/
