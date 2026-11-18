# Урок 8. Неполадки

**Коротко.** Если данных нет, проверяйте по порядку: регион адреса, тип ключа, переменную окружения, сеть до порта 4317, события `NrIntegrationError`. В 9 случаях из 10 причина — регион или тип ключа.

## Порядок проверки

```
1 программа стартовала? ──> 2 телеметрия включена? ──> 3 регион адреса = регион аккаунта?
        ──> 4 ключ типа Ingest - License? ──> 5 переменная видна процессу?
        ──> 6 порт 4317 открыт? ──> 7 NrIntegrationError
```

**1. Программа стартовала.** Код возврата `2` и строка в stderr с `OpenTelemetry:...` означают ошибку конфигурации. Частые случаи: `http` вместо `https` при заданном `Headers`; неправильный формат `Headers`. Сообщение называет ключ, значение в нём не показано.

Настольная программа (`GitHubBackup.exe`) не пишет в консоль. При ошибке конфигурации она показывает окно ошибки с заголовком `GitHub Backup` и первой строкой `Configuration error:`, а после него закрывается, не открыв главное окно. Код возврата при этом тоже `2`. Чтобы увидеть код, запустите программу из PowerShell и дождитесь её закрытия:

```powershell
(Start-Process .\GitHubBackup.exe -Wait -PassThru).ExitCode
```

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

## Источники

- Устранение проблем OTLP: https://docs.newrelic.com/docs/opentelemetry/best-practices/opentelemetry-otlp-troubleshooting/
- NrIntegrationError: https://docs.newrelic.com/docs/data-apis/manage-data/nrintegrationerror/
- OTLP-endpoint и порты: https://docs.newrelic.com/docs/opentelemetry/best-practices/opentelemetry-otlp/
- Ключи: https://docs.newrelic.com/docs/apis/intro-apis/new-relic-api-keys/
