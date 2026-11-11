# План: [#20](https://github.com/askrinnik/GitHubBackup/issues/20) F2.1: GitHub API wrapper (Octokit.NET)

- Лейн: Feature
- Сложность: L
- Дата: 2026-11-11

## 1. Цель

Нужна абстракция `IGitHubApi` в `Core` и её реализация на Octokit.NET в `Infrastructure` (FR-11.5, §8.2).

Абстракция умеет:
- определять тип аккаунта (FR-1.1);
- получать все страницы трёх списков FR-2.1;
- получать репозиторий по `owner/name` и по ID с признаками private, fork, archived, has_wiki и default_branch.

Адрес API берётся из `GitHub:ApiBaseUrl`. После каждого вызова остаток лимита запросов пишется в лог. Если лимит исчерпан, wrapper бросает доменное исключение с временем сброса лимита.

Интерфейс рассчитан на будущие issues: #22 и #23 (классификация, 404/403 означает «недоступен», переименование по ID), #24 (`HasWiki`), #25 (исчерпанный лимит — критическая ошибка). Сами эти issues здесь не реализуются.

## 2. Критерии приёмки

- [x] Контрактные тесты WireMock.Net: постраничность, 401, 403, 404, лимит
- [x] Octokit не виден за пределами `Infrastructure`
- [ ] Покрыто тестами, CI зелёный
- [x] *(добавлено)* Запросы точно повторяют FR-2.1. `/user/repos` идёт с `affiliation=owner,collaborator,organization_member&visibility=all`. `/orgs/{org}/repos` и `/users/{login}/repos` идут с `type=all`. Проверка: matcher-ы параметров в WireMock.
- [x] *(добавлено)* Запросы идут по настроенному `GitHub:ApiBaseUrl` без переписывания пути.
- [x] *(добавлено)* Остаток лимита (`remaining/limit`, время сброса) пишется в лог, токен в лог не попадает (FR-10.6, NFR-1).
- [x] *(добавлено)* Токен берётся из существующего `ITokenProvider` (#18). В анонимном режиме заголовок `Authorization` не отправляется.
- [x] *(добавлено)* Неверный `login`, `owner` или `name` отклоняется до HTTP-запроса. Валидаторы: `GitHubUrl.IsValidLogin` и `IsValidRepositoryName`.

## 3. Изменения PRD

Не нужны. FR-1.1, FR-2.1, FR-11.5, §7.2 (`GitHub:ApiBaseUrl` и его валидация) и §11 (контрактные тесты в `GitHubBackup.Infrastructure.Tests`) покрывают задачу.

## 4. Затрагиваемые проекты и типы

**`GitHubBackup.Core`** — новые типы в `Core/GitHub/` (там уже лежит `GitHubUrl`):
- `IGitHubApi` — контракт, все методы `async` и принимают `CancellationToken`:
  - `GetAccountAsync(string login, ct)` → `GitHubAccount` (`GET /users/{login}`);
  - `GetCurrentUserRepositoriesAsync(ct)` (`/user/repos`);
  - `GetOrganizationRepositoriesAsync(string organization, ct)`;
  - `GetUserRepositoriesAsync(string login, ct)`;
  - `GetRepositoryAsync(string owner, string name, ct)`;
  - `GetRepositoryByIdAsync(long id, ct)` (`/repositories/{id}`, нужен для переименований в #22).

  Списочные методы возвращают `IReadOnlyList<GitHubRepository>`.
- `GitHubAccountType` — enum `User` и `Organization`.
- `GitHubAccount` — record `(string Login, GitHubAccountType Type)`. `Login` берётся в регистре сервера.
- `GitHubRepository` — record `(long Id, string Owner, string Name, bool IsPrivate, bool IsFork, bool IsArchived, bool HasWiki, string DefaultBranch, string HtmlUrl)` и вычисляемое `FullName`.
- `GitHubApiException` — базовое исключение, не sealed. Хранит `int? StatusCode` и `string Resource` (например `repos/owner/name`). Покрывает прочие HTTP-ошибки (5xx), сетевые сбои и таймауты.
- `GitHubAuthenticationException` (401), `GitHubAccessDeniedException` (403), `GitHubNotFoundException` (404) — sealed-наследники.
- `GitHubRateLimitExceededException` — sealed-наследник с `DateTimeOffset? ResetAt`, `int Limit` и `int Remaining`. Сообщение называет время сброса.

**`GitHubBackup.Infrastructure`** — новые типы в `Infrastructure/GitHub/`, все `internal`:
- `OctokitGitHubApi : IGitHubApi` — реализация на `IGitHubClient`.
- `OctokitGitHubApiLog` — `[LoggerMessage]`: остаток лимита, анонимный пропуск `/user/repos`.
- `OctokitExceptionMapper` — переводит исключения Octokit в доменные (допустимо как private-методы `OctokitGitHubApi`).
- `OctokitModelMapper` — `Octokit.Repository` и `Octokit.User` → доменные record-ы.

Изменения:
- `InfrastructureServiceCollectionExtensions` — `TryAddSingleton<IGitHubApi, OctokitGitHubApi>()` и обновлённый XML-doc.
- `GitHubBackup.Infrastructure.csproj` — `PackageReference Include="Octokit"`.

**`src/Directory.Packages.props`**:
- `Octokit` в новой группе `Label="GitHub API"`; актуальную стабильную версию подтвердить при добавлении.
- `WireMock.Net` в группе `Tests`.
- После добавления выполнить `dotnet list package --vulnerable` (правило D1).

**`GitHubBackup.Infrastructure.Tests`**:
- csproj: `WireMock.Net` (и `Microsoft.Extensions.TimeProvider.Testing`, только если понадобится);
- новый `GitHub/OctokitGitHubApiTests.cs`;
- дополнение `InfrastructureServiceCollectionExtensionsTests`.

**`GitHubBackup.ArchitectureTests`**:
- `LayerRules`: свойства `Cli` и `App` (Assembly) и константы Octokit для хостов;
- новый `OctokitVisibilityTests.cs`.

## 5. Подход

1. **Core.** Контракт, модели и исключения. `CoreDependencyTests` уже запрещает `Octokit` в `Core`.
2. **Клиент.** `OctokitGitHubApi(ITokenProvider, IOptions<GitHubOptions>, ILogger<OctokitGitHubApi>)`.
   - `new GitHubClient(new Connection(new ProductHeaderValue("GitHubBackup", <версия сборки>), baseUri))`.
   - К `baseUri` добавляется завершающий `/`, иначе Octokit отбрасывает последний сегмент пути при склейке URI.
   - `Connection` используется сознательно: конструктор `GitHubClient(header, baseAddress)` вызывает `FixUpBaseUri` и добавляет `/api/v3/` ко всем хостам, кроме `api.github.com`.
   - **Риск:** проверить, что `Connection` не переписывает адрес; если переписывает — отметить в плане и обсудить.
3. **Токен (переиспользуем #18).** При первом вызове `await tokenProvider.GetTokenAsync(ct)` один раз на экземпляр под `SemaphoreSlim(1,1)`; класс `IDisposable`. Есть токен → `client.Credentials = new Credentials(token.Value)`; анонимный режим → `Credentials.Anonymous`. Токен не попадает в логи и исключения.
4. **Списки (все страницы).**
   - `/user/repos`: `GetAllForCurrent(new RepositoryRequest { Affiliation = All, Visibility = All }, new ApiOptions { PageSize = 100 })`; `Type` не задавать.
   - Анонимный режим: пустой список без запроса, Debug-запись.
   - `/orgs/{org}/repos?type=all` и `/users/{login}/repos?type=all` через `new ApiConnection(client.Connection).GetAll<Repository>(relativeUri, {type=all, per_page=100})` — типизированные методы не задают `type`.
   - Сегменты пути экранирует `Uri.EscapeDataString` после проверки валидаторами `GitHubUrl`.
5. **Единичные запросы.** `client.User.Get(login)` (`Organization` → `Organization`, иначе `User`), `client.Repository.Get(owner, name)`, `client.Repository.Get(id)`.
6. **Лимит в лог.** После каждого публичного метода `client.GetLastApiInfo()?.RateLimit` → одна Information-запись `"GitHub API rate limit: {Remaining}/{Limit} remaining, resets at {ResetAt}"`. Нет сведений → нет записи.
7. **Ошибки.** Один helper `ExecuteAsync(resource, func, ct)`:

   | Octokit | Доменное исключение |
   |---|---|
   | `RateLimitExceededException` | `GitHubRateLimitExceededException(Reset, Limit, Remaining)` |
   | `SecondaryRateLimitExceededException` | `GitHubRateLimitExceededException`, `ResetAt` из `Retry-After` или `null` |
   | `AuthorizationException` (401, `TwoFactorRequiredException`) | `GitHubAuthenticationException` |
   | `ForbiddenException` (403) | `GitHubAccessDeniedException` |
   | `NotFoundException` (404) | `GitHubNotFoundException` |
   | прочий `ApiException`, `HttpRequestException`, `TaskCanceledException` без нашей отмены | `GitHubApiException` |

   Специфичные исключения проверяются раньше общих. Сообщение: наш текст, ресурс, код статуса, `ApiError.Message`. Исключение Octokit не кладётся в `InnerException` (S2).
8. **Отмена.** Каждый вызов Octokit обёрнут в `.WaitAsync(ct)`, между шагами `ThrowIfCancellationRequested()`; наш `OperationCanceledException` проходит без изменений.
9. **DI.** Singleton в `AddGitHubBackupInfrastructure`; новых настроек нет.
10. **Архитектура.** `OctokitVisibilityTests`: а) типы `Infrastructure`, зависящие от `Octokit`, не публичные; б) `Cli` и `App` не ссылаются на сборку `Octokit`; в) `Cli` и `App` не используют пространство имён `Octokit`.

## 6. Тесты

**`GitHubBackup.Infrastructure.Tests/GitHub/OctokitGitHubApiTests.cs`** — контрактные тесты WireMock.Net на loopback. `ApiBaseUrl = server.Url`, `ITokenProvider` — NSubstitute, `RecordingLogger<OctokitGitHubApi>`, JSON — raw string literals.

- `GetCurrentUserRepositoriesAsync_TwoPages_ReturnsAllRepositories` (заголовок `Link` с `rel="next"`).
- `GetCurrentUserRepositoriesAsync_Request_SendsAffiliationAndVisibility`.
- `GetOrganizationRepositoriesAsync_TwoPages_ReturnsAllRepositoriesWithTypeAll`.
- `GetUserRepositoriesAsync_TwoPages_ReturnsAllRepositoriesWithTypeAll`.
- `GetCurrentUserRepositoriesAsync_Anonymous_ReturnsEmptyWithoutRequest`.
- `GetRepositoryAsync_Found_MapsFlags` (`[Theory]`).
- `GetRepositoryByIdAsync_Found_ReturnsRepository`.
- `GetAccountAsync_TypeOrganization_ReturnsOrganization`, `GetAccountAsync_TypeUser_ReturnsUser` (включая `Bot` → `User`).
- `GetRepositoryAsync_Unauthorized_ThrowsGitHubAuthenticationException`.
- `GetRepositoryAsync_Forbidden_ThrowsGitHubAccessDeniedException`.
- `GetRepositoryAsync_NotFound_ThrowsGitHubNotFoundException`; то же для `GetAccountAsync`.
- `GetCurrentUserRepositoriesAsync_RateLimitExhausted_ThrowsWithResetTime`.
- `GetRepositoryAsync_ServerError_ThrowsGitHubApiException`.
- `GetRepositoryAsync_Success_LogsRemainingRateLimit`.
- `GetRepositoryAsync_Authenticated_SendsTokenHeaderAndNeverLogsIt`.
- `GetRepositoryAsync_Anonymous_SendsNoAuthorizationHeader`.
- `GetRepositoryAsync_ApiBaseUrlWithPath_UsesPathAsIs`.
- `GetAccountAsync_InvalidLogin_ThrowsArgumentExceptionWithoutRequest`; то же для `owner` и `name`.
- `GetRepositoryAsync_Cancelled_ThrowsOperationCanceledException`.
- `GetRepositoryAsync_TwoCalls_ResolvesTokenOnce`.

**`InfrastructureServiceCollectionExtensionsTests`**: `AddGitHubBackupInfrastructure_GitHubApi_IsOneInstancePerProvider`.

**`GitHubBackup.ArchitectureTests/OctokitVisibilityTests.cs`**: три теста из п. 10; при наличии образца в `RuleDetectionTests` — случай для нового правила.

## 7. Проверка

| Критерий | Как проверить |
|---|---|
| WireMock: постраничность, 401, 403, 404, лимит | `--filter-class "*OctokitGitHubApiTests"` в `GitHubBackup.Infrastructure.Tests` |
| Octokit не виден за пределами `Infrastructure` | `*OctokitVisibilityTests` и `*CoreDependencyTests` в `GitHubBackup.ArchitectureTests` |
| Параметры FR-2.1, адрес API, лог лимита, токен | одноимённые тесты из раздела 6 |
| Покрыто тестами, CI зелёный | полный rebuild, тесты решения, `dotnet format --verify-no-changes`, CI в PR |
| Пакеты без уязвимостей | `dotnet list src/GitHubBackup.slnx package --vulnerable` |

## 8. Вне рамок

- Классификация репозиториев и `IRepositoryDiscoveryService` — #22.
- Решения обнаружения — #23. Бэкап wiki — #24.
- Перевод исчерпанного лимита в критическую ошибку запуска — #25.
- Источники токена Credential Manager и `gh auth token`.
- Повторы запросов и ожидание сброса лимита: PRD требует ошибку.
- Smoke-тест против реального GitHub.
- Сброс кэшированного токена после `set-token` в WPF.

## 9. Открытые вопросы

1. **Тип `Bot`.** Считать `User` (запрос `/users/{login}/repos`). Альтернатива — исключение «неподдерживаемый тип аккаунта», что требует строки в FR-1.1.
2. **Вторичный лимит** (403 или 429 с `Retry-After`). Считать исчерпанным лимитом, `ResetAt` из `Retry-After` или `null`.
3. **`/user/repos` без токена.** Пустой список без запроса (FR-1.8).
4. **Кэш токена.** Один раз на экземпляр singleton; сброс для WPF — будущий issue.
5. **`InnerException`.** Исключения Octokit не сохраняются; в сообщение идут только код статуса и текст ошибки GitHub.

## 10. Задачи

- [x] Добавить `Octokit` и `WireMock.Net` в `src/Directory.Packages.props`, сослаться на них из `Infrastructure` и `Infrastructure.Tests`, проверить `--vulnerable`.
- [x] `Core/GitHub`: `IGitHubApi`, `GitHubAccountType`, `GitHubAccount`, `GitHubRepository`.
- [x] `Core/GitHub`: `GitHubApiException` и наследники для 401, 403, 404 и лимита (с `ResetAt`).
- [x] `Infrastructure/GitHub`: `OctokitGitHubApi`: клиент через `Connection`, ленивый токен через `ITokenProvider`, валидация входа.
- [x] Списки FR-2.1 со всеми страницами и `per_page=100`; `type=all` через `ApiConnection.GetAll`; анонимный пропуск `/user/repos`.
- [x] Единичные запросы: аккаунт, репозиторий по имени и по ID; `OctokitModelMapper`.
- [x] `OctokitExceptionMapper` и `ExecuteAsync` с `WaitAsync(ct)`; без `InnerException` Octokit.
- [x] `OctokitGitHubApiLog`: остаток лимита после каждого метода.
- [x] Регистрация singleton в `AddGitHubBackupInfrastructure` и XML-doc.
- [x] Контрактные тесты `OctokitGitHubApiTests` и тест DI.
- [x] `OctokitVisibilityTests` и расширение `LayerRules` сборками `Cli` и `App`.
- [x] Build, тесты, `dotnet format --verify-no-changes`; проверить добавленные комментарии по `csharp.md`.

## Решения

План утверждён без правок; открытые вопросы решены так, как предложено в разделе 9:

1. `type: "Bot"` считается `User`.
2. Вторичный лимит считается исчерпанным лимитом; `ResetAt` из `Retry-After` или `null`.
3. `/user/repos` без токена возвращает пустой список без запроса.
4. Токен кэшируется один раз на экземпляр singleton; сброс для WPF — будущий issue.
5. Исключения Octokit не сохраняются в `InnerException`.

Отклонения при реализации:

- Списки обходятся постранично вручную через `IConnection.Get`: ссылка `rel="next"` принимается, только если она ведёт под настроенный `GitHub:ApiBaseUrl` (та же схема, хост, порт и префикс пути). Иначе — `GitHubApiException` без запроса: учётные данные идут с каждым запросом страницы, и сервер не должен уводить их на другой хост (ревью безопасности, S1).
- PRD 0.15 (утверждено): §7.2 допускает `http` в `GitHub:ApiBaseUrl` только для loopback-адреса, FR-11.5 описывает правило ссылки на следующую страницу; `GitHubOptionsValidator` проверяет новое правило.
- `/user/repos` идёт с явными параметрами, как и остальные списки: в Octokit 14 `RepositoryAffiliation.All` сериализуется с пробелами (`owner, collaborator, organization_member`), что расходится с FR-2.1.
- `OctokitGitHubApi` получает `TimeProvider`, чтобы перевести `Retry-After` в секундах в `ResetAt`.
- Ответ 429 тоже считается исчерпанным лимитом (решение 2).
- Лимит пишется в лог только после успешного вызова и только при `Limit > 0`.
- В `Infrastructure.Tests` добавлена прямая ссылка на `Microsoft.AspNetCore.WebUtilities` 2.3.14: WireMock.Net транзитивно приносит 2.3.0 с уязвимостью GHSA-6527-9fp8-r4x6.
