# Урок 6. Где смотреть данные

**Коротко.** Начните с **APM & Services** → сервис `GitHubBackup`. Там есть трейсы, логи и ошибки. Метрики `githubbackup.*` удобнее смотреть в **Metrics explorer** или через **Query your data** (NRQL).

Названия разделов в интерфейсе New Relic иногда меняются. Если пункта нет на месте, найдите его через поиск в левом меню.

## Карта интерфейса

```
Левое меню
├── APM & Services ──> GitHubBackup ──┬── Summary
│                                     ├── Distributed tracing
│                                     ├── Logs
│                                     └── Errors inbox
├── Query your data ──> NRQL-редактор, Metrics explorer
├── Logs            ──> все логи аккаунта
├── Dashboards
└── Alerts
```

## APM & Services

1. Откройте **APM & Services** в левом меню.
2. Найдите `GitHubBackup` в списке. Сервис OpenTelemetry отмечен значком OpenTelemetry.
3. Откройте сервис. Страница **Summary** показывает обзор: длительность, число операций, ошибки.

Сервис появляется после первых данных. Если его нет, перейдите к уроку 8.

## Distributed tracing

1. В сервисе `GitHubBackup` откройте **Distributed tracing**.
2. Выберите трейс `backup.run`.
3. Под графиком длительностей — дерево спанов, как в уроке 5. Красная метка — спан с ошибкой.
4. Нажмите на спан. Справа откроется панель спана, по умолчанию на вкладке **Performance**.
5. Откройте вкладку **Attributes**. Там атрибуты спана: `githubbackup.status`, `githubbackup.change.decision`, `githubbackup.operation`, `error.type` и другие. Строка **Search attributes and keys** фильтрует список.

## Logs

- В сервисе `GitHubBackup` откройте **Logs**. Там только логи этого сервиса.
- Раздел **Logs** в левом меню показывает логи всего аккаунта.
- В строке поиска можно фильтровать по атрибутам, например `RunId:"<id>"`.
- Колонка `level` в списке логов пустая: OpenTelemetry передаёт уровень записи в атрибуте `severity.text`, а не в `level`. Предупреждения программы имеют значение `Warning`, ошибки — `Error`.
- Как показать только предупреждения: нажмите на запись лога, найдите в её атрибутах `severity.text` со значением `Warning` и добавьте фильтр по этому значению. В строке поиска появится `"severity.text":"Warning"`. Этот фильтр можно и ввести вручную.
- Чтобы видеть уровень в списке, нажмите `+` над таблицей и добавьте колонку `severity.text`.
- Из лога можно перейти к трейсу, если запись связана с трейсом.

## Metrics explorer

1. В сервисе `GitHubBackup` откройте **Metrics explorer** (группа **DATA** меню сервиса) или найдите «Metrics explorer» в поиске.
2. Найдите `githubbackup.repositories`.
3. Выберите агрегацию (`sum`) и разбивку (facet) по `githubbackup.status`.

## Query your data: примеры NRQL

Формат запроса: `FROM <тип> SELECT <что> WHERE <фильтр> FACET <группировка> SINCE <период>`.

Как выполнить запрос:

1. Нажмите **Query your data** в нижней панели окна New Relic, в левом нижнем углу. Редактор NRQL откроется поверх текущей страницы.
2. Над редактором проверьте аккаунт: нужен тот, куда программа отправляет данные.
3. Вставьте запрос и нажмите **Run** (или Ctrl+Enter).
4. Если результат показан графиком, переключите вид на **Table**.

Без `SINCE` запрос берёт последний час. Без `LIMIT` таблица показывает до 100 строк; больше даёт, например, `LIMIT 500`.

Атрибуты OpenTelemetry в NRQL пишутся как есть, с точками. Имя спана хранится в атрибуте `name`, длительность — в `duration.ms`.

**Последние запуски и их результат:**

```sql
FROM Span SELECT timestamp, githubbackup.run.outcome, githubbackup.dry_run, duration.ms
WHERE service.name = 'GitHubBackup' AND name = 'backup.run'
SINCE 7 days ago LIMIT 20
```

**Неуспешные репозитории за неделю:**

```sql
FROM Span SELECT count(*)
WHERE service.name = 'GitHubBackup' AND name = 'backup.repository' AND githubbackup.status = 'Failed'
FACET githubbackup.repository, error.type
SINCE 7 days ago
```

**Самые долгие операции git:**

```sql
FROM Span SELECT average(duration.ms), max(duration.ms), count(*)
WHERE service.name = 'GitHubBackup' AND name IN ('clone', 'fetch', 'archive', 'ls-remote refs')
FACET name
SINCE 7 days ago
```

**Почему репозиторий не архивировался (решение проверки изменений):**

```sql
FROM Span SELECT count(*)
WHERE service.name = 'GitHubBackup' AND name = 'backup.repository'
FACET githubbackup.change.decision, githubbackup.change.reason
SINCE 7 days ago
```

**Число репозиториев по статусу (метрика):**

```sql
FROM Metric SELECT sum(githubbackup.repositories)
WHERE service.name = 'GitHubBackup'
FACET githubbackup.status
SINCE 7 days ago TIMESERIES 1 day
```

**Длительность запуска (метрика-гистограмма, секунды):**

```sql
FROM Metric SELECT average(githubbackup.run.duration), max(githubbackup.run.duration)
WHERE service.name = 'GitHubBackup'
FACET githubbackup.run.outcome
SINCE 30 days ago
```

**Размер архивов:**

```sql
FROM Metric SELECT average(githubbackup.archive.size) / 1e6 AS 'MB, среднее'
WHERE service.name = 'GitHubBackup'
FACET githubbackup.target
SINCE 30 days ago
```

**Предупреждения и ошибки в логах:**

```sql
FROM Log SELECT timestamp, severity.text, message, Repository, Operation
WHERE service.name = 'GitHubBackup' AND severity.text IN ('Warning', 'Error', 'Fatal')
SINCE 1 day ago LIMIT 100
```

Значения уровня (`severity.text`) зависят от того, как их передаёт логгер. Если запрос пуст, посмотрите реальные значения: `FROM Log SELECT uniques(severity.text) WHERE service.name = 'GitHubBackup' SINCE 1 day ago`.

**Какие атрибуты вообще есть:**

```sql
FROM Span SELECT keyset() WHERE service.name = 'GitHubBackup' SINCE 1 day ago
```

## Источники

- Введение в NRQL: https://docs.newrelic.com/docs/nrql/get-started/introduction-nrql-new-relics-query-language/
- Справка по синтаксису NRQL: https://docs.newrelic.com/docs/nrql/nrql-syntax-clauses-functions/
- OpenTelemetry в интерфейсе APM: https://docs.newrelic.com/docs/opentelemetry/get-started/apm-monitoring/opentelemetry-apm-intro/
- Distributed tracing: https://docs.newrelic.com/docs/distributed-tracing/concepts/introduction-distributed-tracing/
