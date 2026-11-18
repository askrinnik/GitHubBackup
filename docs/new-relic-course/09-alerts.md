# Урок 9. Алерты

**Коротко.** Создайте алерт на неуспешный запуск: политика `GitHubBackup`, NRQL-условие и workflow с письмом. Проверьте его на отменённом запуске. Алерт на расход данных — в уроке 10.

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

## Алерт по логам

Условие можно строить и по `FROM Log` (урок 7). Пример: в логах появилась ошибка.

```sql
FROM Log SELECT count(*) WHERE service.name = 'GitHubBackup' AND level = 'Error'
```

Настройки — как у алерта на сбой: **Event timer**, Timer 5 minutes, порог **above 0 at least once in** 5 minutes, политика `GitHubBackup`.

## Источники

- NRQL-условия алертов: https://docs.newrelic.com/docs/alerts/create-alert/create-alert-condition/alert-conditions/
- Методы агрегации (Event flow, Event timer): https://docs.newrelic.com/docs/alerts/create-alert/fine-tune/choose-your-aggregation-method/
- Workflows и уведомления: https://docs.newrelic.com/docs/alerts/get-notified/incident-workflows/
