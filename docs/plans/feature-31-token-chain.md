# План: #31 F3.6: Token chain: Credential Manager, gh, set-token command

| | |
|---|---|
| Issue | [#31](https://github.com/askrinnik/GitHubBackup/issues/31) |
| Lane | Feature |
| Сложность | L |
| Дата | 2026-11-12 |

## 1. Цель

Нужно достроить цепочку источников токена из FR-11.1. Порядок источников: `IConfiguration` (`GitHub:Token`), затем Windows Credential Manager (Generic Credential, target `GitHubBackup:https://github.com`), затем `gh auth token`, затем анонимный режим. В лог пишется имя источника, значение токена в лог не попадает. Команда `set-token` (FR-9.1) читает токен скрытым вводом и записывает его в Credential Manager. `git credential fill` не используется (FR-11.2). Затронуты также NFR-1, FR-10.4 и FR-10.6: stdout процесса `gh` содержит токен и не должен попасть в лог.

Что уже есть в коде:

- Ядро цепочки готово: `TokenProvider` берёт первый непустой источник, обрезает пробелы, регистрирует токен в `ISecretMasker` и пишет в лог имя источника.
- `ITokenSource` и `ConfigurationTokenSource` готовы.
- Парсер `set-token` готов: аргументы отклоняются, значение не цитируется.
- `validate` выводит `Token: {SourceName}`.

Чего не хватает:

- `SetTokenCommandHandler` — заглушка: выводит `NotImplementedMessage` и возвращает код `2`.
- Нет источников Credential Manager и `gh`, нет хранилища токена.
- `ProcessRunner.ReadLinesAsync` пишет в лог каждую строку stdout. Токен к этому моменту ещё не зарегистрирован в маскировщике, поэтому вывод `gh auth token` попал бы в лог открытым текстом.

## 2. Критерии приёмки

1. Тесты порядка источников с подменами
2. Интеграционный тест записи и чтения Credential Manager (тестовый target)
3. Покрыто тестами, CI зелёный

Добавлено по сверке с кодом и PRD:

4. *(добавлено)* В зарегистрированной цепочке `ITokenSource` три источника в порядке configuration → Credential Manager → gh. Порядок проверяется через DI.
5. *(добавлено)* Вывод `gh auth token` не попадает ни в лог, ни в консоль. Строка запуска и stderr пишутся в лог как обычно (FR-10.4).
6. *(добавлено)* Источник `gh` возвращает `null` (цепочка идёт дальше, к анонимному режиму), если: `gh.exe` нет в `PATH`; процесс не запускается (`ProcessStartException`); код возврата не 0; stdout пустой или из одних пробелов. Отмена (`OperationCanceledException`) пробрасывается дальше.
7. *(добавлено)* Источник Credential Manager возвращает `null`, если записи нет. Если чтение падает с ошибкой Win32, он пишет предупреждение без значения и тоже возвращает `null`.
8. *(добавлено)* `set-token`: скрытый ввод, пустой или пробельный ввод запрашивается повторно; токен обрезается и записывается, выводится строка об успехе, код `0`; если ввод перенаправлен — код `2` и подсказка, ничего не пишется; если запись не удалась — код `2` и одна строка ошибки без токена; токен не появляется в stdout, stderr и логе.

## 3. Затрагиваемые проекты и типы

**GitHubBackup.Core**

- `Authentication/ITokenStore.cs` (новый) — хранилище токена программы: `Task<string?> ReadAsync(CancellationToken)`, `Task WriteAsync(string token, CancellationToken)`, `string Location` (описание места для сообщений, без секрета). Его же позже использует UI (FR-12).
- `Authentication/TokenStoreException.cs` (новый) — ошибка чтения или записи хранилища. В сообщении есть target и текст ошибки Win32, токена нет.
- `Processes/ProcessRequest.cs` — новое свойство `bool IsStandardOutputSecret { get; init; }`: строки stdout собираются в результат, но в лог не пишутся.

**GitHubBackup.Infrastructure**

- `Processes/ProcessRunner.cs` — `ReadLinesAsync` пропускает запись stdout в лог, если запрос помечен секретным.
- `Authentication/CredentialManagerTokenStore.cs` (новый, `internal sealed`) — `ITokenStore` через `Meziantou.Framework.Win32.CredentialManager`: `DefaultTarget = "GitHubBackup:https://github.com"`, target передаётся в конструктор (для тестов), `CredentialPersistence.LocalMachine`, `Win32Exception` оборачивается в `TokenStoreException`.
- `Authentication/CredentialManagerTokenSource.cs` (новый) — `ITokenSource` поверх `ITokenStore`, имя `Windows Credential Manager (GitHubBackup:https://github.com)`. Ловит `TokenStoreException`: предупреждение и `null`.
- `Authentication/GitHubCliTokenSource.cs` (новый) — `ITokenSource`, имя `GitHub CLI (gh auth token)`. Ищет `gh.exe` в `PATH` и выполняет `gh auth token --hostname github.com` через `IProcessRunner`: `IsStandardOutputSecret = true`; `GH_PROMPT_DISABLED=1`, `GH_NO_UPDATE_NOTIFIER=1`; рабочая папка — папка `gh.exe`; результат — первая непустая строка stdout без пробелов по краям.
- `Authentication/TokenSourceLog.cs` (новый) — `[LoggerMessage]`: gh не найден, gh завершился с кодом, gh не запустился, чтение Credential Manager не удалось. Значений токена нет.
- `Tools/ExecutableSearchPath.cs` (новый, `internal static`) — поиск исполняемого файла в `PATH`, вынесен из `ToolLocator.FindGit`: пропускает пустые и неполные пути, текущую папку не ищет.
- `Tools/ToolLocator.cs` — использует `ExecutableSearchPath`, поведение не меняется.
- `InfrastructureServiceCollectionExtensions.cs` — регистрирует `ITokenStore` и источники по порядку: Configuration, CredentialManager, GitHubCli.
- `GitHubBackup.Infrastructure.csproj`, `src/Directory.Packages.props` — пакет `Meziantou.Framework.Win32.CredentialManager` (последняя стабильная версия).

**GitHubBackup.Cli**

- `SetTokenCommandHandler.cs` — реализация на `CliConsole` и `ITokenStore`, сигнатура `RunAsync(CancellationToken)`. `NotImplementedMessage` удаляется; новые константы `RedirectedInputMessage`, `SavedMessage` и префикс ошибки.
- `CliApplication.RunCommandAsync` — передаёт `cancellationToken` в `set-token`.

**Документация**

- `README.md`: статус, таблица команд (`set-token`, `validate`), абзац о цепочке токена.

**Выбор пакета (§8.3).** `Meziantou.Framework.Win32.CredentialManager`: простой API (`WriteCredential`, `ReadCredential` возвращает `null` без записи, `DeleteCredential`), активная поддержка, современный .NET. `AdysTech.CredentialManager` обновляется реже. Собственный P/Invoke (`CredReadW`/`CredWriteW`) — ручной маршалинг `CREDENTIAL` и `CredFree`, больше кода и риска.

## 4. Подход

1. **Core.** `ITokenStore`, `TokenStoreException`, `ProcessRequest.IsStandardOutputSecret`. `TokenProvider` не меняется: он уже обрезает и маскирует найденный токен.
2. **ProcessRunner.** Флаг подавляет только логирование stdout; строки по-прежнему возвращаются в `ProcessResult.StandardOutput`. Маскировщик не помогает: значение ещё неизвестно, когда строка пишется в лог.
3. **Поиск в `PATH`.** Общий код выносится из `ToolLocator`. Настройки `Tools:GhPath` нет: PRD её не описывает. Для тестов источник получает `GetEnvironmentVariable` как `init`-свойство, по образцу `ToolLocator`.
4. **CredentialManagerTokenStore.** Синхронный API пакета за асинхронным интерфейсом. Имя пользователя в записи фиксированное.
5. **Источники и DI.** Регистрация через `TryAddEnumerable` в нужном порядке; порядок регистрации — порядок цепочки.
6. **set-token.** Перенаправленный ввод → одна строка в stderr и `ExitCode.Critical` (как FR-9.6). Иначе `TextPrompt<string>("GitHub token:").Secret()`, валидатор отклоняет пробельный ввод; затем `WriteAsync(value.Trim())`, строка об успехе с `Location`, код `Success`. `TokenStoreException` → одна строка `Error: …` и код `Critical`.
7. **Детерминизм тестов хоста.** Настоящий хост на машине разработчика найдёт токен в Credential Manager или в `gh`, поэтому тесты «без токена — анонимный режим» на реальном хосте заменяются проверкой состава и порядка `GetServices<ITokenSource>()`.

Безопасность: токен не попадает в аргументы и в лог; `gh` получает только переменные, отключающие запросы и проверку обновлений, `GitHubBackup__*` уже удаляются из окружения `ProcessRunner`; токен не кэшируется в статических полях.

## 5. Тесты

**GitHubBackup.Core.Tests / `TokenProviderTests`**

- `GetTokenAsync_ThreeSources_TakesFirstNonBlankInOrder` — theory: токен только в 1-м, 2-м, 3-м источнике, ни в одном; проверить `SourceName`.
- `GetTokenAsync_EarlierSourceHasToken_LaterSourcesNotQueried`.

**GitHubBackup.Infrastructure.Tests**

- `Authentication/GitHubCliTokenSourceTests`: успех с обрезкой; форма запроса (аргументы, флаг, переменные, рабочая папка); `gh` не найден; пустые и относительные элементы `PATH`; `ProcessStartException`; ненулевой код; пустой stdout; отмена; нет токена в логе.
- `Authentication/CredentialManagerTokenSourceTests`: значение, `null`, `TokenStoreException`, `Name`.
- `Tools/ExecutableSearchPathTests`: найден, кавычки и пробелы, неполные пути, не найден.
- `InfrastructureServiceCollectionExtensionsTests`: `AddGitHubBackupInfrastructure_TokenSources_RegisteredInChainOrder`; `ITokenStore` — `CredentialManagerTokenStore`; машинно-зависимый тест анонимного режима заменяется.

**GitHubBackup.Infrastructure.IntegrationTests**

- `Authentication/CredentialManagerTokenStoreTests` — настоящий Credential Manager, target `GitHubBackup.Tests:{Guid}`, `Dispose` удаляет запись: записи нет → `null`; запись и чтение; перезапись.
- `Processes/ProcessRunnerLoggingTests` — при `IsStandardOutputSecret = true` строка stdout есть в результате, но не в логе; stderr и строка запуска логируются.

**GitHubBackup.Cli.Tests**

- `SetTokenCommandHandlerTests` — переписать: обрезка и запись, повторный запрос, перенаправленный ввод, ошибка записи, отмена, токена нет в выводе.
- `CliApplicationTests` — `RunAsync_SetToken_ReturnsCriticalNotImplemented` заменить на `RunAsync_SetTokenInputRedirected_ReturnsCriticalWithHint`.
- `TokenEnvironmentVariableTests` — машинно-зависимый тест анонимного режима поправить (см. *Открытые вопросы*).

## 6. Проверка

| Критерий | Чем проверяется |
|---|---|
| 1 | `TokenProviderTests`, `*RegisteredInChainOrder`, `GitHubCliTokenSourceTests`, `CredentialManagerTokenSourceTests` |
| 2 | `CredentialManagerTokenStoreTests` |
| 3 | полная сборка, тесты решения, `dotnet format --verify-no-changes`; CI на PR |
| 4 | `*RegisteredInChainOrder` |
| 5 | `ProcessRunnerLoggingTests`, `GitHubCliTokenSourceTests` |
| 6–7 | `GitHubCliTokenSourceTests`, `CredentialManagerTokenSourceTests` |
| 8 | `SetTokenCommandHandlerTests`, `CliApplicationTests.RunAsync_SetTokenInputRedirected_*` |

## 7. Вне рамок

- UI для сохранения токена (FR-12): он использует тот же `ITokenStore`.
- Удаление токена из Credential Manager: PRD такой команды не описывает.
- Таймаут для `gh`: у `IProcessRunner` таймаутов нет; `gh auth token` читает только локальное хранилище, stdin закрыт.
- Повторный вызов `gh` за один `backup` (`BackupOrchestrator` и `OctokitGitHubApi.EnsureClient` оба вызывают `GetTokenAsync`). Кэширование в `TokenProvider` — отдельное решение.
- Проверка токена (кто авторизован, scopes) — FR-12.

## 8. Открытые вопросы

Нет: все вопросы решены на ревью (см. *Решения*).

## 9. Решения

Приняты на ревью плана (2026-11-12):

1. Если `CredWrite` на CI-раннере падает с ошибкой 1312 (`ERROR_NO_SUCH_LOGON_SESSION`), интеграционный тест Credential Manager пропускается через `Assert.Skip` только при этой ошибке. `ci.yml` не меняется.
2. `set-token` с перенаправленным вводом отказывает: одна строка подсказки в stderr, код `2`, ничего не записывается. PRD не меняется.
3. Тест `TokenEnvironmentVariableTests.BuildHost_NoTokenVariable_*` удаляется: состав и порядок цепочки проверяет DI-тест, анонимный режим — `TokenProviderTests` с подменами.
4. Запись Credential Manager: имя пользователя `GitHubBackup`, `CredentialPersistence.LocalMachine`. `gh` вызывается как `gh auth token --hostname github.com`.
5. #31 выполняется отдельно от пакета с #32 из-за сложности L; реализация — `issue-developer` на Opus.

Приняты при реализации:

6. `gh` наследует окружение программы (кроме `GitHubBackup__*`). Если у пользователя заданы `GH_TOKEN`, `GITHUB_TOKEN` или `GH_CONFIG_DIR`, `gh auth token` учитывает их — это поведение самого `gh`, утечки нет. Переменные не очищаются.
7. Слишком длинный токен (больше 2560 байт) в `set-token` даёт одну строку `Error:` и код `2`: `CredentialManagerTokenStore` оборачивает `ArgumentException` в `TokenStoreException`.
8. Хостовые тесты CLI задают `GitHub:Token` в тестовой конфигурации: цепочка останавливается на первом источнике и не читает настоящий Credential Manager и не запускает `gh`.
9. Уровни логов источников: `gh` не найден — Debug (обычный случай), `gh` не вернул токен — Information, сбой запуска `gh` и чтения Credential Manager — Warning.

## 10. Задачи

- [x] Добавить `Meziantou.Framework.Win32.CredentialManager` в `src/Directory.Packages.props` и `GitHubBackup.Infrastructure.csproj`; проверить уязвимости пакетов
- [x] Core: `ITokenStore`, `TokenStoreException`, `ProcessRequest.IsStandardOutputSecret`
- [x] `ProcessRunner`: не логировать stdout при `IsStandardOutputSecret`; тест в `ProcessRunnerLoggingTests`
- [x] Вынести `ExecutableSearchPath` из `ToolLocator.FindGit`; `ExecutableSearchPathTests`; `ToolLocatorTests` проходят
- [x] `CredentialManagerTokenStore` и интеграционные `CredentialManagerTokenStoreTests`
- [x] `CredentialManagerTokenSource`, `TokenSourceLog`, `CredentialManagerTokenSourceTests`
- [x] `GitHubCliTokenSource` и `GitHubCliTokenSourceTests`
- [x] DI: `ITokenStore` и три источника по порядку; тест порядка; убрать машинно-зависимый тест анонимного режима
- [x] `TokenProviderTests`: theory порядка трёх источников
- [x] `SetTokenCommandHandler`; `CliApplication` передаёт `cancellationToken`
- [x] Переписать `SetTokenCommandHandlerTests`; заменить тест `CliApplicationTests`; поправить `TokenEnvironmentVariableTests`
- [x] README: статус, таблица команд, абзац о цепочке токена
- [x] Сборка, тесты, `dotnet format --verify-no-changes`; проверка комментариев
