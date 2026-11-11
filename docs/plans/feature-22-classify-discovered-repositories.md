# План: #22 «F2.3: Classify discovered repositories»

- Issue: [#22](https://github.com/askrinnik/GitHubBackup/issues/22)
- Lane: Feature (area:core)
- Сложность: M
- Дата: 2026-11-11
- Пакет: `/implement-issues 21–25`

## 1. Цель

В `GitHubBackup.Core` появляется `IRepositoryDiscoveryService`. Он собирает репозитории, доступные через GitHub API, объединяет их без дублей по ID (FR-2.1) и сравнивает с `backup-config.json`. Каждый репозиторий он относит к одному из классов: новый в аккаунте (FR-2.2), новый вне аккаунтов (FR-2.3), пропавший (FR-2.4), вернувшийся (FR-2.4), переименованный (FR-2.5). Сервис учитывает исключения (FR-1.6) и фильтр `--account` (FR-2.7). Сервис только классифицирует: он не меняет конфиг и не подключается к orchestrator.

## 2. Критерии приёмки

Из issue:

1. Unit tests for every class with a substituted `IGitHubApi`
2. Covered by tests, CI green

Добавлено по PRD и сверке с кодом:

- Источники (FR-2.1): `GetCurrentUserRepositoriesAsync` и список каждого аккаунта; тип аккаунта из `GetAccountAsync` (FR-1.1): `Organization` → `GetOrganizationRepositoriesAsync`, `User` → `GetUserRepositoriesAsync`. Дубли убираются по `Id`.
- Репозиторий, чей `Id` есть в конфиге (в аккаунте или в группе `repositories`), никогда не новый.
- Исключённый репозиторий не считается новым ни в одном классе (FR-1.6).
- Форки, архивные и приватные классифицируются как любые другие (FR-1.5).
- Пропавший: записи `Active` нет в собранном списке, и `GetRepositoryAsync` выбросил `GitHubNotFoundException` или `GitHubAccessDeniedException`.
- Переименованный: тот же `Id` под другим `owner/name` (из списка, редиректа `GetRepositoryAsync` или `GetRepositoryByIdAsync`).
- Вернувшийся: запись `Unavailable` снова найдена; по-прежнему не найденная — без события.
- Сбой API при сборе (rate limit, аутентификация, сеть, 404 аккаунта) уходит наверх; rate limit при проверке записи не превращается в «пропавший».
- `--account`: обнаружение только для этого аккаунта; неизвестный аккаунт → `ArgumentException` до первого запроса.
- Отмена уходит наверх; порядок результатов детерминирован.

## 3. Изменения PRD

Не нужны: модель уже хранит `Id` (FR-1.7) и `Status` (§7.3).

## 4. Затрагиваемые проекты и типы

**GitHubBackup.Core**, `Discovery/`:

- `IRepositoryDiscoveryService.DiscoverAsync(BackupConfig config, string? accountLogin, CancellationToken)` → `DiscoveryResult`.
- `RepositoryDiscoveryService` (`sealed`): `IGitHubApi`, `IExclusionMatcher`, `ILogger<…>`; без состояния.
- `RepositoryDiscoveryServiceLog` (`[LoggerMessage]`): Debug — число репозиториев из источника, Information — итог по классам.
- `TrackedRepository(Owner, Name, Id, Status, AccountLogin?)`, `NewAccountRepository(AccountLogin, Repository)`, `RenamedRepository(Tracked, Current)`, `ReturnedRepository(Tracked, Current)`.
- `DiscoveryResult`: `NewInAccounts`, `NewStandalone`, `Missing`, `Returned`, `Renamed`, `HasChanges`, `Empty`.
- Регистрация `TryAddSingleton<IRepositoryDiscoveryService, RepositoryDiscoveryService>()` в `AddGitHubBackupCore`.

**GitHubBackup.Core.Tests**: `Discovery/RepositoryDiscoveryServiceTests`, `Discovery/DiscoveryResultTests`, дополнение `CoreServiceCollectionExtensionsTests`.

## 5. Подход

1. **Записи конфига** разбираются в `TrackedRepository` (`GitHubUrl.TryParseAccount` / `TryParseRepository`); невалидный URL → `ArgumentException`, как в `BackupWorkItem.CreateAll`.
2. **Область:** с `accountLogin` — только этот аккаунт и его записи (`OrdinalIgnoreCase`); без фильтра — всё.
3. **Сбор** последовательно: `/user/repos` (и с фильтром — коллабораторские приватные приходят только оттуда), затем аккаунты области в порядке файла. Упорядоченный словарь по `Id`, первое вхождение побеждает.
4. **Новые:** `Id` нет в конфиге и не исключён. Owner равен login отслеживаемого аккаунта → `NewInAccounts` (с фильтром — только этого аккаунта); иначе, только без фильтра → `NewStandalone`.
5. **Записи области:** запись, исключённая под своим именем, пропускается без запросов. Остальные: `Id` в словаре → `current`; иначе `GetRepositoryAsync(owner, name)` (404/403 → `null`; другой `Id` → `GetRepositoryByIdAsync(id)` с той же обработкой). `current == null`: `Active` → `Missing`, `Unavailable` → без события. `current != null`: `Unavailable` → `Returned`; другое `owner/name` (`OrdinalIgnoreCase`) → `Renamed`; оба события возможны вместе.
6. **Порядок:** новые по `FullName` (`OrdinalIgnoreCase`), записи — в порядке файла.

## 6. Тесты

`RepositoryDiscoveryServiceTests` (NSubstitute для `IGitHubApi`, настоящий `ExclusionMatcher`, `NullLogger`):

- Сбор: org → `GetOrganizationRepositoriesAsync`, user → `GetUserRepositoriesAsync`; дубль ID; пустой конфиг → `Empty`.
- Новый в аккаунте (в т.ч. другой регистр owner, форк, archived); новый вне аккаунтов из `/user/repos`; не новый при известном ID.
- Исключения подавляют оба класса новых; запись, исключённая под своим именем, не вызывает API.
- Пропавший (404, 403, через `GetRepositoryByIdAsync`); доступный вне списков — без событий.
- `Unavailable`: всё ещё 404 — без событий; найден — `Returned`.
- Переименованный: в списке, перенос к другому owner, редирект, имя занято другим ID; новое имя не попадает в новые; отличие только в регистре — не переименование; переименован и был `Unavailable` — оба события.
- Сбои API при сборе уходят наверх; rate limit при проверке записи уходит наверх.
- `--account`: другие аккаунты не запрашиваются, новые вне аккаунтов не попадают, неизвестный аккаунт → `ArgumentException` без запросов.
- Отмена → `OperationCanceledException`.

`DiscoveryResultTests`: `HasChanges`. DI: сервис разрешается как singleton.

## 7. Проверка

| Пункт | Как проверить |
|---|---|
| Каждый класс с подменным `IGitHubApi` | `RepositoryDiscoveryServiceTests` |
| Дубли, исключения, фильтр, сбои API | тот же класс |
| Регистрация в DI | `CoreServiceCollectionExtensionsTests` |
| CI green | `build-runner` |

## 8. Вне рамок

- Применение решений, статусы, переименование папок, запись конфига — #23.
- Wiki — #24.
- Вызов из orchestrator, `--repo`, разбор `--account`, коды возврата — #25.

## 9. Решения

- Анонимный режим (FR-1.8): классификация буквальная по FR-2.4 — приватная запись, недоступная без токена, попадает в `Missing`. С токеном она вернётся как `Returned`.
- Переименование в исключённое имя сообщается как `Renamed`; дальше orchestrator пропускает запись с предупреждением (FR-1.6).
- Запись, исключённая под своим настроенным именем, не проверяется и не даёт событий.
- Повторно недоступная запись `Unavailable` не даёт событий.
- Отдельный репозиторий, чей owner — отслеживаемый аккаунт, считается известным.
- `404` у `GetAccountAsync` — сбой обнаружения (уходит наверх).
- Сравнение имён без учёта регистра, чтобы регистр в URL не давал ложное переименование.

## 10. Задачи

- [x] Типы результата: `TrackedRepository`, `NewAccountRepository`, `RenamedRepository`, `ReturnedRepository`, `DiscoveryResult`
- [x] `IRepositoryDiscoveryService`
- [x] `RepositoryDiscoveryService` и `RepositoryDiscoveryServiceLog`
- [x] Регистрация в `AddGitHubBackupCore`
- [x] `RepositoryDiscoveryServiceTests`, `DiscoveryResultTests`, DI-тест
- [x] Сборка, тесты, `dotnet format --verify-no-changes`
