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

Условие и политику создаёт один мастер. Политику он создаёт на последнем шаге.

```
Create alert condition ──> Write your own query
  1. Tell us where to look   — NRQL-запрос
  2. Set thresholds          — агрегация и порог
  3. Add details             — имя условия и политика GitHubBackup
```

**Шаг 1. Запрос.** Откройте **Alerts**. В пустом аккаунте нажмите **Create alert condition**. Если условия уже есть, откройте **Alert conditions** → **New alert condition**. Выберите **Write your own query**. Вставьте запрос и нажмите **Run**, чтобы проверить синтаксис:

```sql
FROM Span SELECT count(*)
WHERE service.name = 'GitHubBackup'
  AND ((name = 'backup.run' AND githubbackup.run.outcome != 'completed')
    OR (name = 'backup.repository' AND githubbackup.status = 'Failed'))
```

Пустой график — это нормально: запрос считает только сбои.

**Шаг 2. Порог.** Экран **Set thresholds** состоит из двух колонок. Слева — **Fine-tune your signal**, справа — **Set condition thresholds**.

| Блок | Поле | Значение | Почему |
|---|---|---|---|
| Data aggregation | Window duration | 5 minutes | Запуск короткий |
| Data aggregation | Use sliding window aggregation | выключено | Скользящее окно здесь не нужно |
| Streaming method | Event timer | выбрано | Данные приходят редко (только во время запуска). Event timer подходит для редких данных |
| Streaming method | Timer | **5** minutes (по умолчанию 1) | Таймер ждёт новые данные перед закрытием окна. Предупреждение «Make this match your data frequency» напоминает об этом |
| Gap filling strategy | Fill data gaps with | None | Пропуски между запусками не заполняются |
| Set condition thresholds | тип | Static | Фиксированный порог |
| Set condition thresholds | Severity level | Critical | Любой сбой — проблема |
| Set condition thresholds | When a query returns a value | **above** `0` **at least once in** 5 minutes | Хотя бы один сбойный span открывает alert event |
| Set condition thresholds | Add lost signal threshold | не добавлять | Между запусками данных нет. Это нормально, issue открываться не должен |

Счётчик «N critical alert events» в правом верхнем углу — это предпросмотр. Он показывает, сколько раз условие сработало бы на уже собранных данных. Настоящих оповещений он не создаёт.

**Шаг 3. Имя и политика.** Экран **Add details**. Если политик ещё нет, мастер предлагает создать первую политику с именем `Initial policy`.

| Поле | Значение | Почему |
|---|---|---|
| Name your alert condition | `GitHubBackup run failed` | Имя видно в письме и в списке условий |
| Policy name | `GitHubBackup` (вместо `Initial policy`) | Workflow в шаге 4 фильтрует по этому имени |
| Group alert events into issues | **One issue per policy** (по умолчанию) | Одно условие в политике — один issue на сбой |
| Correlate and suppress noise | выключено | Условие одно, коррелировать нечего |
| Close open alert events after | 3 days (по умолчанию) | Alert event закрывается сам, если его никто не закрыл |
| Title template, Description template | пусто | Необязательные поля; New Relic подставит заголовок сам |
| Runbook URL | пусто | Инструкции на случай сбоя нет; ссылку можно добавить позже |
| Enable on save | включено | Иначе условие сохранится выключенным и не сработает |

Нажмите **Save & set up notifications**. Мастер сохранит условие и политику и перейдёт к настройке уведомлений (шаг 4).

**Шаг 4. Куда слать.** Кнопка **Save & set up notifications** сразу открывает экран **Configure your workflow**. Workflow можно создать и позже: **Alerts** → **Workflows** → **Add a workflow**.

| Блок | Поле | Значение | Почему |
|---|---|---|---|
| — | Имя workflow | `GitHubBackup email` | Поле обязательное |
| Filter data (Basic) | Tag | пусто | Пустое поле не сужает фильтр |
| Filter data (Basic) | Policy | `GitHubBackup` | Письма приходят только по этой политике |
| Filter data (Basic) | Priority | пусто | Пустое поле пропускает issue любого приоритета, в том числе Critical |
| Additional settings | — | по умолчанию | Для урока не нужны |
| Notify | Add channel → **Email** | ваш адрес | Канал уведомлений |

Справа от фильтра должна стоять отметка **Filter is valid!**. Канал Email по умолчанию шлёт письмо при каждом изменении состояния issue: `Activated`, `Acknowledged`, `Closed`, `Investigating`. Для урока этого достаточно.

Прокрутите экран до блока **Test this workflow** и нажмите **Test workflow**. Тест берёт данные из аккаунта и шлёт пример уведомления на ваш адрес.

После теста под фильтром может появиться предупреждение «This filter doesn't match any past issues. Is that OK?». Это нормально. Политика только что создана, и issue по ней ещё не было. Счётчик alert events на шаге 2 был только предпросмотром и issue не создавал.

Если по политике ещё не было issue, тестовое письмо не придёт. Нажмите **Activate workflow** и проверьте алерт на настоящем сбое.

**Шаг 5. Проверка на настоящем сбое.** Условие срабатывает, если `githubbackup.run.outcome` не равен `completed`. Отменённый запуск получает `cancelled`. Это самый безопасный способ вызвать сбой:

1. Откройте `GitHubBackup.exe`.
2. Включите **Dry run**. Запуск ничего не изменит на диске, но отправит трейсы.
3. Нажмите **Backup account** и сразу нажмите **Cancel**.
   Чтобы убедиться, что отмена дошла до New Relic, откройте трейс `backup.run`. Нажмите на название верхнего span `backup.run`, а не на кнопку с числом дочерних span. Справа откроется панель. На вкладке **Attributes** у `githubbackup.run.outcome` должно стоять `cancelled`. На шкале отмена не видна, и счётчик errors остаётся 0: отмена — ожидаемый исход, а не ошибка.
4. Подождите 10–15 минут: окно агрегации (5 минут), таймер Event timer (5 минут) и задержка доставки данных.
5. Проверьте почту и **Alerts** → **Issues & Activity**. В списке появится issue с приоритетом **Critical** и названием `GitHubBackup query result is > 0.0 on 'GitHubBackup run failed'`. Письмо содержит то же название, политику, условие и NRQL-запрос.

Сам issue не закроется: между запусками данных нет, и условию нечем подтвердить, что сбой прошёл. Закройте его вручную. В письме есть кнопка **Close issue**. На странице issue эта команда спрятана в меню **⋯** справа от кнопки **Acknowledge**. После закрытия придёт второе письмо, «Critical priority issue is closed»: канал Email шлёт письма и на состояние `Closed`. Иначе его закроет политика через срок **Close open alert events after** (3 дня). Если письмо так и не пришло, проверьте span в **Query your data**: `FROM Span SELECT * WHERE service.name = 'GitHubBackup' AND name = 'backup.run' SINCE 30 minutes ago`.

**Альтернатива.** «Запуск не пришёл вовремя» — отдельное условие: `FROM Span SELECT count(*) WHERE service.name = 'GitHubBackup' AND name = 'backup.run'`, порог **below 1** за окно больше интервала запусков (например, 25 часов при ежедневном запуске). Окно такой длины имеет ограничения; начните с основного алерта.

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

**Алерт на расход.** Создайте NRQL-условие с запросом `FROM NrMTDConsumption SELECT latest(GigabytesIngested) WHERE productLine = 'DataPlatform'`, порог `above 50` (половина лимита). В условии алерта `SINCE` не пишут: период задаёт окно агрегации.

New Relic пишет `NrConsumption` раз в час, а `NrMTDConsumption` считает из него. Поэтому настройки отличаются от алерта на сбой:

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

После сохранения откроется сводка с сообщением «Alert condition enabled» и напоминанием подключить политику к workflow. Workflow политики `GitHubBackup` уже создан в шаге 4, так что нажмите **Close**. New Relic сам предупредит на 85 %, но собственный ранний алерт надёжнее.

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
