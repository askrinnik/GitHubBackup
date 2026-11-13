# План: F3.9 — трейсы и метрики OpenTelemetry (необязательно)

| | |
|---|---|
| Issue | [#34](https://github.com/askrinnik/GitHubBackup/issues/34) |
| Направление | Feature |
| Сложность | L |
| Дата | 2026-11-13 |
| Требования | [§6.10](../PRD.md#610-логирование) FR-10.5, §7.2 (`OpenTelemetry`) |

## 1. Цель

Реализовать FR-10.5:

- **Трейсы:** запуск → обнаружение → репозиторий → операции.
- **Метрики:** число репозиториев по статусам, длительности, размеры архивов.
- **Экспорт:** OTLP на адрес `OpenTelemetry:OtlpEndpoint` (§7.2).

По умолчанию телеметрия выключена (`OpenTelemetry:Enabled=false`). Тогда SDK OpenTelemetry и экспортер не регистрируются. Инструментация (`ActivitySource` и `Meter` из BCL) без слушателя почти ничего не стоит.

## 2. Критерии приёмки

1. При `Enabled=false` экспортер не регистрируется (issue).
2. Ручная проверка через .NET Aspire Dashboard описана в README (issue).
3. Поведение покрыто тестами, CI зелёный (issue).
4. *(добавлено, FR-10.5)* Трейсы образуют иерархию запуск → обнаружение → репозиторий → операции (`clone`, `fetch`, `ls-remote`, `archive`).
5. *(добавлено, FR-10.5)* Метрики: число репозиториев по статусам, длительности, размеры архивов.
6. *(добавлено, FR-10.5)* При `Enabled=true` идёт экспорт по OTLP на `OpenTelemetry:OtlpEndpoint`.
7. *(добавлено)* Spans и метрики есть во всех ветках:
   - успех (`Archived`, `UpToDate`, `Warning`);
   - `Failed`;
   - `Skipped`: неактивный репозиторий, репозиторий, отсутствующий при dry run, новый неподтверждённый, не начатый из-за отмены;
   - `Cancelled`;
   - dry run (`WouldClone`, `WouldArchive`);
   - остановленный запуск (ошибка конфигурации, занятая блокировка, нет инструмента, сбой обнаружения);
   - запуск с `--repo` (обнаружение пропущено).
8. *(добавлено, NFR-1, FR-10.6)* Spans не содержат токена и текста исключений. Ошибка — это `ActivityStatusCode.Error` и тег `error.type` (имя типа исключения).
9. *(добавлено)* Недопустимое `OpenTelemetry:Enabled` (например, `perhaps`) по-прежнему даёт ошибку конфигурации с кодом `2` и прежним текстом.
10. *(добавлено)* Регистрация SDK находится в общем `GitHubBackupHostBuilder`, который используют обе программы.

## 3. Затронутые проекты и типы

**`src/Directory.Packages.props`.** Новая группа `Telemetry`: `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.OpenTelemetryProtocol` (последний стабильный 1.x), `Microsoft.Extensions.Diagnostics` (версия остальных `Microsoft.Extensions.*`).

**GitHubBackup.Core.**

- `Telemetry/BackupTelemetry` (новый, singleton, `IDisposable`). Владеет `ActivitySource` и `Meter` с именем `GitHubBackup` (константа `SourceName`). `Meter` создаётся через `IMeterFactory`. Методы: `StartRun`, `StartDiscovery`, `StartRepository`, `StartOperation` (возвращают `Activity?`), `RecordItem`, `RecordRun`, `RecordArchiveSize`.
- `Runs/BackupOrchestrator`: spans `backup.run`, `backup.discovery`, `backup.repository` (и для вики), операция `archive`; метрики по итоговому списку результатов.
- `Changes/ChangeDetector`: операции `clone`, `fetch`, `ls-remote`.
- `CoreServiceCollectionExtensions`: `AddMetrics()`, `TryAddSingleton<BackupTelemetry>()`.

**GitHubBackup.Infrastructure.**

- `Telemetry/TelemetryServiceCollectionExtensions.AddGitHubBackupTelemetry(IConfiguration)`. Если `Enabled` не равно `true`, ничего не регистрирует. Иначе `AddOpenTelemetry()` с ресурсом `service.name`, `WithTracing` и `WithMetrics` с OTLP-экспортером.
- `Hosting/GitHubBackupHostBuilder`: вызов `AddGitHubBackupTelemetry`.

**GitHubBackup.ArchitectureTests.** `LayerRules`: `OpenTelemetry` запрещён для Core.

**README.md.** Раздел «Телеметрия (OpenTelemetry)».

## 4. Подход

1. **Инструментация живёт в Core.** Иерархия run → discovery → repository → archive создаётся в `BackupOrchestrator`, операции clone, fetch и ls-remote — в `ChangeDetector`. `ActivitySource`, `Meter` и `IMeterFactory` — это BCL и `Microsoft.Extensions.Diagnostics`, не I/O-библиотеки. Spans на уровне `ProcessRunner` отклонены: они дали бы команды git, а не доменные операции.
2. **DI-класс вместо статики.** `BackupTelemetry` — singleton, `Meter` из `IMeterFactory`. Тесты изолированы: `MetricCollector<T>` привязан к экземпляру инструмента.
3. **Регистрация SDK — в `GitHubBackupHostBuilder.Create`.** Сейчас его вызывает только Cli; WPF App получит телеметрию, когда перейдёт на этот builder.
4. **Проверка «выключено» при регистрации.** `bool.TryParse(configuration["OpenTelemetry:Enabled"])`: не `true` или не разбирается — ничего не регистрируется. Ошибку недопустимого значения по-прежнему сообщает проверка опций при старте. Endpoint задаётся отложенно через `IOptions<OpenTelemetryOptions>`. Протокол — gRPC по умолчанию (порт 4317 в PRD).
5. **Spans.** `backup.run` — корень (теги `githubbackup.dry_run`, `githubbackup.run.outcome`). Дочерние: `backup.discovery` (кроме `--repo`) и `backup.repository` на каждый репозиторий и вики (теги `githubbackup.repository`, `githubbackup.target`, `githubbackup.status`). Внутри `backup.repository` — операции с тегом `githubbackup.operation`.
   - `Failed` и остановленный запуск с исключением: `Error` и `error.type`, без описания.
   - `Cancelled`: тег status, исключение пробрасывается дальше.
   - Неактивный репозиторий: span со status `Skipped`.
   - Не начатые из-за отмены, отсутствующие при dry run, новые неподтверждённые: без span, только в метрике.
6. **Метрики.**
   - `githubbackup.repositories` — Counter, `{repository}`, теги status, target, dry_run; для каждого элемента итогового списка.
   - `githubbackup.repository.duration` — Histogram, `s`.
   - `githubbackup.run.duration` — Histogram, `s`, тег outcome; время из `TimeProvider`.
   - `githubbackup.archive.size` — Histogram, `By`; только при реальном архиве.
7. **README.** Запуск Aspire Dashboard в Docker, запуск `backup` с `--OpenTelemetry:Enabled=true`, что смотреть в Traces и Metrics.

## 5. Тесты

- **Core.Tests:** `BackupTelemetryTests` (новый); новые случаи в `BackupOrchestratorTests` (иерархия, Failed, Cancelled, Skipped, dry run, `--repo`, остановленный запуск, счётчик совпадает с результатами, вики) и `ChangeDetectorTests` (clone, ls-remote, fetch, preview); `CoreServiceCollectionExtensionsTests`. `ActivityListener` фильтруется по `SourceName` и `TraceId` родительской activity теста, потому что тесты идут параллельно.
- **Infrastructure.Tests:** `TelemetryServiceCollectionExtensionsTests` (новый): выключено, ключ не задан или `perhaps` — `TracerProvider` и `MeterProvider` не разрешаются; включено — разрешаются; endpoint экспортера равен `OtlpEndpoint`.
- **Существующие тесты:** создание `BackupOrchestrator` и `ChangeDetector` получает `BackupTelemetry`.
- **ArchitectureTests:** правила Core с `OpenTelemetry`.
- **Cli.Tests:** тест `perhaps` остаётся зелёным.

## 6. Вне рамок

- Spans HTTP-запросов Octokit и проверки вики.
- Spans отдельных процессов в `ProcessRunner`.
- Экспорт логов по OTLP.
- Spans записи истории и state-файла.
- Подключение WPF App к `GitHubBackupHostBuilder`.
- Выбор протокола OTLP через настройку (это изменение PRD).

## 7. Решения

- Spans `backup.discovery` и `backup.repository` — дочерние для `backup.run` и идут друг за другом; операции — дочерние для `backup.repository`. Обнаружение заканчивается до обработки, поэтому repository не вложен в discovery.
- Core получает пакет `Microsoft.Extensions.Diagnostics` ради `IMeterFactory` и `AddMetrics`; это не I/O-библиотека.
- Пользователь разрешил переходить к реализации без отдельного ревью плана, если по нему нет вопросов.

## 8. Задачи

- [x] `Directory.Packages.props`: группа `Telemetry`; проверка уязвимостей
- [x] Core: `BackupTelemetry` и регистрация в `AddGitHubBackupCore`
- [x] Core: операции в `ChangeDetector`
- [x] Core: spans и метрики в `BackupOrchestrator`
- [x] Infrastructure: `AddGitHubBackupTelemetry` и вызов в `GitHubBackupHostBuilder`
- [x] ArchitectureTests: `OpenTelemetry` запрещён для Core
- [x] Тесты: новые и обновлённые
- [x] README: раздел «Телеметрия (OpenTelemetry)»
- [x] Сборка, тесты, `dotnet format --verify-no-changes`
