# Урок 8. Запросы NRQL и дашборд

**Коротко.** Соберите дашборд `GitHubBackup` из запросов NRQL. Каждый запрос ниже — один виджет. Как выполнить запрос, описано в уроке 6.

## Как собрать дашборд

1. Откройте **Query your data** и выполните первый запрос из списка ниже.
2. Выберите вид графика, указанный у запроса (Line, Bar, Table, Billboard).
3. Нажмите **Add to dashboard** → **New dashboard**. Назовите его `GitHubBackup`.
4. Повторите для остальных запросов. В **Add to dashboard** выбирайте уже созданный дашборд.

Другой путь: **Dashboards** → **Create a dashboard** → **Add widget** (или «+») → вставить NRQL.

Предлагаемый состав дашборда:

```
┌──────────────────────────────┬──────────────────────────────┐
│ 1 Последние запуски (Table)  │ 5 Репозитории по статусу     │
│                              │   (Bar)                      │
├──────────────────────────────┼──────────────────────────────┤
│ 6 Длительность запуска       │ 2 Неуспешные репозитории     │
│   (Line)                     │   (Table)                    │
├──────────────────────────────┼──────────────────────────────┤
│ 3 Самые долгие операции git  │ 4 Почему не архивировался    │
│   (Bar)                      │   (Bar)                      │
├──────────────────────────────┼──────────────────────────────┤
│ 7 Размер архивов (Billboard) │ 8 Предупреждения и ошибки    │
│                              │   в логах (Table)            │
└──────────────────────────────┴──────────────────────────────┘
```

## Виджеты

**1. Последние запуски и их результат** (Table):

```sql
FROM Span SELECT timestamp, githubbackup.run.outcome, githubbackup.dry_run, duration.ms
WHERE service.name = 'GitHubBackup' AND name = 'backup.run'
SINCE 7 days ago LIMIT 20
```

**2. Неуспешные репозитории за неделю** (Table):

```sql
FROM Span SELECT count(*)
WHERE service.name = 'GitHubBackup' AND name = 'backup.repository' AND githubbackup.status = 'Failed'
FACET githubbackup.repository, error.type
SINCE 7 days ago
```

**3. Самые долгие операции git** (Bar):

```sql
FROM Span SELECT average(duration.ms), max(duration.ms), count(*)
WHERE service.name = 'GitHubBackup' AND name IN ('clone', 'fetch', 'archive', 'ls-remote refs')
FACET name
SINCE 7 days ago
```

**4. Почему репозиторий не архивировался — решение проверки изменений** (Bar):

```sql
FROM Span SELECT count(*)
WHERE service.name = 'GitHubBackup' AND name = 'backup.repository'
FACET githubbackup.change.decision, githubbackup.change.reason
SINCE 7 days ago
```

**5. Число репозиториев по статусу — метрика** (Bar или Line):

```sql
FROM Metric SELECT sum(githubbackup.repositories)
WHERE service.name = 'GitHubBackup'
FACET githubbackup.status
SINCE 7 days ago TIMESERIES 1 day
```

**6. Длительность запуска — метрика-гистограмма, секунды** (Line):

```sql
FROM Metric SELECT average(githubbackup.run.duration), max(githubbackup.run.duration)
WHERE service.name = 'GitHubBackup'
FACET githubbackup.run.outcome
SINCE 30 days ago TIMESERIES 1 day
```

**7. Размер архивов** (Billboard):

```sql
FROM Metric SELECT average(githubbackup.archive.size) / 1e6 AS 'MB, среднее'
WHERE service.name = 'GitHubBackup'
FACET githubbackup.target
SINCE 30 days ago
```

**8. Предупреждения и ошибки в логах** (Table):

```sql
FROM Log SELECT timestamp, level, message, Repository, Operation
WHERE service.name = 'GitHubBackup' AND level IN ('Warning', 'Error', 'Critical')
SINCE 1 day ago LIMIT 100
```

Ещё запросы по логам — в уроке 7, по расходу данных — в уроке 10. Любой из них можно добавить на дашборд так же.

## Источники

- Справка по синтаксису NRQL: https://docs.newrelic.com/docs/nrql/nrql-syntax-clauses-functions/
- Дашборды: https://docs.newrelic.com/docs/query-your-data/explore-query-data/dashboards/introduction-dashboards/
