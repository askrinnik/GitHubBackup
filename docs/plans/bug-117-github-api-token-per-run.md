# План: GitHub API keeps the token resolved at the first call for the whole process

| | |
|---|---|
| Issue | [#117](https://github.com/askrinnik/GitHubBackup/issues/117) |
| Заголовок | GitHub API keeps the token resolved at the first call for the whole process |
| Lane | Bug |
| Сложность | M |
| Дата | 2026-11-15 |

Требования: FR-11.1, FR-11.5, FR-12.5 (а также FR-1.8, NFR-1, FR-10.6).

## Причина

`OctokitGitHubApi` получает токен через `ITokenProvider` при первом вызове (`IsAnonymousAsync`), ставит его в `GitHubClient.Credentials` и больше не спрашивает провайдер (`_credentialsResolved`). Тест `GetRepositoryAsync_TwoCalls_ResolvesTokenOnce` закрепляет это поведение. `BackupOrchestrator` спрашивает `ITokenProvider` на каждый запуск и передаёт результат только git. В долгоживущем UI-процессе после смены токена git получает новый токен, а API — старый (или работает анонимно).

## Подход

Токен запуска один, и его определяет оркестратор. API получает тот же токен явно, а не спрашивает провайдер сам.

```
BackupOrchestrator.RunAsync
  └─ tokenProvider.GetTokenAsync  (токен запуска, регистрируется в ISecretMasker)
      └─ RunLockedAsync (под блокировкой запуска)
          ├─ gitHubApi.UseToken(token)   → Octokit Credentials / анонимный режим
          ├─ разрешение ID, обнаружение   → API с токеном запуска
          └─ git                          → тот же токен
```

- `IGitHubApi` (Core) получает метод `void UseToken(GitHubToken token)`: задаёт токен для следующих запросов. До первого вызова доступ анонимный.
- `OctokitGitHubApi` больше не зависит от `ITokenProvider`: `UseToken` ставит `Credentials` клиента (или `Credentials.Anonymous`) и флаг анонимного режима. Семафор и ленивое разрешение удаляются, `IDisposable` больше не нужен.
- `BackupOrchestrator.RunAsync` вызывает `gitHubApi.UseToken(token)` сразу после того, как получил блокировку запуска, и `UseToken(GitHubToken.Anonymous)` в `finally` после запуска. Поэтому параллельный запуск (который не получил блокировку) не меняет токен работающего запуска, а токен не остаётся в клиенте API после запуска (замечание S5 security review).
- `README.md`, раздел редактора настроек: вместо фразы о перезапуске — «следующий запуск использует сохранённый токен».
- Почему не спрашивать провайдер на каждый вызов API: источник `gh auth token` запускает процесс, а `TokenProvider` пишет запись в лог на каждый вызов. Кроме того, токен мог бы смениться посреди запуска, и git и API получили бы разные токены.
- Токен по-прежнему уходит только на `GitHub:ApiBaseUrl` (FR-11.5): адрес клиента и проверка ссылки на следующую страницу не меняются. В `ISecretMasker` токен регистрирует `TokenProvider` до того, как оркестратор его получает.
- `SettingsViewModel`: подсказка `RestartForTokenMessage` удаляется, статус после сохранения — `Token saved to <location>.` PRD подсказку о перезапуске для токена не описывает, правка PRD не нужна.

## Изменяемые файлы

- `src/GitHubBackup.Core/GitHub/IGitHubApi.cs` — метод `UseToken`.
- `src/GitHubBackup.Core/Runs/BackupOrchestrator.cs` — вызов `UseToken` в `RunLockedAsync`, документация параметров.
- `src/GitHubBackup.Infrastructure/GitHub/OctokitGitHubApi.cs` — без `ITokenProvider`, с `UseToken`.
- `src/GitHubBackup.Infrastructure.IntegrationTests/Runs/FakeGitHubApi.cs` — реализация `UseToken` (ничего не делает).
- `src/GitHubBackup.App/ViewModels/SettingsViewModel.cs` — без подсказки о перезапуске.

## Тесты

- `OctokitGitHubApiTests` (WireMock):
  - `UseToken_TokenChanged_NextRequestSendsNewTokenAndNeverLogsEither` — токен A, запрос, токен B, запрос: второй запрос несёт B; ни один токен не попадает в лог.
  - `UseToken_TokenThenAnonymousThenToken_SwitchesAuthorizationOnNextRequest` — заголовок `Authorization` исчезает и появляется.
  - `GetCurrentUserRepositoriesAsync_AnonymousThenToken_RequestsOnlyWithToken` и `GetCurrentUserRepositoriesAsync_TokenThenAnonymous_SkipsWithoutRequest` — анонимный режим (FR-1.8) меняется на следующем вызове.
  - `GetRepositoryAsync_NoTokenSet_SendsNoAuthorizationHeader` — без вызова `UseToken` запросы анонимные.
  - Тест `GetRepositoryAsync_TwoCalls_ResolvesTokenOnce` удаляется (он закреплял дефект); тест `AnyMethod_InvalidInput_…` больше не проверяет провайдер.
- `Core.Tests/BackupOrchestratorTests`:
  - `RunAsync_TokenChangesBetweenRuns_GivesEachRunItsTokenForGitHubApiAndGit` — провайдер возвращает токен, затем анонимный режим; API получает их по порядку, git каждого запуска получает тот же токен.
  - `RunAsync_Token_GivesGitHubApiTheRunTokenForDiscoveryAndDropsItAfterTheRun` — токен задаётся до обнаружения и сбрасывается после запуска.
  - `RunAsync_LockHeld_…` — при занятой блокировке `UseToken` не вызывается.
- `App.Tests/SettingsViewModelTests`: статус после сохранения без подсказки о перезапуске.

## Вне рамок

- `OctokitGitHubTokenInspector` уже получает токен на каждую проверку, не меняется.
- Пути к инструментам по-прежнему действуют после перезапуска (FR-12.5).

## Решения

- Пользователь разрешил пройти workflow до конца без ревью плана; ревью — в pull request.
- Токен передаётся в API явно (`IGitHubApi.UseToken`), а не запрашивается на каждый вызов.
- По итогам security review токен сбрасывается в анонимный режим после каждого запуска.

## Задачи

- [x] Воспроизвести: дефект закреплён тестом `GetRepositoryAsync_TwoCalls_ResolvesTokenOnce`
- [x] `IGitHubApi.UseToken`, `OctokitGitHubApi`, `FakeGitHubApi`
- [x] `BackupOrchestrator` вызывает `UseToken`
- [x] Подсказка о перезапуске удалена из `SettingsViewModel`
- [x] Тесты
- [x] Сборка, тесты, формат
