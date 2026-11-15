# План: #100 «F3.14: Telemetry setup with .NET Aspire Dashboard»

| Поле | Значение |
|---|---|
| Issue | [#100](https://github.com/askrinnik/GitHubBackup/issues/100) |
| Направление | Feature |
| Сложность | M |
| Дата | 2026-11-15 |

## 1. Цель

Дать готовый способ поднять .NET Aspire Dashboard локально одной командой, проверить сквозной приём телеметрии GitHubBackup и дать включить телеметрию в редакторе настроек UI. Требования: FR-10.5 (OTLP-экспорт на заданный адрес, включается в `appsettings.json` или в UI), FR-12.5 (группа «Telemetry»), §7.1, §7.2 (`OpenTelemetry:Enabled` и `OpenTelemetry:OtlpEndpoint` в `user-settings.json`; адрес по умолчанию `http://localhost:4317`).

Код экспорта уже есть (#34): `TelemetryServiceCollectionExtensions.AddGitHubBackupTelemetry` регистрирует OTLP-экспортёр (gRPC) для трейсов и метрик. Протокол gRPC и порт 4317 совпадают с приёмом дашборда. Включение через UI — новое поведение; PRD обновлён до 0.34 до кода (решение пользователя на ревью).

## 2. Критерии приёмки

Пункты issue:

1. Aspire Dashboard запускается одной командой из репозитория.
2. Трейс `backup.run` с дочерними spans и метрики `githubbackup.*` видны в дашборде после запуска `backup`.
3. README описывает настройку и проверку с Aspire Dashboard по шагам.
4. CI зелёный.

Добавлено:

- (добавлено) Порты дашборда открыты только на `127.0.0.1`: телеметрия содержит имена репозиториев, в том числе приватных, и не должна приниматься или показываться из локальной сети.
- (добавлено, PRD 0.34) Редактор настроек показывает группу «Telemetry» с флажком «Send telemetry (OpenTelemetry)» и полем адреса OTLP; поля показывают действующие значения.
- (добавлено, PRD 0.34) «Save telemetry settings» пишет `OpenTelemetry:Enabled` и `OpenTelemetry:OtlpEndpoint` в `user-settings.json` атомарно; другие разделы файла остаются как были; статус говорит, что настройки действуют после перезапуска.
- (добавлено, PRD 0.34) При включённом флажке и адресе, который не является абсолютным URI, редактор показывает ошибку у поля адреса и файл не пишет. Выключенный флажок принимает любой адрес.
- (добавлено, PRD 0.34) Оба хоста читают эти два ключа из `user-settings.json` (после `appsettings.json`, до переменных окружения и командной строки); другие ключи раздела `OpenTelemetry` и прочие ручные ключи игнорируются.
- (добавлено, PRD 0.34) Запуск из UI с телеметрией, включённой в редакторе, после перезапуска даёт трейс `backup.run` в дашборде.

## 3. Затрагиваемые файлы

| Файл | Изменение |
|---|---|
| `compose.yaml` (корень, новый) | Сервис `aspire-dashboard`: образ `mcr.microsoft.com/dotnet/aspire-dashboard:13`, порты `127.0.0.1:18888:18888` (UI) и `127.0.0.1:4317:18889` (OTLP gRPC). |
| `docs/PRD.md` | 0.34: FR-10.5, FR-12.5, §7.1, §7.2, история. |
| `src/GitHubBackup.Core/Telemetry/TelemetrySettings.cs` (новый) | `sealed record TelemetrySettings(bool Enabled, string? OtlpEndpoint)` с константами `EnabledKey = "OpenTelemetry:Enabled"`, `OtlpEndpointKey = "OpenTelemetry:OtlpEndpoint"`. |
| `src/GitHubBackup.Core/Telemetry/ITelemetrySettingsStore.cs` (новый) | По образцу `IToolPathsStore`: `Current`, `Validate`, `SaveAsync`. |
| `src/GitHubBackup.Infrastructure/Configuration/JsonTelemetrySettingsStore.cs` (новый) | По образцу `JsonToolPathsStore`: `Current` из `IOptions<OpenTelemetryOptions>`, проверка правилом `OpenTelemetryOptionsValidator` (вынести статический метод проверки, как `ToolsOptionsValidator.Validate`), запись раздела `OpenTelemetry` через `UserSettingsFile.WriteSectionAsync` (пустой адрес → `null`). |
| `src/GitHubBackup.Infrastructure/Configuration/OpenTelemetryOptionsValidator.cs` | Общий статический метод проверки для хоста и хранилища. |
| `src/GitHubBackup.Infrastructure/InfrastructureServiceCollectionExtensions.cs` | Регистрация `ITelemetrySettingsStore` (singleton). |
| `src/GitHubBackup.Infrastructure/Hosting/GitHubBackupHostBuilder.cs` | `ReadUserSettings` пропускает ещё `OpenTelemetry:Enabled` и `OpenTelemetry:OtlpEndpoint`; документация `UserSettingsFileName` и метода. |
| `src/GitHubBackup.App/ViewModels/SettingsViewModel.cs` | Свойства `TelemetryEnabled`, `OtlpEndpoint`, `TelemetryStatus`, команда `SaveTelemetryCommand`, ошибка у `OtlpEndpoint` (как у путей к инструментам), сообщение `TelemetrySavedMessage`. |
| `src/GitHubBackup.App/Views/SettingsView.xaml` | Группа «Telemetry» между «Tools» и «GitHub token»: подсказка о перезапуске, `CheckBox` (`TelemetryEnabledCheckBox`), поле адреса (`OtlpEndpointBox`, `FormTextBoxStyle`), кнопка `SaveTelemetryButton`, статус `TelemetryStatus`; группа `SettingsTelemetryGroup`; стили — существующие, с `BasedOn` Fluent. |
| `README.md` | Раздел телеметрии: оба приложения, включение в редакторе UI, метрики UI приходят периодически и при закрытии; таблица ключей `user-settings.json`, если она есть. |
| `CLAUDE.md` | Строка `compose.yaml` в структуре репозитория. |

## 4. Подход

1. `compose.yaml`, сквозная проверка CLI, README (сделано).
2. PRD 0.34 (сделано).
3. Core: `TelemetrySettings`, `ITelemetrySettingsStore`.
4. Infrastructure: общий метод проверки, `JsonTelemetrySettingsStore`, регистрация, список разрешённых ключей в `GitHubBackupHostBuilder.ReadUserSettings`.
5. App: ViewModel и XAML по образцу группы «Tools».
6. Тесты (ниже), README.
7. Проверка в запущенном UI (netwright) с временной папкой и сквозная проверка UI → дашборд.

## 5. Тесты

| Слой | Тесты |
|---|---|
| `Infrastructure.Tests` | `JsonTelemetrySettingsStoreTests`: `Current` из опций; `Validate` — включено + пустой/относительный адрес → ошибка с ключом без значения; выключено + любой адрес → без ошибок; `SaveAsync` пишет раздел, сохраняет разделы `Tools` и `Ui`, пустой адрес → `null`, при ошибке проверки бросает `ArgumentException` и не пишет. `OpenTelemetryOptionsValidatorTests` — общий метод. `InfrastructureServiceCollectionExtensionsTests` — регистрация. |
| `Infrastructure.IntegrationTests` | `UserSettingsHostTests`: `OpenTelemetry:Enabled`/`OtlpEndpoint` из `user-settings.json` попадают в конфигурацию и перекрывают `appsettings.json`; другой ключ раздела (`OpenTelemetry:Other`) не попадает. `OpenTelemetryHostTests`: `Enabled=true` только в `user-settings.json` регистрирует экспорт. |
| `App.Tests` | `SettingsViewModelTests`: начальные значения из `Current`; сохранение валидных значений вызывает `SaveAsync` и ставит статус; ошибка проверки — ошибка у `OtlpEndpoint`, `SaveAsync` не вызывается; `IOException` — диалог ошибки. |
| `App.UITests` | `SettingsEditorTests`: группа «Telemetry» есть; сохранение пишет `user-settings.json` с разделом `OpenTelemetry`. |

## 6. Проверка

| Пункт | Как проверяется |
|---|---|
| 1 | `docker compose up -d` из корня; контейнер `running`. |
| 2 | Запуск CLI `backup --silent --OpenTelemetry:Enabled=true` во временной папке: в дашборде трейс `backup.run` (13 spans) и 4 метрики `githubbackup.*`. |
| 3 | README, шаги совпадают с проверкой. |
| 4 | CI пулл-реквеста. |
| Порты `127.0.0.1` | `docker compose config`. |
| Группа «Telemetry», сохранение, ошибка | Тесты ViewModel и UI; проверка в запущенном UI (netwright, временная папка). |
| Хосты читают ключи | Интеграционные тесты хоста. |
| UI → дашборд | Включить в редакторе, перезапустить UI во временной папке, запустить бэкап, увидеть `backup.run` в дашборде. |

## 7. Вне рамок

- Seq и другие приёмники OTLP.
- Протокол `http/protobuf` и настройка протокола.
- Экспорт логов по OTLP.
- Проверка `compose.yaml` в CI.
- Включение телеметрии без перезапуска.

## 8. Решения

1. Файл — `compose.yaml` в корне репозитория; запуск `docker compose up -d`.
2. Образ закреплён на мажорной версии (`:13`), не `latest`.
3. Сквозную проверку выполняет агент на машине пользователя; пользователь запускает Docker Desktop.
4. Сборку и тесты для правок только документации и `compose.yaml` пользователь разрешил пропустить; после изменения кода шаг 9 выполняется полностью.
5. Включение телеметрии через UI входит в #100 (решение пользователя). PRD 0.34 утверждён: отдельная группа «Telemetry», флажок и адрес, действуют после перезапуска, приоритет источников не меняется, поля показывают действующие значения.

6. Отступления от плана при реализации: `UserSettingsFile` знает раздел `OpenTelemetry` (иначе запись отбрасывает раздел); ключи `TelemetrySettings` — `static readonly`, а не `const`, потому что архитектурный тест считает строковую константу с префиксом `OpenTelemetry` зависимостью Core от SDK.

## 9. Задачи

- [x] `compose.yaml`
- [x] Сквозная проверка CLI с дашбордом
- [x] README (и строка `compose.yaml` в разделе структуры `CLAUDE.md`)
- [x] PRD 0.34
- [x] Core и Infrastructure: хранилище, проверка, ключи хоста
- [x] App: ViewModel и группа «Telemetry»
- [x] Тесты
- [x] README: телеметрия в UI
- [x] Сборка, тесты, формат, security review
- [x] Проверка в UI и сквозная проверка UI → дашборд
