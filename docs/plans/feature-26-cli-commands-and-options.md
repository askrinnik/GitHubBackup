# План: #26 — F3.1: CLI commands and options (System.CommandLine)

| | |
|---|---|
| Issue | [#26](https://github.com/askrinnik/GitHubBackup/issues/26) |
| Название | F3.1: CLI commands and options (System.CommandLine) |
| Линия | Feature |
| Сложность | M |
| Дата | 2026-11-11 |

## 1. Цель

У CLI появляются все команды FR-9.1 (`backup`, `validate`, `set-token`) и все параметры `backup` из FR-9.2 (`--account`, `--repo`, `--dry-run`, `--silent`, `--config`). Ключи вида `--Section:Key=value` по-прежнему уходят в конфигурацию .NET. Любой неверный аргумент даёт одну строку `Command-line error: …` и код `2` (FR-9.10). Разбор `--config` переезжает из `GitHubBackupHostBuilder.FindConfigPath` в `Cli`. Путь к файлу и ключи конфигурации попадают в хост через `GitHubBackupHostSettings`, поэтому Infrastructure больше не видит сырой `args`. `validate` проверяет `backup-config.json`, инструменты и токен и ничего не меняет.

Что уже есть в коде: `System.CommandLine` подключён к Cli. `CliCommandLine.Parse` знает команду `backup`, рекурсивный `--config`, `--help`/`--version`, пропускает ключи конфигурации и не показывает длинные токены в ошибках. В Core есть `BackupRunFilter.ForAccount/ForRepository`, оркестратор их учитывает. Есть `GitHubUrl.TryParseAccount/TryParseRepository/IsValidLogin/IsValidRepositoryName`, `IToolLocator`, `ITokenProvider`, `IBackupConfigStore`. Хранилища в Credential Manager нет. Флага dry-run в оркестраторе нет (#29).

## 2. Критерии приёмки

Из issue, дословно:
- [ ] Тесты разбора аргументов
- [ ] `--account` вместе с `--repo` даёт ошибку
- [ ] Покрыто тестами, CI зелёный

Добавлено по сверке с кодом и PRD:
- (добавлено) `--help` перечисляет `backup`, `validate`, `set-token`; `backup --help` перечисляет все пять параметров.
- (добавлено) `--account <url|login>` и `--repo <url|owner/name>` превращаются в `BackupRunFilter`. Неверное значение даёт ошибку командной строки и код `2`; само значение в сообщение не попадает.
- (добавлено) `--config` без значения, с пустым значением, со значением-опцией, с недопустимым путём или повторённый даёт `Command-line error: …` и код `2`. Относительный путь считается от текущей папки. `--config` побеждает `--Backup:ConfigPath=`.
- (добавлено) `--GitHub:Token=…` и другие ключи принимаются в любом месте строки. Опции команд (`--silent`, `--account x`) в конфигурацию не попадают.
- (добавлено) Неизвестная команда, неизвестная опция и опция чужой команды (`validate --account x`) дают ошибку и код `2`.
- (добавлено) `set-token <что-угодно>`: лишний аргумент отклоняется, его текст не печатается (это почти наверняка токен).
- (добавлено) `validate`: код `0`, если всё в порядке, и `2` при любой ошибке. Предупреждения `backup-config.json` печатаются, код остаётся `0`. Ничего не пишется, кроме логов.
- (добавлено) Сообщения парсера всегда на английском, даже при русской UI-культуре (NFR-7).
- (добавлено) `--dry-run` до #29 ничего не меняет на диске: команда сразу останавливается с кодом `2`.

## 3. Изменения PRD

Версия 0.17 → 0.18:
- FR-9.1, строка `validate`: «Принимает `--config` и ключи конфигурации .NET, как `backup`». Без этого `validate` не проверит тот же файл, с которым запускается `backup --config …`.
- Строка в истории изменений.

## 4. Затрагиваемые проекты и типы

**GitHubBackup.Core**
- `GitHub/GitHubUrl` — `TryParseAccountReference(string, out login)` (URL аккаунта или логин) и `TryParseRepositoryReference(string, out owner, out name)` (URL репозитория или `owner/name`). Повторно используют существующие проверки.

**GitHubBackup.Infrastructure**
- `Hosting/GitHubBackupHostSettings`: `Args` заменяется на `ConfigurationArguments` (`IReadOnlyList<string>`, по умолчанию `[]`, только `--Section:Key=value`); новое `BackupConfigPath` (`string?`, абсолютный путь, переопределяет `Backup:ConfigPath`); `ForCurrentProcess()` без параметра `args`.
- `Hosting/GitHubBackupHostBuilder`: удаляются `ConfigOption`, `FindConfigPath`, `IsConfigurationArgument`; `AddCommandLine(settings.ConfigurationArguments)`, затем `AddInMemoryCollection` для `BackupConfigPath`; обновляются XML-комментарии.
- `Hosting/ConfigurationFileException` — из summary убирается упоминание `--config`.

**GitHubBackup.Cli**
- `CliCommand` — плюс `Validate`, `SetToken`.
- `CliCommandLine` — команды `backup`, `validate`, `set-token`; опции `backup`: `--account`, `--repo`, `--dry-run`, `--silent`, `--config`; опция `validate`: `--config`; валидатор взаимоисключения `--account`/`--repo`; валидаторы значений; сюда переезжает `IsConfigurationArgument`; своё сообщение о лишнем аргументе `set-token` без цитирования.
- `CliArguments` — плюс `BackupConfigPath`, `ConfigurationArguments`, `Backup` (`BackupCommandOptions`).
- `BackupCommandOptions` (новый record) — `Filter`, `DryRun`, `Silent`, `Default`.
- `CliApplication` — `RunAsync(IReadOnlyList<string> args, GitHubBackupHostSettings settings, …)`; разбор первым, ошибки командной строки до сборки хоста; затем `settings with { BackupConfigPath, ConfigurationArguments }` и выбор обработчика.
- `BackupCommandHandler` — `RunAsync(BackupCommandOptions, …)`: фильтр в `BackupRunRequest`; при `DryRun` остановка с кодом `2` до оркестратора.
- `ValidateCommandHandler` (новый) — `IBackupConfigStore.LoadAsync` + `BackupWorkItem.CreateAll`, `IToolLocator.LocateAsync`, `ITokenProvider.GetTokenAsync`; строка на проверку, код `0` или `2`.
- `SetTokenCommandHandler` (новый, заглушка) — `Error: The set-token command is not implemented yet.`, код `2`.
- `CliLogPath` (новый, internal static) — путь к логу дня, общий для обработчиков.
- `CliServiceCollectionExtensions` — регистрация обработчиков; `Program.cs` — новая сигнатура.
- `GitHubBackup.Cli.csproj` — `<SatelliteResourceLanguages>en</SatelliteResourceLanguages>` с комментарием (NFR-7).

**GitHubBackup.TestUtilities**
- `TemporaryContentRoot.BuildHost(params string[] configurationArguments)` заполняет `ConfigurationArguments`.

**Документы**
- `docs/PRD.md` (раздел 3), `README.md` (статус и таблица команд).

## 5. Подход

1. PRD: правка FR-9.1, версия и строка истории.
2. Core: `GitHubUrl.TryParse*Reference` — сначала URL, затем логин или `owner/name` с проверками `IsValidLogin`/`IsValidRepositoryName`.
3. Infrastructure получает готовые строки ключей и абсолютный путь и ничего не разбирает. Строки ключей отдаются стандартному `AddCommandLine`.
4. Парсер: `--config` объявлен у `backup` и `validate`, не у корня; `TreatUnmatchedTokensAsErrors = false` и отбор ключей остаются; `ResponseFileTokenReplacer = null` сохраняется; взаимоисключение — в `backup.Validators` с сообщением `The --account and --repo options cannot be used together.`
5. Сообщения валидаторов значений (значения не цитируются):
   - `--config` пустой, пробельный или начинается с `-`: `The --config option requires the path of the backup configuration file.`;
   - `Path.GetFullPath` бросил исключение: `The --config option is not a valid path.`;
   - `--account`: `The --account option must be https://github.com/<login> or a login.`;
   - `--repo`: `The --repo option must be https://github.com/<owner>/<repo> or owner/repo.`;
   - повторный `--config` — встроенная ошибка арности.
6. Приложение: ошибки командной строки выводятся сразу, хост не строится, код `2`; текст про `--config` переходит из `Configuration error:` в `Command-line error:`; `--help`/`--version` как сейчас; ошибки `appsettings.json` остаются `Configuration error:`; обработчики `Backup`, `Validate`, `SetToken`, `None` (как сейчас).
7. `validate` выполняет шаги 1–3 из §9 без блокировки, обнаружения и записи; проверки независимы. Вывод: `backup-config.json: <path>`, `Warning: …`, `git: <path> (<version>)`, `git-lfs: <version>`, `7-Zip: <path> (<version>)`, `Token: <SourceName>`, `Validation passed.` Ошибки — `Error: …` в stderr, затем `Log: <path>`. Токен без сети: печатается только источник.
8. `--silent` разбирается и передаётся в `BackupCommandOptions`; поведение не меняется.

## 6. Тесты

- `Core.Tests/GitHub/GitHubUrlTests` — `TryParseAccountReference`, `TryParseRepositoryReference`: допустимые и недопустимые формы.
- `Cli.Tests/CliCommandLineTests` (новый) — команды, справка, `BackupCommandOptions.Default`, `--account`/`--repo` в обеих формах, флаги, взаимоисключение в обоих порядках (критерий issue), неверные значения без цитирования, `--config` во всех формах и ошибках, ключи конфигурации, чужие опции, `set-token <аргумент>`, `--config` без команды, английские сообщения при `ru-RU`.
- `CliApplicationTests` — переделка тестов `--config` под `BackupConfigPath` и `Command-line error:`; новые: взаимоисключение одной строкой, справка по командам и опциям, ошибка командной строки не строит хост.
- `BackupCommandHandlerTests` — фильтр уходит в `BackupRunRequest.Filter`; `DryRun` → `Critical`, оркестратор не вызван.
- `ValidateCommandHandlerTests` (новый) — успех, предупреждения, `BackupConfigException`, `ToolUnavailableException`, анонимный токен, значение токена не печатается, `SaveAsync` не вызывается.
- `SetTokenCommandHandlerTests` — `Critical` и одна строка.
- `Infrastructure.Tests/ConfigurationErrorMessagesTests` — нейтральный текст вместо `--config`.
- `SerilogFileLoggingTests` — компилируется с новым `TemporaryContentRoot.BuildHost`.

## 7. Проверка

| Критерий | Как |
|---|---|
| Тесты разбора аргументов | `CliCommandLineTests`, `GitHubUrlTests` |
| `--account` + `--repo` → ошибка | `CliCommandLineTests`, `CliApplicationTests.RunAsync_AccountAndRepo_ReturnsCriticalWithOneLine` |
| Покрыто тестами, CI зелёный | полная сборка, тесты, `dotnet format --verify-no-changes`, CI пулл-реквеста |
| Справка | тесты справки; `dotnet run --project src/GitHubBackup.Cli -- backup --help` |
| `validate` ничего не меняет | `ValidateCommandHandlerTests` |
| Английские сообщения | тест с `ru-RU` |
| Токен не утекает | тесты `set-token`, `validate`, существующие `*NeverWritesToken` |

## 8. Вне рамок

- Хранение токена в Credential Manager и скрытый ввод `set-token` (FR-9.1, FR-11.1) — отдельная задача фазы 3. Здесь `set-token` только разбирается и отвечает заглушкой.
- Поведение `--dry-run` (FR-9.5) — #29. До неё флаг останавливает запуск с кодом `2`.
- Интерактивный режим, отличие `--silent` от режима по умолчанию, FR-9.6 — задачи фазы 3.
- Проверка токена запросом к API в `validate` — нет требования в PRD.
- Ctrl+C (NFR-4), блокировка запуска — свои задачи.
- Поведение без команды (`None`) не меняется.
- `GitHubBackup.App` не использует `GitHubBackupHostSettings`.

## 9. Открытые вопросы

1. `set-token` до Credential Manager: заглушка, код `2`. По умолчанию так.
2. `--dry-run` до #29: остановка с кодом `2` и сообщением `The --dry-run option is not implemented yet; nothing was changed.` По умолчанию так.
3. Ключи в хост — строками `--Section:Key=value` для `AddCommandLine`. По умолчанию так.
4. `--config` у `validate` требует правки FR-9.1 (раздел 3). По умолчанию правка вносится до кода.
5. Повторный `--config` — ошибка, а не «последний побеждает». По умолчанию ошибка.
6. Запуск без команды не меняется. По умолчанию не трогаем.

## 10. Решения

Пользователь утвердил план и все варианты по умолчанию из раздела 9:
- правка PRD FR-9.1 (версия 0.18) вносится до кода;
- `set-token` — заглушка с кодом `2`;
- `--dry-run` до #29 останавливает запуск с кодом `2`;
- ключи передаются в хост строками для `AddCommandLine`;
- повторный `--config` — ошибка;
- запуск без команды не меняется.

## 11. Задачи

- [x] PRD: FR-9.1 — `validate` принимает `--config` и ключи; версия 0.18, строка истории
- [x] Core: `GitHubUrl.TryParseAccountReference` / `TryParseRepositoryReference` + `GitHubUrlTests`
- [x] Infrastructure: `GitHubBackupHostSettings`, `GitHubBackupHostBuilder` без `FindConfigPath`/`ConfigOption`/`IsConfigurationArgument`, документация `ConfigurationFileException`, правка `ConfigurationErrorMessagesTests`
- [x] TestUtilities: `TemporaryContentRoot.BuildHost(configurationArguments)`
- [x] Cli: `CliCommand`, `BackupCommandOptions`, `CliArguments`, `CliCommandLine`
- [x] Cli: `CliApplication`, `Program.cs`
- [x] Cli: `BackupCommandHandler`, `CliLogPath`, `ValidateCommandHandler`, `SetTokenCommandHandler`, регистрация
- [x] Cli.csproj: `SatelliteResourceLanguages=en`
- [x] Тесты: `CliCommandLineTests`, `ValidateCommandHandlerTests`, `SetTokenCommandHandlerTests`, правки существующих
- [x] README: статус и таблица команд
- [x] Сборка, тесты, `dotnet format --verify-no-changes`
- [x] Security review и проверка комментариев в диффе
