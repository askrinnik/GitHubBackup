# Урок 6. Где смотреть данные

**Коротко.** Начните с **APM & Services** → сервис `GitHubBackup`. Там есть трейсы, логи и ошибки. Метрики `githubbackup.*` удобнее смотреть в **Metrics explorer** или через **Query your data** (NRQL). Логам посвящён урок 7, запросам NRQL и дашборду — урок 8.

Названия разделов в интерфейсе New Relic иногда меняются. Если пункта нет на месте, найдите его через поиск в левом меню.

## Карта интерфейса

```
Левое меню
├── APM & Services ──> GitHubBackup ──┬── Summary
│                                     ├── Distributed tracing
│                                     ├── Logs                 (урок 7)
│                                     └── Errors inbox
├── Query your data ──> NRQL-редактор, Metrics explorer        (урок 8)
├── Logs            ──> все логи аккаунта                      (урок 7)
├── Dashboards                                                 (урок 8)
└── Alerts                                                     (урок 9)
```

## APM & Services

1. Откройте **APM & Services** в левом меню.
2. Найдите `GitHubBackup` в списке. Сервис OpenTelemetry отмечен значком OpenTelemetry.
3. Откройте сервис. Страница **Summary** показывает обзор: длительность, число операций, ошибки.

Сервис появляется после первых данных. Если его нет, перейдите к уроку 11.

## Distributed tracing

1. В сервисе `GitHubBackup` откройте **Distributed tracing**.
2. Выберите трейс `backup.run`.
3. Под графиком длительностей — дерево спанов, как в уроке 5. Красная метка — спан с ошибкой.
4. Нажмите на название спана, а не на кнопку с числом дочерних спанов: кнопка только сворачивает и разворачивает дерево. Справа откроется панель спана, по умолчанию на вкладке **Performance**.
5. Откройте вкладку **Attributes**. Там атрибуты спана: `githubbackup.status`, `githubbackup.change.decision`, `githubbackup.operation`, `error.type` и другие. Строка **Search attributes and keys** фильтрует список.

Исход запуска (`githubbackup.run.outcome`: `completed`, `cancelled`, `stopped`, `failed`) есть только у верхнего спана `backup.run`. Отменённый запуск не считается ошибкой: счётчик errors у трейса остаётся 0.

Вкладка **Logs** рядом с **Trace details** показывает логи этого запуска (урок 7).

## Logs

Раздел **Logs** сервиса показывает логи только этого сервиса. Раздел **Logs** в левом меню показывает логи всего аккаунта. Поиск и анализ логов — в уроке 7.

## Metrics explorer

1. В сервисе `GitHubBackup` откройте **Metrics explorer** (группа **DATA** меню сервиса) или найдите «Metrics explorer» в поиске.
2. Найдите `githubbackup.repositories`.
3. Выберите агрегацию (`sum`) и разбивку (facet) по `githubbackup.status`.

## Query your data: как выполнить NRQL-запрос

NRQL — язык запросов New Relic. Запросы используют уроки 7–10.

Формат запроса: `FROM <тип> SELECT <что> WHERE <фильтр> FACET <группировка> SINCE <период>`.

1. Нажмите **Query your data** в нижней панели окна New Relic, в левом нижнем углу. Редактор NRQL откроется поверх текущей страницы.
2. Над редактором проверьте аккаунт: нужен тот, куда программа отправляет данные.
3. Вставьте запрос и нажмите **Run** (или Ctrl+Enter).
4. Если результат показан графиком, переключите вид на **Table**.

Без `SINCE` запрос берёт последний час. Без `LIMIT` таблица показывает до 100 строк; больше даёт, например, `LIMIT 500`.

Атрибуты OpenTelemetry в NRQL пишутся как есть, с точками. Имя спана хранится в атрибуте `name`, длительность — в `duration.ms`.

**Какие атрибуты есть у спанов:**

```sql
FROM Span SELECT keyset() WHERE service.name = 'GitHubBackup' SINCE 1 day ago
```

Так же работает `FROM Log` и `FROM Metric`.

## Источники

- Введение в NRQL: https://docs.newrelic.com/docs/nrql/get-started/introduction-nrql-new-relics-query-language/
- OpenTelemetry в интерфейсе APM: https://docs.newrelic.com/docs/opentelemetry/get-started/apm-monitoring/opentelemetry-apm-intro/
- Distributed tracing: https://docs.newrelic.com/docs/distributed-tracing/concepts/introduction-distributed-tracing/
