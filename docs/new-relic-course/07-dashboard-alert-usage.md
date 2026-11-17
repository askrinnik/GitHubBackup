# Урок 7. Дашборд, алерт, хранение данных и расход

**Коротко.** Соберите дашборд из NRQL-запросов урока 6. Создайте один алерт на неуспешный запуск. Раз в месяц проверяйте расход данных: бесплатный лимит — 100 GB в месяц.

## Простой дашборд

1. Откройте **Query your data** и выполните запрос из урока 6.
2. Выберите вид графика (Line, Bar, Table, Billboard).
3. Нажмите **Add to dashboard** → **New dashboard**. Назовите его `GitHubBackup`.
4. Повторите для других запросов, выбирая уже созданный дашборд.

Другой путь: **Dashboards** → **Create a dashboard** → **Add widget** (или «+») → вставить NRQL.

Предлагаемый состав дашборда:

```
┌──────────────────────────────┬──────────────────────────────┐
│ Последние запуски (Table)    │ Репозитории по статусу (Bar) │
├──────────────────────────────┼──────────────────────────────┤
│ Длительность запуска (Line)  │ Неуспешные репозитории (Tbl) │
├──────────────────────────────┴──────────────────────────────┤
│ Ошибки и предупреждения в логах (Table)                     │
└─────────────────────────────────────────────────────────────┘
```

## Алерт: неуспешный запуск

**Идея.** Алерт срабатывает, если запуск закончился не со статусом `completed` или хотя бы один репозиторий получил статус `Failed`.

```
Alert policy "GitHubBackup" ──> Condition (NRQL, порог > 0) ──> Issue ──> Workflow ──> Destination (email)
```

**Шаг 1. Политика.** **Alerts** → **Alert policies** → **New alert policy**. Имя: `GitHubBackup`. Группировку issue оставьте по умолчанию.

**Шаг 2. Условие.** **Alerts** → **Alert conditions** → **New alert condition** → **Write your own query** (NRQL). Запрос:

```sql
FROM Span SELECT count(*)
WHERE service.name = 'GitHubBackup'
  AND ((name = 'backup.run' AND githubbackup.run.outcome != 'completed')
    OR (name = 'backup.repository' AND githubbackup.status = 'Failed'))
```

Настройки:

| Параметр | Значение | Почему |
|---|---|---|
| Window duration | 5 minutes | Запуск короткий |
| Streaming method | **Event timer**, timer 5 minutes | Данные приходят редко (только во время запуска). Event timer подходит для редких данных |
| Threshold | Critical: **above 0**, **at least once in** 5 minutes | Любой сбой — проблема |
| Signal loss | Не открывать issue при потере сигнала | Между запусками данных нет. Это нормально |
| Policy | `GitHubBackup` | Шаг 1 |

**Шаг 3. Куда слать.** **Alerts** → **Workflows** → **Add a workflow**. Фильтр — политика `GitHubBackup`. Канал — **Email** (ваш адрес). Нажмите **Test workflow**, чтобы проверить письмо.

**Альтернатива.** «Запуск не пришёл вовремя» — отдельное условие: `FROM Span SELECT count(*) WHERE service.name = 'GitHubBackup' AND name = 'backup.run'`, порог **below 1** за окно больше интервала запусков (например, 25 часов при ежедневном запуске). Окно такой длины имеет ограничения; начните с основного алерта.

## Хранение данных (retention)

Срок хранения задан по пространствам данных (namespace). Значения по умолчанию для варианта Original Data:

| Данные | Срок по умолчанию |
|---|---|
| Спаны (Distributed traces) | 8 дней |
| Логи (Logging) | 30 дней |
| Метрики `Metric` (сырые точки) | 30 дней |
| Метрики `Metric` (агрегаты) | 13 месяцев |

Где посмотреть: меню пользователя → **Manage your data** → **Data retention**. На бесплатном тарифе изменить сроки нельзя: нужен Standard или выше.

**Вывод для GitHubBackup.** Детальные трейсы живут неделю. Тренды за месяцы смотрите по метрикам.

## Контроль расхода на бесплатном тарифе

**Где смотреть.** Меню пользователя → **Administration** → **Usage** (или найдите «Usage» в поиске). Там видно, сколько GB принято за месяц и из каких источников.

**NRQL: принято за текущий месяц, GB:**

```sql
FROM NrMTDConsumption SELECT latest(GigabytesIngested) WHERE productLine = 'DataPlatform'
```

**NRQL: по дням и по источникам за 30 дней:**

```sql
FROM NrConsumption SELECT sum(GigabytesIngested)
WHERE productLine = 'DataPlatform'
FACET usageMetric SINCE 30 days ago TIMESERIES 1 day
```

**Алерт на расход.** Создайте NRQL-условие с запросом `FROM NrMTDConsumption SELECT latest(GigabytesIngested) WHERE productLine = 'DataPlatform'`, порог `above 50` (половина лимита). New Relic сам предупредит на 85 %, но собственный ранний алерт надёжнее.

**Как снизить расход GitHubBackup**, если он вдруг вырос:

- поднимите уровень логов (`Serilog:MinimumLevel`) с `Debug` до `Information`: тот же уровень действует и для логов, отправленных по OTLP;
- выключайте телеметрию (`OpenTelemetry:Enabled=false`) для разовых отладочных запусков.

Цифры New Relic приблизительные и не совпадают точно со счётом.

## Источники

- Дашборды: https://docs.newrelic.com/docs/query-your-data/explore-query-data/dashboards/introduction-dashboards/
- NRQL-условия алертов: https://docs.newrelic.com/docs/alerts/create-alert/create-alert-condition/alert-conditions/
- Методы агрегации (Event flow, Event timer): https://docs.newrelic.com/docs/alerts/create-alert/fine-tune/choose-your-aggregation-method/
- Workflows и уведомления: https://docs.newrelic.com/docs/alerts/get-notified/incident-workflows/
- Сроки хранения: https://docs.newrelic.com/docs/data-apis/manage-data/manage-data-retention/
- Запросы и алерты по расходу: https://docs.newrelic.com/docs/accounts/accounts-billing/new-relic-one-pricing-billing/usage-queries-alerts/
- Тарифы: https://newrelic.com/pricing
