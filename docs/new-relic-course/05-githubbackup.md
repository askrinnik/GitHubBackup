# Урок 5. Подключение GitHubBackup

**Коротко.** Задайте три настройки: включите телеметрию, укажите адрес New Relic вашего региона и передайте ключ лицензии через переменную окружения `GitHubBackup__OpenTelemetry__Headers`. Запустите копию, через 1–2 минуты сервис `GitHubBackup` появится в New Relic.

> **Условие.** Ключ `OpenTelemetry:Headers` реализуется в issue #101. Пока его нет в коде, этот урок описывает будущую настройку. До слияния #101 заголовок `api-key` передать в программу нельзя: стандартную переменную `OTEL_EXPORTER_OTLP_HEADERS` GitHubBackup не читает, потому что хост собирается с `DisableDefaults = true`.

## Три настройки

| Ключ конфигурации | Значение | Где задать |
|---|---|---|
| `OpenTelemetry:Enabled` | `true` | `appsettings.json` или редактор настроек (группа **Telemetry**) |
| `OpenTelemetry:OtlpEndpoint` | US: `https://otlp.nr-data.net:4317`<br>EU: `https://otlp.eu01.nr-data.net:4317` | `appsettings.json` или редактор настроек |
| `OpenTelemetry:Headers` | `api-key=<ключ лицензии>` | **только** переменная окружения `GitHubBackup__OpenTelemetry__Headers` |

**Формат `Headers`.** Пары `имя=значение` через запятую: `name1=value1,name2=value2`. Для New Relic нужна одна пара: `api-key=<ключ>`. Значение отделяется от имени первым знаком `=`.

**Почему двойное подчёркивание.** .NET превращает `__` в имени переменной в `:` ключа конфигурации. Префикс `GitHubBackup__` говорит программе, что переменная — её. Поэтому `GitHubBackup__OpenTelemetry__Headers` становится ключом `OpenTelemetry:Headers`.

**Почему порт 4317.** GitHubBackup отправляет данные по OTLP/gRPC. Стандартный порт gRPC — 4317.

## Правила, которые проверит программа (по плану #101)

- Если `Headers` задан, адрес должен быть `https`. Адрес `http` разрешён только для loopback (`localhost`, `127.0.0.1`, `[::1]`). Иначе программа не стартует и вернёт код `2`.
- Если `Headers` записан неправильно (нет `=`, пустое имя или значение, лишняя запятая), программа не стартует и вернёт код `2`.
- Сообщение об ошибке называет ключ, но никогда не содержит значения.
- Значение `Headers` маскируется как `***` в логах, в том числе в логах, отправленных в New Relic.
- Редактор настроек не показывает и не пишет `Headers`.

## Шаги

```
1 ключ лицензии ──> 2 переменная окружения ──> 3 Enabled + Endpoint ──> 4 пробный запуск ──> 5 проверка в New Relic
```

**Шаг 1. Создайте ключ** типа **Ingest - License** (урок 3).

**Шаг 2. Задайте переменную окружения** для своего пользователя Windows. Используйте окно, а не команду, чтобы ключ не попал в историю оболочки:

1. Нажмите Win, введите «Изменение переменных среды для вашей учётной записи» (англ. «Edit environment variables for your account»).
2. В верхнем списке «Переменные среды пользователя» нажмите **Создать**.
3. Имя: `GitHubBackup__OpenTelemetry__Headers`. Значение: `api-key=` и сразу после знака `=` ключ.
4. Нажмите **OK**. Закройте и снова откройте терминал или программу: процесс видит переменную только после перезапуска.

Переменная `GitHubBackup__*` не передаётся процессам git и 7-Zip: `ProcessRunner` убирает такие переменные.

**Шаг 3. Включите телеметрию и задайте адрес.** Вариант А — редактор настроек `GitHubBackup.exe`, группа **Telemetry**: флажок и поле адреса, затем **Save** и перезапуск программы. Вариант Б — `appsettings.json`:

```json
"OpenTelemetry": {
  "Enabled": true,
  "OtlpEndpoint": "https://otlp.nr-data.net:4317"
}
```

Для пробы без изменения файла можно передать флаги в командной строке. В них нет секрета, поэтому это безопасно:

```
GitHubBackup.Cli.exe backup --dry-run --OpenTelemetry:Enabled=true --OpenTelemetry:OtlpEndpoint=https://otlp.nr-data.net:4317
```

Никогда не передавайте `--OpenTelemetry:Headers=...` в командной строке.

**Шаг 4. Запустите копию.** `--dry-run` ничего не меняет на диске, но создаёт трейсы и метрики. Консольная программа отправляет метрики в конце запуска. Настольная программа отправляет их периодически и при закрытии.

**Шаг 5. Проверьте в New Relic** (урок 6). Первые данные появляются через 1–2 минуты.

## Что отправляет GitHubBackup

**Сервис:** `GitHubBackup` (атрибут ресурса `service.name`).

**Спаны:**

```
backup.run                      githubbackup.dry_run, githubbackup.run.outcome
├── backup.discovery            при --repo — точечная проверка записи; нет, если у записи нет ID или она исключена
├── backup.repository           githubbackup.repository, githubbackup.status, githubbackup.change.decision ...
│   ├── ls-remote refs
│   ├── ls-remote default-branch
│   ├── clone | fetch
│   └── archive                 githubbackup.archive.size
└── backup.repository           вики того же репозитория (githubbackup.target = Wiki)
```

**Метрики (meter `GitHubBackup`):**

| Метрика | Тип и единица | Атрибуты |
|---|---|---|
| `githubbackup.repositories` | Counter, `{repository}` | `githubbackup.status`, `githubbackup.target`, `githubbackup.dry_run` |
| `githubbackup.repository.duration` | Histogram, `s` | те же |
| `githubbackup.run.duration` | Histogram, `s` | `githubbackup.run.outcome`, `githubbackup.dry_run` |
| `githubbackup.archive.size` | Histogram, `By` | `githubbackup.target` |

**Логи:** те же записи, что и в файлах логов, с атрибутами `RunId`, `Repository`, `Operation`. Секреты заменены на `***`.

**Приватность.** Атрибут `githubbackup.repository` содержит имена репозиториев, в том числе приватных. Они уйдут в New Relic. Решите заранее, допустимо ли это для вас.

Подробности — в `README.md` проекта, раздел «Telemetry (OpenTelemetry)».

## Источники

- OTLP-endpoint New Relic: https://docs.newrelic.com/docs/opentelemetry/best-practices/opentelemetry-otlp/
- Конфигурация .NET и переменные окружения (`__` → `:`): https://learn.microsoft.com/dotnet/core/extensions/configuration-providers#environment-variable-configuration-provider
- План issue #101: `docs/plans/feature-101-new-relic-telemetry.md`
