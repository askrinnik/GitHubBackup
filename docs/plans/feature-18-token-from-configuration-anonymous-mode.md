# План: #18 F1.11: Token from configuration and anonymous mode

| Поле | Значение |
|---|---|
| Issue | [#18](https://github.com/askrinnik/GitHubBackup/issues/18) |
| Заголовок | F1.11: Token from configuration and anonymous mode |
| Направление | Feature |
| Сложность | S |
| Дата | 2026-11-11 |

## 1. Цель

Добавить `ITokenProvider` (PRD §8.2). Он проходит по расширяемой цепочке источников токена (`ITokenSource`) и берёт первый найденный токен (FR-11.1). На этом этапе в цепочке один источник — ключ `GitHub:Token` из `IConfiguration` (FR-11.1, п. 1). Ключ покрывает `appsettings.json`, переменную `GitHubBackup__GitHub__Token`, user-secrets и `--GitHub:Token=`. Без токена включается анонимный режим с предупреждением (FR-11.1, п. 4; FR-1.8). В лог попадает имя источника, значение токена — никогда (FR-11.1, NFR-1, FR-10.6).

## 2. Критерии приёмки

- Test: a token from an environment variable is found
- Test: without a token anonymous mode is enabled
- Covered by tests, CI green
- (добавлено) Пустой или пробельный токен считается отсутствующим: цепочка переходит к следующему источнику, в итоге включается анонимный режим.
- (добавлено) Источники опрашиваются в порядке регистрации. Первый непустой результат побеждает, следующие источники не вызываются.
- (добавлено) При найденном токене пишется запись Information с именем источника. Ни одна запись не содержит значения токена.
- (добавлено) В анонимном режиме пишется Warning: видны только публичные репозитории, лимит — 60 запросов в час (FR-1.8).
- (добавлено) Найденный токен регистрируется в `ISecretMasker`.
- (добавлено) `ToString()` результата не содержит токен.
- (добавлено) Отменённый `CancellationToken` даёт `OperationCanceledException`.

## 3. Изменения PRD

Не нужны: §6.11, §7.2 и §8.2 описывают всё поведение.

## 4. Затрагиваемые проекты и типы

**GitHubBackup.Core**, папка `Authentication/`:

- `ITokenSource` — `string Name { get; }` и `Task<string?> GetTokenAsync(CancellationToken)`. Одно звено цепочки; без токена возвращает `null`.
- `ITokenProvider` — `Task<GitHubToken> GetTokenAsync(CancellationToken)`.
- `GitHubToken` — `sealed class` (не record: автогенерируемый `ToString` вывел бы токен). Свойства `Value`, `SourceName`, `IsAnonymous`; статическое `Anonymous`; `ToString()` без `Value`.
- `TokenProvider` — принимает `IEnumerable<ITokenSource>`, `ISecretMasker`, `ILogger<TokenProvider>`. Обходит источники по порядку, пропускает `null`, пустые и пробельные значения, регистрирует найденный токен в маскере и пишет лог.
- `TokenProviderLog` — `[LoggerMessage]`: `TokenFound` (Information, имя источника) и `AnonymousMode` (Warning).

**GitHubBackup.Infrastructure**:

- `Authentication/ConfigurationTokenSource` (`internal sealed`) — читает `configuration[GitHubOptions.TokenKey]`, имя `"configuration (GitHub:Token)"`, пробельное значение → `null`, остальное обрезается (`Trim`).
- `AddGitHubBackupInfrastructure` — регистрирует `ITokenSource` через `TryAddEnumerable` с фабрикой (передаётся `IConfiguration` из аргумента метода) и `ITokenProvider` через `TryAddSingleton`.

**Хосты** код не меняют: `GitHubBackupHostBuilder.Create` уже добавляет `AddEnvironmentVariables("GitHubBackup__")`.

## 5. Подход

1. Core: контракты, `GitHubToken`, `TokenProvider`, лог.
2. Infrastructure: `ConfigurationTokenSource` и регистрация в DI. `TokenProvider` регистрируется здесь, потому что зависит от `ISecretMasker`, который создаётся в Infrastructure.
3. Хосты: только тест в `Cli.Tests`.

Ключевые решения: асинхронный контракт (будущий источник `gh auth token` запускает процесс); порядок источников равен порядку регистрации; без кэша; ошибки источников не перехватываются; `ThrowIfCancellationRequested` перед каждым источником.

## 6. Тесты

- `Core.Tests/Authentication/TokenProviderTests` — первый источник с токеном, второй не опрашивается, пропуск `null`/`""`/`" "`, нет токена → анонимный режим, нет источников, Warning, Information без значения, регистрация в маскере, отмена. Логгер — свой `RecordingLogger<T>` в `Core.Tests`.
- `Core.Tests/Authentication/GitHubTokenTests` — `ToString` без значения, `Anonymous`.
- `Infrastructure.Tests/Authentication/ConfigurationTokenSourceTests` — значение, отсутствие и пробелы, обрезка, имя.
- `InfrastructureServiceCollectionExtensionsTests` — токен из конфигурации, анонимный режим без токена, один экземпляр на провайдер.
- `Cli.Tests/TokenEnvironmentVariableTests` (коллекция `ProcessEnvironmentTestGroup`) — переменная `GitHubBackup__GitHub__Token` находится; без переменной — анонимный режим.

## 7. Проверка

| Критерий | Как проверяется |
|---|---|
| Токен из переменной окружения найден | `TokenEnvironmentVariableTests.BuildHost_PrefixedTokenVariable_TokenProviderFindsToken` |
| Без токена — анонимный режим | `TokenProviderTests.GetTokenAsync_NoSourceHasToken_ReturnsAnonymous`, `InfrastructureServiceCollectionExtensionsTests.AddGitHubBackupInfrastructure_NoToken_TokenProviderReturnsAnonymous`, `TokenEnvironmentVariableTests.BuildHost_NoTokenVariable_TokenProviderReturnsAnonymous` |
| Пустой токен, порядок источников | `TokenProviderTests`, `ConfigurationTokenSourceTests` |
| Лог без значения | `TokenProviderTests.GetTokenAsync_TokenFound_LogsSourceNameWithoutValue`, `GitHubTokenTests.ToString_WithToken_OmitsValue` |
| CI зелёный | полная сборка, тесты решения, `dotnet format --verify-no-changes` |

## 8. Вне рамок

- FR-11.1, п. 2: Windows Credential Manager и команда `set-token`.
- FR-11.1, п. 3: источник `gh auth token`.
- Использование `ITokenProvider` оркестратором, `GitRepositoryRequest.Token` и клиентом Octokit (FR-11.5).
- Вывод предупреждения об анонимном режиме в консоль или сводку (FR-9.7) — только лог.

## 9. Решения

- Токен обрезается (`Trim`): защищает от перевода строки из переменной окружения или файла. `TokenProvider` регистрирует в маскере обрезанное значение, исходное уже регистрирует `CreateSecretMasker`.
- Имя источника в логе — `configuration (GitHub:Token)`.
- `TokenProvider` регистрируется в `AddGitHubBackupInfrastructure`, а не в `AddGitHubBackupCore`, из-за зависимости от `ISecretMasker`.
- После security review (PRD 0.13): `ProcessRunner` убирает из окружения дочернего процесса все унаследованные переменные с префиксом `GitHubBackup__` (FR-11.6), потому что переменная окружения стала официальным источником токена. README и FR-11.1 предупреждают, что токен в командной строке виден другим процессам. Альтернатива — запускать дочерние процессы с минимальным окружением, но git и 7-Zip нужны `PATH`, `SystemRoot` и профиль пользователя.

## 10. Задачи

- [x] Core: `ITokenSource`, `ITokenProvider`, `GitHubToken`
- [x] Core: `TokenProvider` и `TokenProviderLog`
- [x] Core.Tests: `RecordingLogger`, `TokenProviderTests`, `GitHubTokenTests`
- [x] Infrastructure: `ConfigurationTokenSource`
- [x] Infrastructure: регистрация в `AddGitHubBackupInfrastructure` и XML-doc
- [x] Infrastructure.Tests: `ConfigurationTokenSourceTests` и три теста регистрации
- [x] Cli.Tests: `TokenEnvironmentVariableTests`
- [x] Сборка, тесты, `dotnet format --verify-no-changes`
- [x] Проверка добавленных комментариев
