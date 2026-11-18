# Урок 10. Хранение данных и расход

**Коротко.** New Relic хранит данные ограниченное время: спаны — 8 дней, агрегаты метрик — 13 месяцев. Бесплатный лимит — 100 GB в месяц. Раз в месяц проверяйте расход и заведите алерт на половину лимита.

## Хранение данных (retention)

Срок хранения задан по пространствам данных (namespace). Значения по умолчанию для варианта Original Data:

| Данные | Срок по умолчанию |
|---|---|
| Спаны (Distributed traces) | 8 дней |
| Логи (Logging) | 30 дней |
| Метрики `Metric` (сырые точки) | 30 дней |
| Метрики `Metric` (агрегаты) | 13 месяцев |

Где посмотреть: меню пользователя (нажмите на аватар в правом верхнем углу) → **Manage your data** → **Data retention**. На бесплатном тарифе изменить сроки нельзя: нужен Standard или выше.

**Вывод для GitHubBackup.** Детальные трейсы живут неделю. Тренды за месяцы смотрите по метрикам.

## Контроль расхода на бесплатном тарифе

**Где смотреть.** Меню пользователя → **Administration** → **Usage** (или найдите «Usage» в поиске). Там видно, сколько GB принято за месяц и из каких источников.

**NRQL: принято за текущий месяц, GB:**

```sql
FROM NrMTDConsumption SELECT latest(GigabytesIngested) WHERE productLine = 'DataPlatform' SINCE 1 day ago
```

`SINCE 1 day ago` обязателен. Без него запрос смотрит только последние 60 минут. События `NrMTDConsumption` приходят редко, и в такое окно значение может не попасть: результат будет `null`. Результат — GB с начала месяца, например `0.009` ≈ 9 MB.

Фильтр `productLine = 'DataPlatform'` оставляет только приём данных. В тех же таблицах лежат события линии `Compute`: это вычислительные единицы (CCU), которые тратят ваши запросы. У них нет `GigabytesIngested`, и без фильтра `latest()` может взять такое событие. Какие линии есть в аккаунте, покажет запрос `FROM NrMTDConsumption SELECT uniques(productLine) SINCE 1 day ago`.

**NRQL: по дням и по источникам за 30 дней:**

```sql
FROM NrConsumption SELECT sum(GigabytesIngested)
WHERE productLine = 'DataPlatform'
FACET usageMetric SINCE 30 days ago TIMESERIES 1 day
```

Колонки называются по `usageMetric`: `Logging Bytes`, `Metrics Bytes`, `Tracing Bytes`. Несмотря на слово «Bytes», значения в GB. Суффикс `μ` означает миллионные доли: `456 μ` = 0.000456 GB ≈ 0.46 MB. Сумма колонок за все дни примерно равна результату первого запроса. Текущие сутки в таблицу ещё не попадают.

**Алерт на расход.** Создайте NRQL-условие тем же мастером, что в уроке 9, с запросом `FROM NrMTDConsumption SELECT latest(GigabytesIngested) WHERE productLine = 'DataPlatform'`, порог `above 50` (половина лимита). В условии алерта `SINCE` не пишут: период задаёт окно агрегации.

New Relic пишет `NrConsumption` раз в час, а `NrMTDConsumption` считает из него. Поэтому настройки отличаются от алерта на сбой (урок 9):

| Блок | Поле | Значение | Почему |
|---|---|---|---|
| Data aggregation | Window duration | **60** minutes | Одно событие в час; максимум — 120 минут |
| Streaming method | **Event timer**, Timer | 5 minutes | Редкие данные; документация New Relic рекомендует Event timer для этого запроса |
| Set condition thresholds | Severity level | Critical | — |
| Set condition thresholds | When a query returns a value | **above** `50` **at least once in** 60 minutes | Достаточно одного значения выше половины лимита |
| Set condition thresholds | Add lost signal threshold | не добавлять | Пропуск часа — не повод для алерта |
| Add details | Name your alert condition | `New Relic data ingest above 50 GB` | — |
| Add details | Policy | существующая `GitHubBackup` | Её workflow уже шлёт письма; новая политика потребовала бы ещё один workflow |

Политика `GitHubBackup` группирует события как **One issue per policy**. Если сбой запуска и превышение расхода совпадут по времени, они попадут в один issue.

После сохранения откроется сводка с сообщением «Alert condition enabled» и напоминанием подключить политику к workflow. Workflow политики `GitHubBackup` уже создан в уроке 9, так что нажмите **Close**.

New Relic сам предупредит на 85 % лимита, но собственный ранний алерт надёжнее.

**Как снизить расход GitHubBackup**, если он вырос. Сначала выясните, какие записи логов занимают больше места: это покажет запрос с `bytecountestimate()` из урока 7. Затем:

- поднимите уровень логов (`Serilog:MinimumLevel`) с `Debug` до `Information`: тот же уровень действует и для логов, отправленных по OTLP;
- выключайте телеметрию (`OpenTelemetry:Enabled=false`) для разовых отладочных запусков.

Цифры New Relic приблизительные и не совпадают точно со счётом.

## Источники

- Сроки хранения: https://docs.newrelic.com/docs/data-apis/manage-data/manage-data-retention/
- Запросы и алерты по расходу: https://docs.newrelic.com/docs/accounts/accounts-billing/new-relic-one-pricing-billing/usage-queries-alerts/
- Тарифы: https://newrelic.com/pricing
