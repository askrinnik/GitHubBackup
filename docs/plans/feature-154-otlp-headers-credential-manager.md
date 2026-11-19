# План: #154 F3.20 — хранить заголовки OTLP в Windows Credential Manager

| Поле | Значение |
|---|---|
| Issue | [#154](https://github.com/askrinnik/GitHubBackup/issues/154) |
| Название | F3.20: Store OTLP headers in Windows Credential Manager |
| Lane | Feature |
| Сложность | L |
| Дата | 2026-11-19 |

## 1. Цель

Значение `OpenTelemetry:Headers` (ключ приёмника, секрет) хранится в Windows Credential Manager в Generic Credential с target `GitHubBackup:OpenTelemetry:Headers`. В `appsettings.json` открытым текстом оно больше не хранится.

Credential Manager — источник конфигурации сразу после `appsettings.json`. Он отдаёт только ключ `OpenTelemetry:Headers`. Значение задают Save в редакторе настроек (FR-12.5) и новая команда CLI `set-otlp-headers` (FR-9.1). Маскирование FR-10.6 и NFR-1 действует и для нового источника. Требования: FR-9.1, FR-10.5, FR-10.6, FR-12.5, NFR-1, §7.2.

## 2. Критерии приёмки

Из issue (дословно):

- [ ] PRD (FR-9.1, FR-10.5, FR-10.6, FR-12.5, §7.2, NFR-1) содержит текст из раздела «Изменение PRD»; версия PRD повышена, строка в истории изменений добавлена
- [ ] Оба хоста читают `OpenTelemetry:Headers` из Credential Manager; приоритет источников — как в §7.2 (файл < Credential Manager < user-secrets < переменная < командная строка)
- [ ] Значение из Credential Manager проверяется при старте по правилам §7.2 и маскируется в логах, stderr и OTLP
- [ ] Недоступный Credential Manager не останавливает запуск; в логе предупреждение без значения
- [ ] Редактор показывает значение из Credential Manager; Save пишет его в Credential Manager и удаляет ключ из `appsettings.json`; пустое поле удаляет запись
- [ ] Значение из `appsettings.json` без записи в Credential Manager показано с сообщением о переносе; Save переносит его; редактор сообщает, что старое значение осталось в `.bak`
- [ ] Сообщение о перекрытии ключа источником с более высоким приоритетом работает с новым порядком источников
- [ ] Команда `set-otlp-headers`: скрытый ввод, проверка формата, пустой ввод удаляет запись, предупреждение при ключе в файле
- [ ] Значение не попадает в аргументы процессов, логи и имена, описания и подсказки элементов UI
- [ ] README описывает Credential Manager как место хранения заголовков, команду `set-otlp-headers` и порядок источников
- [ ] Покрыто тестами (источник конфигурации, ViewModel, CLI, интеграционный тест Credential Manager), CI зелёный

Добавлено по коду:

- Ошибка записи или удаления в Credential Manager: редактор показывает сообщение без значения и остаётся открытым, файл не меняется; CLI возвращает `2`.
- Слишком длинное значение (сверх лимита Credential Manager) — понятная ошибка без значения.
- Перенаправленный ввод в `set-otlp-headers` → код `2` и подсказка, как у `set-token`; отмена (Ctrl+C) → учётные данные не меняются.
- Если ключ есть и в Credential Manager, и в файле, редактор показывает значение Credential Manager, показывает сообщение о файле, и Save удаляет ключ из файла.

## 3. Изменения требований

Текст одобрен и лежит в issue (`## Изменение PRD`). Разработчик копирует его в `docs/PRD.md` дословно: FR-9.1, FR-10.5, FR-10.6, FR-12.5 (пункты «Телеметрия» и «Сохранение»), §7.2 («Кто пишет файл», «Источники конфигурации»), NFR-1. Версия 0.47 → 0.48, дата 2026-11-19, строка в «Истории изменений» из issue.

## 4. Затронутые проекты и типы

**GitHubBackup.Core**
- `Telemetry/IOtlpHeadersStore` (новый): `Location`, `ReadAsync`, `WriteAsync`, `DeleteAsync` — аналог `ITokenStore`.
- `Telemetry/OtlpHeadersStoreException` (новый): ошибка хранилища, текст без значения.
- `Telemetry/ITelemetrySettingsStore`: `Current.Headers` — значение Credential Manager, иначе файла; `HeadersOverridden` — ключ задаёт источник выше Credential Manager; новое `bool HeadersInAppSettings`; `SaveAsync` документирует `OtlpHeadersStoreException`.

**GitHubBackup.Infrastructure**
- `Authentication/CredentialManagerSecret` (новый, internal static): общие `Read`, `Write`, `Delete`; переводят `Win32Exception` и `ArgumentException` (слишком длинное значение) в сообщение без значения. `CredentialManagerTokenStore` переходит на него без изменения поведения.
- `Configuration/CredentialManagerOtlpHeadersStore` (новый): `IOtlpHeadersStore` над target; `DefaultTarget = "GitHubBackup:OpenTelemetry:Headers"`.
- `Configuration/CredentialManagerConfigurationSource` и `CredentialManagerConfigurationProvider` (новые): `Load()` читает target; непустое значение → `Data["OpenTelemetry:Headers"]`; нет записи или пустое значение → данных нет; ошибка чтения не выбрасывается, текст без значения сохраняется как предупреждение.
- `Hosting/ConfigurationWarnings` (singleton) и `Hosting/ConfigurationWarningsLogger : IHostedService` (новые): на старте хоста пишут предупреждения провайдера через `[LoggerMessage]` Warning. Провайдер работает до появления логирования, поэтому лог пишет hosted service.
- `Hosting/GitHubBackupHostBuilder.Create`: после `AddJsonFile` добавляет источник Credential Manager, если `settings.OtlpHeadersCredentialTarget` не `null`; регистрирует `ConfigurationWarnings` и logger; `<remarks>` о порядке источников.
- `Hosting/GitHubBackupHostSettings`: `string? OtlpHeadersCredentialTarget`; `ForCurrentProcess()` ставит `DefaultTarget` или значение переменной изоляции (решение 1).
- `Configuration/ConfigurationLayers.FindHeadersLayers` → (`FileValue`, `CredentialValue`, `Overridden`).
- `Configuration/AppSettingValue.Remove(key)`; `AppSettingsFile` пропускает такой ключ при записи.
- `Configuration/OpenTelemetryOptionsValidator.ValidateHeadersFormat(string)` с тем же сообщением.
- `Configuration/JsonTelemetrySettingsStore`: получает `IOtlpHeadersStore`; `SaveAsync` регистрирует значение в masker, проверяет, пишет или удаляет запись Credential Manager, затем пишет в файл `Enabled`, `OtlpEndpoint` и удаляет `Headers`. Masker регистрирует и значение Credential Manager, и значение файла.
- `InfrastructureServiceCollectionExtensions`: регистрирует `IOtlpHeadersStore` с target из настроек хоста.

**GitHubBackup.Cli**
- `CliCommandLine`: `set-otlp-headers`; `DescribeUnmatchedTokens` скрывает аргументы обеих секретных команд.
- `CliCommand.SetOtlpHeaders`, `SetOtlpHeadersCommandHandler` (новый), `CliApplication.RunCommandAsync`, DI.

**GitHubBackup.App**
- `SettingsViewModel`: `HeadersOverriddenMessage` про Credential Manager; новые `HeadersInAppSettingsMessage`, `IsTelemetryHeadersInAppSettings`; группа телеметрии изменена, если ключ в файле; `OtlpHeadersStoreException` → диалог ошибки, редактор открыт.
- `Views/SettingsView.xaml`: подпись поля без «prefer the variable»; сообщение о файле с `AutomationId="TelemetryHeadersInAppSettingsNote"`.

**GitHubBackup.TestUtilities**: уникальный target на тест и очистка; изоляция дочерних процессов (решение 1).

**Документация**: `docs/PRD.md`, `README.md` (Telemetry, Settings editor, CLI commands, New Relic), `docs/development.md` (переменная изоляции для тестов).

## 5. Подход

1. PRD. 2. Core. 3. Infrastructure: `CredentialManagerSecret`, store, source/provider, warnings logger, layers, `AppSettingValue.Remove`, `JsonTelemetrySettingsStore`, host settings и builder. 4. CLI. 5. App. 6. README и `docs/development.md`.

Ключевые решения:
1. Провайдер не бросает исключений: ошибка чтения → предупреждение (target и сообщение Win32, без значения).
2. Пустое или пробельное значение в Credential Manager — «не задано».
3. Save: сначала Credential Manager, потом файл. Сбой Credential Manager — файл не тронут; сбой файла — секрет уже в Credential Manager, повторный Save повторит запись.
4. `OtlpHeadersCredentialTarget` в настройках хоста. Тесты в процессе задают уникальный target или `null` (источника нет); реальная запись разработчика тестами не читается и не пишется.
5. CLI проверяет формат всегда, независимо от `Enabled`; правило `https` проверяется при старте.
6. `CliApplication.CreateMaskerWithoutFiles` значение Credential Manager не читает — остаётся как есть.

## 6. Тесты

**Infrastructure.Tests**
- `CredentialManagerConfigurationProviderTests` (подставной reader): запись есть → только ключ Headers; записи нет; пустое значение; ошибка чтения → нет данных, предупреждение без значения.
- `ConfigurationWarningsLoggerTests` (`FakeLogger`): предупреждение → Warning с EventId; нет предупреждений → нет записей.
- `ConfigurationLayersTests`: файл, Credential Manager, оба; `Overridden` для env, командной строки, user-secrets; Credential Manager над файлом не перекрытие.
- `AppSettingsFileTests`: удаление ключа, другие ключи сохраняются; удаление из второго вхождения раздела; удаление отсутствующего ключа.
- `JsonTelemetrySettingsStoreTests`: значение Credential Manager; только файл; `HeadersInAppSettings`; Save пишет store и удаляет ключ из файла; пустое поле удаляет запись и ключ; сбой store → файл не изменён; неверное значение → ничего не пишется; masker регистрирует значение Credential Manager; при перекрытии Save всё равно пишет поле в store.
- Тесты источников host builder: Credential Manager перекрывает файл, env перекрывает Credential Manager, target `null` → источника нет.
- `InfrastructureServiceCollectionExtensionsTests`: `IOtlpHeadersStore` с target из настроек; hosted logger зарегистрирован.
- `OpenTelemetryOptionsValidatorTests.ValidateHeadersFormat`: корректная строка, пустой сегмент, нет `=`, управляющий символ; в сообщении нет значения.

**Infrastructure.IntegrationTests** (реальный Credential Manager, уникальный target, как `CredentialManagerTokenStoreTests`)
- `CredentialManagerOtlpHeadersStoreTests`: Write → чтение через провайдер; Delete; Delete без записи; слишком длинное значение → исключение без значения; `Location`.
- `AppSettingsHostTests`: значение Credential Manager действует и маскируется; миграция — после Save ключа нет в файле, есть в `.bak`.

**Cli.Tests**
- `CliCommandLineTests`: `set-otlp-headers` распознаётся; аргумент → ошибка без цитирования.
- `SetOtlpHeadersCommandHandlerTests`: ввод сохраняется → 0; пустой ввод → Delete, 0; неверный формат → stderr с ключом, без значения, 2; перенаправленный ввод → 2; сбой store → 2 без значения; ключ в файле → предупреждение; значение зарегистрировано в masker.
- `CliApplicationTests`: маршрутизация.

**App.Tests** — `SettingsViewModelTests`: ключ в файле → сообщение и группа изменена; ключа нет → не изменена; сбой store → диалог без значения, группа изменена; тексты сообщений.

**App.UITests** — `SettingsEditorTests`: Save заголовков → значение в Credential Manager (уникальный target процесса), ключа в файле нет; новый `Open_HeadersInAppSettings_ShowsMigrationNote`.

**ArchitectureTests**: новых правил нет; существующие проходят.

## 7. Проверка

- Сборка, тесты без smoke, `dotnet format`; UI-тесты с `-p:RunUITests=true`.
- Каждый критерий — тестом из §6; README и PRD — ревью диффа.

## 8. Вне рамок

- Отчёт `validate` об источнике заголовков.
- Удаление значения из `appsettings.json.bak` (по решению оно остаётся, редактор сообщает).
- Уроки `docs/new-relic-course/` — отдельная задача, если нужна.
- Регистрация значения Credential Manager в masker при ошибке сборки хоста (`CreateMaskerWithoutFiles`).

## 9. Решения

Пользователь разрешил идти без ревью плана; открытые вопросы планировщика решены так:

1. **Изоляция дочерних процессов** (UI-тесты, end-to-end тесты CLI, TestBench). `ForCurrentProcess()` читает target из переменной окружения `GITHUBBACKUP_OTLP_HEADERS_CREDENTIAL_TARGET` (вне префикса `GitHubBackup__`, поэтому не попадает в конфигурацию); если переменная не задана — `DefaultTarget`. `IsolatedAppProcess` и запуск CLI в тестах задают уникальный target и удаляют запись в `Dispose`. Переменная описывается в `docs/development.md` как средство тестов; в PRD и README не попадает.
2. **Значение и в Credential Manager, и в файле.** Редактор показывает значение Credential Manager, сообщение о файле, группа считается изменённой, Save удаляет ключ из файла.
3. **Уроки New Relic** в этой задаче не меняются.
4. **Cancel при ключе в файле** спрашивает о несохранённых изменениях — ожидаемо.

## 10. Задачи

- [x] PRD: перенести одобренный текст, версия 0.48, строка в истории
- [x] Core: `IOtlpHeadersStore`, `OtlpHeadersStoreException`, `ITelemetrySettingsStore.HeadersInAppSettings`
- [x] Infrastructure: `CredentialManagerSecret`, перевести на него `CredentialManagerTokenStore`
- [x] Infrastructure: `CredentialManagerOtlpHeadersStore`, `OpenTelemetryOptionsValidator.ValidateHeadersFormat`
- [x] Infrastructure: источник и провайдер конфигурации Credential Manager с предупреждением
- [x] Infrastructure: `ConfigurationWarnings`, `ConfigurationWarningsLogger`
- [x] Infrastructure: `GitHubBackupHostSettings.OtlpHeadersCredentialTarget`, источник в `GitHubBackupHostBuilder.Create`, DI
- [x] Infrastructure: `ConfigurationLayers.FindHeadersLayers`, `AppSettingValue.Remove`, `AppSettingsFile`
- [x] Infrastructure: `JsonTelemetrySettingsStore` (Credential Manager → файл, masker)
- [x] TestUtilities: уникальный target и очистка, переменная изоляции для дочерних процессов
- [x] CLI: `set-otlp-headers`
- [x] App: `SettingsViewModel`, `SettingsView.xaml`
- [x] Тесты по §6
- [x] README, `docs/development.md`
- [x] Сборка, тесты, `dotnet format`, UI-тесты
