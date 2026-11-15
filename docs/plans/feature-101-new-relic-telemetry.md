# План: #101 F3.15 — настройка телеметрии для New Relic

| Поле | Значение |
|---|---|
| Issue | [#101](https://github.com/askrinnik/GitHubBackup/issues/101) F3.15: Telemetry setup with New Relic |
| Lane | Feature |
| Сложность | L |
| Дата | 2026-11-15 |

## 1. Цель

GitHubBackup должен отправлять трейсы, метрики и логи в New Relic по OTLP (gRPC, https). New Relic требует ключ в заголовке `api-key`. Для этого появляется общий ключ конфигурации `OpenTelemetry:Headers` в формате `name=value[,name2=value2]`. Он подходит для любого приёмника OTLP. Значение ключа — секрет. Оно не попадает в лог, в вывод, в сообщения об ошибках, в аргументы процессов и в файлы, которые пишет программа. Требования: FR-10.5, FR-10.6, §7.2, FR-12.5, NFR-1.

Что установлено по коду:

- Хост собирается с `DisableDefaults = true`. Он читает `appsettings.json`, user-secrets, переменные `GitHubBackup__` и командную строку (`GitHubBackupHostBuilder.Create`). Поэтому SDK OpenTelemetry не видит переменную окружения `OTEL_EXPORTER_OTLP_HEADERS`: `OtlpExporterOptions` берёт значения из `IConfiguration` в DI, то есть из конфигурации хоста. Совет в README (раздел Telemetry) не работает.
- Есть обходной путь без маскирования. Переменная `GitHubBackup__OTEL_EXPORTER_OTLP_HEADERS` или аргумент `--OTEL_EXPORTER_OTLP_HEADERS=…` создаёт в конфигурации ключ `OTEL_EXPORTER_OTLP_HEADERS`, и SDK его прочитает. Значение при этом не регистрируется в маскировщике. План закрывает этот путь (п. 5).

## 2. Критерии приёмки

Из issue (дословно):

1. Способ передачи ключа New Relic выбран и описан в PRD
2. Ключ не попадает в лог, вывод и сообщения об ошибках; это покрыто тестами
3. Трейсы и метрики GitHubBackup видны в New Relic (ручная проверка описана в README)
4. CI зелёный

Добавлено по PRD и проверке покрытия:

5. (добавлено) Если задан `OpenTelemetry:Headers` и телеметрия включена, адрес должен быть `https`; `http` допускается только для loopback. Нарушение — код `2`, строка в stderr называет ключ и не содержит значения.
6. (добавлено) Если `Headers` записан неправильно (нет `=`, пустое имя или значение, пустой сегмент, управляющие символы), запуск завершается с кодом `2`. Сообщение называет ключ и не содержит значения.
7. (добавлено) Стандартные ключи `OTEL_EXPORTER_OTLP_HEADERS` и `OTEL_EXPORTER_OTLP_<SIGNAL>_HEADERS` не действуют, даже если их передать через `GitHubBackup__` или командную строку. Единственный источник заголовков — `OpenTelemetry:Headers`.
8. (добавлено) Значение маскируется везде, где работает `ISecretMasker`: в файлах лога, в живом логе UI, в логах, которые уходят по OTLP, в строке `Startup error:`, в истории запусков. Маскируются вся строка и каждое значение заголовка по отдельности. Маскирование действует и при `Enabled=false`.
9. (добавлено) Редактор настроек UI не показывает и не пишет `Headers`, а Save сохраняет его в файле. Если `Headers` задан, редактор при включённом флажке требует `https`-адрес (или `http` для loopback) — так же, как проверка при старте.
10. (добавлено) Переменная `GitHubBackup__OpenTelemetry__Headers` не попадает к дочерним процессам git и 7-Zip. Это уже обеспечивает `ProcessRunner`: он убирает переменные с префиксом `GitHubBackup__`.

## 3. Изменения требований (PRD 0.40 → 0.41)

Шапка: `Версия документа` → `0.41 (черновик)`, `Дата` → `2026-11-15`.

**FR-10.5.** Заменить пункт «экспорт по OTLP на заданный адрес (например, .NET Aspire Dashboard или Seq).» на:

> - экспорт по OTLP (gRPC) на заданный адрес (например, .NET Aspire Dashboard, Seq или New Relic). Если приёмник требует ключ, его передают заголовками OTLP в ключе `OpenTelemetry:Headers` (§7.2), например `api-key=<license key>` для New Relic. Это единственный источник заголовков: стандартные ключи `OTEL_EXPORTER_OTLP_HEADERS` и `OTEL_EXPORTER_OTLP_<SIGNAL>_HEADERS` не действуют. Значение `OpenTelemetry:Headers` — секрет (FR-10.6). Метрики отправляются с накопительной (cumulative) темпоральностью.

**FR-10.6.** Новый текст:

> - **FR-10.6.** Токен, значение `OpenTelemetry:Headers` (вся строка и каждое значение заголовка) и заголовки авторизации никогда не пишутся в лог: в сообщениях, свойствах и тексте исключений они заменяются на `***`.

**FR-12.5**, пункт «Телеметрия». Добавить в конец:

> Если в конфигурации задан `OpenTelemetry:Headers`, то при включённом флажке адрес должен иметь схему `https` или быть адресом `http` на loopback (§7.2). Ключ `OpenTelemetry:Headers` редактор не показывает и не пишет.

**§7.2, пример.** В раздел `OpenTelemetry` добавить `"Headers": null`:

```json
  "OpenTelemetry": {
    "Enabled": false,
    "OtlpEndpoint": "http://localhost:4317",
    "Headers": null
  },
```

**§7.2, «Кто пишет файл».** После предложения «Остальные ключи и разделы Save оставляет.» добавить:

> Ключ `OpenTelemetry:Headers` редактор не показывает и не пишет; Save его сохраняет. Ключ приёмника лучше задавать переменной `GitHubBackup__OpenTelemetry__Headers`, а не в файле и не в командной строке: командную строку видят другие процессы.

**§7.2, «Проверка при старте».** Слова «без стека и без значения `GitHub:Token`» заменить на «без стека и без значений `GitHub:Token` и `OpenTelemetry:Headers`». После правила `OpenTelemetry:OtlpEndpoint` добавить:

> - `OpenTelemetry:Headers` — если `OpenTelemetry:Enabled` равно `true` и ключ задан (не пустой): пары `имя=значение` через запятую. Имя — непустой токен заголовка HTTP, значение — непустое, без управляющих символов. Значение отделяется от имени первым `=`. Пробелы вокруг имён и значений отбрасываются. Пустой сегмент (`a=b,,c=d`, запятая в конце) — ошибка;
> - `OpenTelemetry:OtlpEndpoint` при `OpenTelemetry:Enabled` = `true` и заданном `OpenTelemetry:Headers` — схема `https`; `http` допускается только для loopback-адреса, чтобы ключ приёмника не уходил в сеть без шифрования;

**§7.2, «Startup error».** Слова «токен в ней заменён на `***`» заменить на «токен и значение `OpenTelemetry:Headers` в ней заменены на `***`».

**NFR-1.** Новый текст:

> | NFR-1 | **Безопасность.** Токен и значение `OpenTelemetry:Headers` не попадают в логи, аргументы процессов, `.git/config` и архивы (FR-10.6, FR-11.3) |

**История изменений**, новая строка:

> | 0.41 | 2026-11-15 | FR-10.5: заголовки OTLP задаются ключом `OpenTelemetry:Headers` (`имя=значение[,…]`, например `api-key` для New Relic); стандартные ключи `OTEL_EXPORTER_OTLP_*HEADERS` не действуют; метрики — cumulative. FR-10.6, NFR-1: значение `OpenTelemetry:Headers` — секрет, маскируется как токен. §7.2: ключ `OpenTelemetry:Headers` (по умолчанию `null`); проверка формата и требование `https` (или loopback `http`) для адреса при заданных заголовках; редактор не пишет ключ и сохраняет его. FR-12.5: то же правило адреса в редакторе |

## 4. Затрагиваемые проекты и типы

**GitHubBackup.Infrastructure**

- `Configuration/OpenTelemetryOptions` (изм.): свойство `string? Headers` (по умолчанию `null`) и константа `HeadersKey = SectionName + ":Headers"` по образцу `GitHubOptions.TokenKey`.
- `Configuration/OtlpHeaders` (новый, `internal static`): разбирает строку заголовков. `TryParse(string headers, out IReadOnlyList<KeyValuePair<string,string>> pairs)` по правилам §7.2. Метод используют валидатор и маскировщик.
- `Configuration/OpenTelemetryOptionsValidator` (изм.): статический `Validate(bool enabled, string? otlpEndpoint, string? headers)` проверяет формат и правило `https`. Сообщения содержат только ключи.
- `Configuration/JsonTelemetrySettingsStore` (изм.): `Validate` передаёт `options.Value.Headers` действующей конфигурации. Save по-прежнему пишет только `Enabled` и `OtlpEndpoint`.
- `Hosting/HostSecretMasker` (изм.): регистрирует значение `OpenTelemetryOptions.HeadersKey` целиком и каждое значение заголовка. Если строка не разбирается, регистрирует каждый непустой сегмент после первого `=`.
- `Telemetry/TelemetryServiceCollectionExtensions` (изм.): в существующем `Configure<IOptions<OpenTelemetryOptions>>` всегда присваивает `exporter.Headers = options.Value.Headers` (пустое значение даёт `null`).

**Тестовые проекты** — см. п. 6. **Документация:** `docs/PRD.md`, `README.md`. В `docs/development.md` упоминаний телеметрии нет, менять нечего.

## 5. Подход

Порядок работы:

```
PRD 0.41 (после одобрения) -> OtlpHeaders -> Options/Validator -> HostSecretMasker
  -> TelemetryServiceCollectionExtensions -> JsonTelemetrySettingsStore -> tests -> README
```

Ключевые решения:

1. **Один источник заголовков.** Exporter всегда получает `Headers` из нашего ключа. `null` тоже присваивается: он перекрывает то, что SDK прочитал из `OTEL_EXPORTER_OTLP_HEADERS` и ключей отдельных сигналов. Иначе секрет мог бы прийти мимо маскировщика. Delegate `Configure` выполняется после фабрики SDK — это уже доказывают тесты endpoint. Разработчик проверяет по исходникам 1.21: трейсы, метрики и логи вызываются без имени (`AddOtlpExporter()`), поэтому все три берут опции с именем `Options.DefaultName`. Если окажется, что сигналы используют именованные опции, нужен `ConfigureAll`.
2. **Валидация только при `Enabled=true`** — так же, как для endpoint. Пустая строка или пробелы считаются «не задано». Правило `https`/loopback повторяет `GitHubOptionsValidator`; проверку схемы лучше вынести в общий helper, а не копировать. Loopback `http` остаётся разрешён: для локального Aspire с `x-otlp-api-key` и для тестов (`http://127.0.0.1:1`).
3. **Маскировщик получает значение тем же путём, что токен.** Это `HostSecretMasker.Create(IConfiguration)`. Он покрывает оба хоста (`InfrastructureServiceCollectionExtensions`, `TryAddSingleton<ISecretMasker>`) и маскировщик ошибок старта CLI (`CliApplication.CreateMaskerWithoutFiles`: переменные `GitHubBackup__` и командная строка). Регистрируются вся строка и каждое значение. Если SDK 1.21 декодирует percent-escape в значениях, регистрируется и декодированная форма. Разработчик проверяет это по исходникам `OtlpExporterOptions`; лишняя регистрация вреда не наносит. Имена заголовков не регистрируются: это не секрет, и `***` вместо `api-key` испортил бы текст сообщений.
4. **Значение не появляется в сообщениях валидации.** Тексты:
   - `OpenTelemetry:Headers must be a comma-separated list of name=value pairs with a header name and a value in each pair.`
   - `OpenTelemetry:OtlpEndpoint must be an absolute https URI, or http for a loopback address, when OpenTelemetry:Headers is set.`
5. **Внутренняя диагностика SDK.** OTLP exporter 1.21 не логирует заголовки через `ILogger`. Свои ошибки он пишет в `EventSource` (`OpenTelemetry-Exporter-OpenTelemetryProtocol`). Эти события попадают в файл, только если рядом с exe или в рабочей папке лежит `OTEL_DIAGNOSTICS.json`. Текст событий — исключения gRPC/HTTP; заголовки запроса в них не входят, но маскировщик их не обрабатывает. `IHttpClientFactory` в решении не зарегистрирован, поэтому логирования HTTP-заголовков из `Microsoft.Extensions.Http` нет. В README добавить строку: не держите `OTEL_DIAGNOSTICS.json` при работе с реальным ключом. В коде ничего не меняется.
6. **Темпоральность** остаётся cumulative, без нового ключа. README объясняет это одним абзацем.

## 6. Тесты

**GitHubBackup.Infrastructure.Tests**

- `Configuration/OtlpHeadersTests` (новый): разбор одной и нескольких пар; пробелы; `=` внутри значения (Base64 `abc==`); ошибки — нет `=`, пустое имя, пустое значение, пустой сегмент, запятая в конце, недопустимый символ в имени, CR/LF в значении.
- `Configuration/OpenTelemetryOptionsValidatorTests` (доп.):
  - `Validate_HeadersMalformed_FailsNamingKeyWithoutValue` (theory; утверждение `ShouldNotContain(value)`);
  - `Validate_DisabledWithMalformedHeaders_Succeeds`;
  - `Validate_HeadersWithHttpRemoteEndpoint_FailsNamingKey`;
  - `Validate_HeadersWithHttpsOrLoopbackHttp_Succeeds` (theory: `https://otlp.nr-data.net:4317`, `http://localhost:4317`, `http://127.0.0.1:1`, `http://[::1]:4317`);
  - `Validate_NoHeadersWithHttpRemoteEndpoint_Succeeds` (граница: без заголовков правило `https` не действует).
- `Hosting/HostSecretMaskerTests` (доп.): `Create_HeadersConfigured_MasksWholeStringAndEachValue`; `Create_HeadersConfigured_KeepsHeaderNames`; `Create_MalformedHeaders_MasksSegmentValues`; `Create_EmptyOrWhiteSpaceHeaders_IsIgnored`; `Create_TokenAndHeaders_MasksBoth`.
- `Telemetry/TelemetryServiceCollectionExtensionsTests` (доп.): `AddGitHubBackupTelemetry_HeadersSet_ExporterReceivesHeaders`; `AddGitHubBackupTelemetry_HeadersNotSet_ExporterHasNoHeaders`; `AddGitHubBackupTelemetry_StandardOtelHeadersKey_IsIgnored` (в конфигурации `OTEL_EXPORTER_OTLP_HEADERS` и `OTEL_EXPORTER_OTLP_TRACES_HEADERS`, `Headers` не задан → у exporter `Headers` пустой).
- `Configuration/JsonTelemetrySettingsStoreTests` (доп.): `Validate_HeadersConfiguredWithHttpRemoteEndpoint_ReturnsErrorWithoutValue`; `SaveAsync_HeadersInFile_KeepsThemAndNeverWritesOptionsHeaders`. Второй тест: в options задан `Headers="api-key=secret"`, в файле его нет → после Save `Headers` в файле не появился. Существующий тест с `"Headers": "x"` проверяет, что значение в файле сохраняется.

**GitHubBackup.Infrastructure.IntegrationTests**

- `Logging/OtlpLogExportTests` (доп.): `Log_HeaderValueInMessagePropertyAndException_ExportsStars` — по образцу `Log_TokenInMessagePropertyAndException_ExportsStars`, с аргументом `--OpenTelemetry:Headers=api-key=<value>`.
- `Logging/StartupLoggingTests` или `Hosting/OpenTelemetryHostTests` (доп.): `BuildHost_HeadersWithRemoteHttpEndpoint_FailsValidationWithoutValue`. Проверка: исключение `OptionsValidationException`, его сообщение содержит ключ и не содержит значения.

**GitHubBackup.Cli.Tests**

- `OtlpHeadersEnvironmentVariableTests` (новый, в коллекции `ProcessEnvironmentTestGroup`, по образцу `TokenEnvironmentVariableTests`). Задаёт `GitHubBackup__OpenTelemetry__Headers=api-key=<value>`, восстанавливает переменную в `Dispose`. Кейсы:
  - `RunAsync_HeadersVariableTelemetryEnabled_NeverWritesValue`: `backup --OpenTelemetry:Enabled=true --OpenTelemetry:OtlpEndpoint=http://127.0.0.1:1` без `backup-config.json` → код `Critical`; stdout, stderr и все файлы в `LogsPath` не содержат значения;
  - `RunAsync_HeadersVariableWithRemoteHttpEndpoint_ReturnsCriticalNamingKeyWithoutValue`: endpoint `http://collector.example:4317` → код `2`, строка stderr с `OpenTelemetry:OtlpEndpoint`, без значения;
  - `RunAsync_MalformedHeadersVariable_ReturnsCriticalNamingKeyWithoutValue`;
  - `BuildHost_HeadersVariable_MaskerMasksValue`: `ISecretMasker` из хоста маскирует значение.
- Существующие тесты `ProcessRunner` на удаление переменных `GitHubBackup__` уже покрывают п. 10: правило работает по префиксу. Новый тест не нужен; разработчик подтверждает, что такой тест есть.

## 7. Проверка

| Пункт | Как проверить |
|---|---|
| 1 | Ревью diff `docs/PRD.md` (0.41, FR-10.5, FR-10.6, §7.2, FR-12.5, NFR-1, строка истории) |
| 2, 6, 8 | `OpenTelemetryOptionsValidatorTests`, `HostSecretMaskerTests`, `OtlpLogExportTests.Log_HeaderValue…`, `OtlpHeadersEnvironmentVariableTests` |
| 3 | Вручную, пользователь с аккаунтом New Relic по шагам README (ниже) |
| 4 | Сборка, тесты без Smoke, `dotnet format --verify-no-changes`; CI в PR зелёный |
| 5 | `Validate_HeadersWithHttpRemoteEndpoint_…`, `RunAsync_HeadersVariableWithRemoteHttpEndpoint_…` |
| 7 | `AddGitHubBackupTelemetry_StandardOtelHeadersKey_IsIgnored` |
| 9 | `JsonTelemetrySettingsStoreTests` (новые кейсы и существующий тест с `"Headers": "x"`) |
| 10 | Существующие тесты окружения `ProcessRunner` |

Ручная проверка п. 3 (пользователь):

1. Создать в New Relic ключ типа **INGEST - LICENSE**.
2. Задать `GitHubBackup__OpenTelemetry__Headers=api-key=<key>`, `OpenTelemetry:Enabled=true`, `OpenTelemetry:OtlpEndpoint=https://otlp.nr-data.net:4317` (для EU — `https://otlp.eu01.nr-data.net:4317`).
3. Запустить `GitHubBackup.Cli.exe backup`.
4. В New Relic открыть **APM & Services → Services - OpenTelemetry → GitHubBackup**. Проверить **Distributed tracing** (`backup.run`), **Logs** и метрики в **Query your data**: `FROM Metric SELECT sum(githubbackup.repositories) FACET githubbackup.status SINCE 1 hour ago`.
5. Проверить, что ключа нет в `logs\*`.

Результат пользователь отмечает в PR.

README (английский):

- новый подраздел `#### Send telemetry to New Relic` после раздела про Aspire: ключ, региональные адреса, переменная, где смотреть в UI, шаги проверки, абзац о темпоральности. Абзац: New Relic рекомендует delta, но принимает cumulative; для CLI, который отправляет метрики один раз в конце, разницы нет. Плюс предупреждение про `OTEL_DIAGNOSTICS.json`;
- исправить совет про `OTEL_EXPORTER_OTLP_HEADERS` в разделе Telemetry: учётные данные задаются в `OpenTelemetry:Headers`, предпочтительно через `GitHubBackup__OpenTelemetry__Headers`; стандартные `OTEL_*` не читаются;
- в пример `appsettings.json` добавить `"Headers": null`;
- в таблицу ключей добавить строку `OpenTelemetry:Headers` с форматом, правилом `https` и предупреждением про командную строку;
- в абзаце о проверке при старте упомянуть `OpenTelemetry:Headers` рядом с токеном;
- в абзаце «Who writes the file» указать, что редактор сохраняет `Headers`; то же в разделе Settings editor;
- в описании логов: заголовки OTLP тоже маскируются.

## 8. Вне рамок

- Отдельный ключ для темпоральности (delta) — решено не делать.
- Протокол OTLP/HTTP (порт 4318) — остаётся gRPC, New Relic его принимает.
- Поле `Headers` в редакторе UI и хранение ключа в Credential Manager. Если понадобится, нужен отдельный issue с изменением PRD.
- Маскирование событий `EventSource` SDK (`OTEL_DIAGNOSTICS.json`) — только предупреждение в README.
- Автоматический тест с реальным New Relic — п. 3 проверяется вручную.

## 9. Открытые вопросы

1. Правило `https` при заданном `Headers` действует при старте и в редакторе UI (FR-12.5). Это подтверждено?
2. Стандартные `OTEL_EXPORTER_OTLP_*HEADERS` явно перекрываются (не действуют даже через `GitHubBackup__`). Это подтверждено?
3. Неправильный `Headers` при `Enabled=false` не проверяется, но значение маскируется. Подходит?
4. Короткое значение (например, `1`) маскируется всюду и портит текст лога. Предлагается принять это как есть, без минимальной длины. Согласны?
5. Тексты PRD из п. 3 — одобрить или поправить до начала работы над кодом.

## 10. Решения

Приняты до планирования:

- Ключ передаётся общим ключом конфигурации `OpenTelemetry:Headers`, а не отдельным ключом New Relic и не через Credential Manager. Он подходит для любого приёмника OTLP.
- Темпоральность метрик остаётся cumulative, новой настройки нет; README описывает разницу.

Приняты на ревью плана (пользователь одобрил план и ответы на открытые вопросы, 2026-11-15):

- Правило `https` (или loopback `http`) при заданном `Headers` действует при старте и в редакторе UI.
- Стандартные `OTEL_EXPORTER_OTLP_*HEADERS` не действуют ни в каком виде, в том числе через `GitHubBackup__` и командную строку.
- При `Enabled=false` формат `Headers` не проверяется, но значение маскируется.
- Минимальной длины значения нет: короткое значение маскируется всюду.
- Тексты PRD 0.41 из п. 3 одобрены без правок.
- Базовый курс по New Relic — отдельная работа вне этого issue, не коммитится.

## 11. Задачи

- [x] Внести изменения PRD 0.41 (п. 3) после одобрения пользователем
- [x] `OtlpHeaders.TryParse` и `OtlpHeadersTests`
- [x] `OpenTelemetryOptions.Headers`, `HeadersKey`
- [x] `OpenTelemetryOptionsValidator`: формат и правило `https`/loopback (общий helper схемы с `GitHubOptionsValidator`); тесты
- [x] `HostSecretMasker`: регистрация всей строки, значений и (если SDK декодирует) декодированной формы; тесты
- [x] `TelemetryServiceCollectionExtensions`: всегда присваивать `exporter.Headers`; проверить по SDK 1.21 имена опций всех трёх сигналов и percent-decoding; тесты, включая игнорирование `OTEL_EXPORTER_OTLP_*HEADERS`
- [x] `JsonTelemetrySettingsStore.Validate`: учитывать действующий `Headers`; тесты
- [x] `OtlpLogExportTests`: маскирование значения заголовка в логах, которые уходят по OTLP
- [x] Тест хоста: ошибка валидации без значения
- [x] `GitHubBackup.Cli.Tests/OtlpHeadersEnvironmentVariableTests`
- [x] Убедиться, что тест `ProcessRunner` на удаление `GitHubBackup__*` существует
- [x] README: пример, таблица ключей, раздел Telemetry, раздел Settings editor, новый подраздел «Send telemetry to New Relic» с ручной проверкой и абзацем о темпоральности
- [x] Сборка, тесты без Smoke, `dotnet format --verify-no-changes`
- [x] Проверить новые комментарии в коде по правилам comment hygiene
- [ ] Ручная проверка в New Relic (пользователь), отметка в PR

Связанные файлы:

- `docs/PRD.md`
- `README.md`
- `src/GitHubBackup.Infrastructure/Configuration/OpenTelemetryOptions.cs`
- `src/GitHubBackup.Infrastructure/Configuration/OpenTelemetryOptionsValidator.cs`
- `src/GitHubBackup.Infrastructure/Configuration/GitHubOptionsValidator.cs`
- `src/GitHubBackup.Infrastructure/Configuration/JsonTelemetrySettingsStore.cs`
- `src/GitHubBackup.Infrastructure/Hosting/HostSecretMasker.cs`
- `src/GitHubBackup.Infrastructure/Hosting/GitHubBackupHostBuilder.cs`
- `src/GitHubBackup.Infrastructure/Telemetry/TelemetryServiceCollectionExtensions.cs`
- `src/GitHubBackup.Cli/CliApplication.cs`
- `src/GitHubBackup.Infrastructure.IntegrationTests/Logging/OtlpLogExportTests.cs`
- `src/GitHubBackup.Cli.Tests/TokenEnvironmentVariableTests.cs`
