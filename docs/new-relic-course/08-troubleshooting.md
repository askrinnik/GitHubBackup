# Урок 8. Неполадки

**Коротко.** Если данных нет, проверяйте по порядку: регион адреса, тип ключа, переменную окружения, сеть до порта 4317, события `NrIntegrationError`. В 9 случаях из 10 причина — регион или тип ключа.

## Порядок проверки

```
1 программа стартовала? ──> 2 телеметрия включена? ──> 3 регион адреса = регион аккаунта?
        ──> 4 ключ типа Ingest - License? ──> 5 переменная видна процессу?
        ──> 6 порт 4317 открыт? ──> 7 NrIntegrationError
```

**1. Программа стартовала.** Код возврата `2` и строка в stderr с `OpenTelemetry:...` означают ошибку конфигурации. Частые случаи: `http` вместо `https` при заданном `Headers`; неправильный формат `Headers`. Сообщение называет ключ, значение в нём не показано.

**2. Телеметрия включена.** `OpenTelemetry:Enabled` должен быть `true`. Редактор настроек применяет изменения только после перезапуска программы.

**3. Регион.** Откройте интерфейс. Если адрес браузера `one.eu.newrelic.com`, аккаунт в EU, и нужен `https://otlp.eu01.nr-data.net:4317`. Если `one.newrelic.com` — US и `https://otlp.nr-data.net:4317`. Ключ одного региона другой регион отклоняет.

**4. Тип ключа.** На странице **API keys** проверьте колонку типа: нужен **Ingest - License**. Ключ **User** (часто начинается с `NRAK-`) и ключ **Ingest - Browser** не подходят. Проверьте, что ключ не деактивирован.

**5. Переменная окружения.** Процесс видит переменную, только если он запущен после её создания. Перезапустите терминал или программу. Проверьте, что переменная существует, не показывая значение:

```powershell
[bool][Environment]::GetEnvironmentVariable('GitHubBackup__OpenTelemetry__Headers', 'User')
```

`True` — переменная есть. Проверьте также написание: два подчёркивания, `api-key=` в начале значения, без пробелов и кавычек.

**6. Сеть и порт 4317.** Корпоративный файрвол или прокси может закрывать нестандартные порты. Проверка:

```powershell
Test-NetConnection otlp.nr-data.net -Port 4317
```

Для EU — `otlp.eu01.nr-data.net`. Если `TcpTestSucceeded : False`, попробуйте порт 443: `https://otlp.nr-data.net:443` (New Relic принимает gRPC и на 443).

**7. NrIntegrationError.** Приёмник проверяет данные после ответа «успешно». Ошибки разбора и лимитов попадают в события `NrIntegrationError`:

```sql
FROM NrIntegrationError SELECT timestamp, newRelicFeature, category, message
SINCE 1 day ago LIMIT 100
```

Фильтр по виду данных: `WHERE newRelicFeature = 'Distributed Tracing'` (спаны), `'Metrics'`, `'Logs'`.

## Коды ошибок экспорта

Если в логах программы есть ошибки экспортёра OpenTelemetry, код подскажет причину:

| HTTP | gRPC | Причина | Что делать |
|---|---|---|---|
| 401 | 7 `Unauthenticated` | Нет заголовка `api-key` | Проверить шаги 4–5 |
| 403 | 16 `PermissionDenied` | Неверный ключ или чужой регион | Проверить шаги 3–4 |
| 429 | 8 `ResourceExhausted` | Превышен rate limit | Подождать; для GitHubBackup маловероятно |
| — | 4 `DeadlineExceeded` | Таймаут | Проверить сеть (шаг 6) |
| 502, 503 | 14 `Unavailable` | Временный сбой | Повтор обычно помогает |

## Ключ не утёк: как проверить

**В репозитории.** Ищите начало ключа (первые 8 символов, они видны на странице **API keys**):

```
git grep -n "<первые 8 символов>"
git log -p -S "<первые 8 символов>" --all
```

Обе команды должны ничего не найти. Вторая ищет и по истории.

**В файлах программы.** Ключ не должен быть в `appsettings.json`, `ui-settings.json`, файлах логов. По плану #101 программа маскирует значение `Headers` как `***`. Проверьте логи:

```powershell
Select-String -Path <папка логов>\*.log -Pattern "<первые 8 символов>"
```

**В New Relic.** Ключ не должен попасть в логи, отправленные в New Relic:

```sql
FROM Log SELECT count(*) WHERE service.name = 'GitHubBackup' AND message LIKE '%<первые 8 символов>%' SINCE 30 days ago
```

**В истории оболочки.** Если вы когда-то вводили ключ в PowerShell, он есть в файле истории PSReadLine: `(Get-PSReadLineOption).HistorySavePath`. Удалите эту строку из файла.

**Если ключ нашёлся.** Считайте его утёкшим. Создайте новый ключ, обновите переменную, удалите старый ключ (урок 3). Удаление строки из файла не помогает, если файл уже был опубликован.

## Источники

- Устранение проблем OTLP: https://docs.newrelic.com/docs/opentelemetry/best-practices/opentelemetry-otlp-troubleshooting/
- NrIntegrationError: https://docs.newrelic.com/docs/data-apis/manage-data/nrintegrationerror/
- OTLP-endpoint и порты: https://docs.newrelic.com/docs/opentelemetry/best-practices/opentelemetry-otlp/
- Ключи: https://docs.newrelic.com/docs/apis/intro-apis/new-relic-api-keys/
