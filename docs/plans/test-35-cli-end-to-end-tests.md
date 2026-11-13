# План: F3.10 — CLI end-to-end tests

| Поле | Значение |
|---|---|
| Issue | [#35](https://github.com/askrinnik/GitHubBackup/issues/35) — F3.10: CLI end-to-end tests |
| Ветка процесса | Test-authoring (метка `type:test`) |
| Сложность | L |
| Дата | 2026-11-13 |

## 1. Цель

Нужны сквозные тесты собранного `GitHubBackup.Cli.exe`, запущенного как отдельный процесс ([PRD §11](../PRD.md#11-тестирование), строка «CLI end-to-end»). Тесты проверяют поведение на границе процесса: код возврата, stdout и stderr, файлы на диске. Покрываются коды возврата FR-9.10 (`0/1/2/3`) и параметры `--silent` (FR-9.4), `--dry-run` (FR-9.5), `--repo`, `--account` и их взаимоисключение (FR-9.2), а также перенаправленный ввод (FR-9.6). Git и 7-Zip работают по-настоящему на локальных bare-репозиториях. Адрес git подменяется на `file://`, GitHub API отдаёт WireMock на loopback. Тесты входят в основной CI (§11, абзац CI).

## 2. Критерии приёмки

Из issue (дословно):

1. Тесты входят в основной CI
2. Покрыто тестами, CI зелёный

Добавлено по проверке покрытия:

3. (добавлено) Код `0`: настоящие clone и архив на диске, файл состояния создан, stderr пуст. Токен не попадает в stdout, stderr и `.git/config` клона (NFR-1).
4. (добавлено) Код `1`: у одного репозитория нет удалённого репозитория, второй архивируется (FR-9.9).
5. (добавлено) Код `3` в `--silent`: новый репозиторий автоматически добавлен в `backup-config.json`, склонирован и заархивирован. Процесс завершается, хотя stdin перенаправлен и не закрыт: тихий режим не ждёт ввода.
6. (добавлено) Код `2`: (а) запуск без `--silent`/`--dry-run` с перенаправленным вводом даёт подсказку в stderr и не трогает диск (FR-9.6); (б) `--account` вместе с `--repo`; (в) повреждённый `backup-config.json`; (г) отсутствующий 7-Zip (`--Tools:SevenZipPath=`).
7. (добавлено) `--dry-run` ничего не меняет: при первом запуске нет клона, архива, `.backup-state.json`, `.tmp`, а `backup-config.json` побайтно не изменился. При повторном запуске после нового коммита на сервере ссылки клона, число архивов и файл состояния прежние, в выводе есть `WouldArchive`.
8. (добавлено) `--repo owner/a`: архивируется только `a`. У `b` нет ни клона, ни архива. Новый репозиторий из `/user/repos` в конфиг не попадает, потому что обнаружения нет.
9. (добавлено) `--account acme`: архивируются только репозитории аккаунта `acme`. Отдельный репозиторий конфига не трогается.

## 3. Затронутые проекты и типы

Только `GitHubBackup.Cli.Tests`, продуктовый код не меняется:

- `GitHubBackup.Cli.Tests.csproj`: элемент `<AssemblyMetadata Include="GitHubBackupCliOutputPath" Value="…" />` с путём к выходной папке проекта Cli.
- новый `CliInstallation`: временная папка (через `TemporaryFolder`) с копией выхода Cli без `logs`, `data`, `sources` и `backup-config.json` разработчика. Свой `appsettings.json` строится из `CliApplicationTests.ValidAppSettings` с адресом WireMock. Папка служит content root exe.
- новый `CliProcess` / `CliProcessResult(int ExitCode, string StandardOutput, string StandardError)`: запуск через `ProcessStartInfo.ArgumentList`, все три потока перенаправлены, stdout и stderr читаются параллельно, таймаут связан с `TestContext.Current.CancellationToken`, при срабатывании дерево процессов убивается. Окружение:
  - `DOTNET_ENVIRONMENT=Production` — user secrets разработчика не читаются;
  - унаследованные `GitHubBackup__*` удалены;
  - `GIT_CONFIG_GLOBAL=<файл>` с `[url "file:///<sandbox>/remotes/"] insteadOf = https://github.com/`;
  - `GIT_CONFIG_NOSYSTEM=1`.
- новый `GitHubApiStub` (обёртка над `WireMockServer`): `/user/repos`, `/repos/{o}/{n}`, `/repositories/{id}`, `/users/{login}`, `/users/{login}/repos` или `/orgs/{login}/repos`, запасной ответ 404.
- новый `BackupConfigJson`: построитель `backup-config.json`.
- новый `CliEndToEndTests`, `[Trait("Category", "EndToEnd")]`.
- новый `BackupRunLockTestGroup`: `[CollectionDefinition(Name, DisableParallelization = true)]`.
- изменён `BackupHistoryEndToEndTests`: `[Collection(BackupRunLockTestGroup.Name)]`, переход на `GitHubApiStub` и `BackupConfigJson`.

Из `GitHubBackup.TestUtilities` переиспользуются `LocalGitSandbox`, `LocalGitRemote` и `TemporaryFolder`.

## 4. Подход

- **Подмена git-адреса без изменения продукта.** `GitHubRemoteUrlProvider` строит `https://github.com/{owner}/{name}.git`. `ProcessRunner` наследует окружение родителя и убирает только `GitHubBackup__*`, а `GitEnvironment` не трогает `GIT_CONFIG_GLOBAL`. Поэтому `url.<file-url>.insteadOf` в глобальном конфиге теста перенаправляет clone, fetch и ls-remote на bare-репозиторий. В `.git/config` клона остаётся `https://github.com/...`.
- **API.** `GitHub:ApiBaseUrl` указывает на `GitHubApiStub`. Токен берётся из `appsettings.json` установки, поэтому цепочка токена не идёт ни в Credential Manager, ни в `gh`.
- **Глобальный мьютекс.** Exe берёт `Global\GitHubBackup.BackupRun`. Внутрипроцессные `BackupHistoryEndToEndTests` и новые тесты идут в одной непараллельной коллекции.
- **stdin всегда перенаправлен**, поэтому FR-9.6 проверяется настоящим `Console.IsInputRedirected`. В тихом режиме stdin не закрывается; тест проверяет, что процесс завершается сам.
- **Утверждения.** Главное — код возврата, stderr и файлы. Из stdout проверяются только статусы (`Archived`, `WouldClone`, `WouldArchive`) через `ShouldContain`. Архивы и клоны ищутся поиском по папкам, пути не прописываются вручную.
- **CI.** `ci.yml` запускает все тесты решения, кроме `Category=Smoke`, на `windows-latest`; git и 7-Zip там есть. Категория `EndToEnd` уже входит в прогон, правка workflow не нужна.

## 5. Тесты

Класс `CliEndToEndTests` (`Category=EndToEnd`, коллекция `BackupRunLockTestGroup`). У каждого теста свои `LocalGitSandbox`, `CliInstallation` и `GitHubApiStub`.

| Тест | Сценарий | Ожидание |
|---|---|---|
| `Backup_SilentTrackedRepository_ExitsSuccessWithCloneArchiveAndState` | `owner/a` в конфиге, в `/user/repos` и на сервере; `backup --silent` | `0`; один `*.zip`; клон с `.git`; `.backup-state.json`; stderr пуст; токена нет в stdout, stderr и `.git/config` |
| `Backup_SilentOneRemoteMissing_ExitsWithFailuresAndArchivesTheOther` | у `owner/b` нет bare-репозитория | `1`; архив `a` есть, архива `b` нет |
| `Backup_SilentNewRepositoryWithOpenInput_ExitsWithDiscoveryEventsAndAddsIt` | новый `owner/new` в `/user/repos`; stdin открыт | `3`; `owner/new` в `backup-config.json`; архив `new` есть |
| `Backup_InteractiveWithRedirectedInput_ExitsCriticalWithHintAndTouchesNothing` | `backup` без флагов | `2`; подсказка в stderr; нет клона и архива; конфиг не изменён |
| `Backup_CriticalArguments_ExitsCriticalWithErrorAndNoArchive` (`[Theory]`) | `--account acme --repo owner/a`; `--repo not-a-repo`; `--Tools:SevenZipPath=<несуществующий>` | `2`; stderr непуст; нет архивов |
| `Backup_InvalidBackupConfig_ExitsCriticalWithErrorAndLogPath` | битый JSON в `backup-config.json` | `2`; строка ошибки и строка с путём к журналу под `<installation>\logs` |
| `Backup_DryRunBeforeFirstBackup_ChangesNothingOnDisk` | `owner/a` отслеживается плюс новый репозиторий; `--dry-run` | `3`; `WouldClone`; нет клона, `*.zip`, `.backup-state.json`, `.tmp`; конфиг побайтно прежний |
| `Backup_DryRunAfterRemoteCommit_ChangesNothingOnDisk` | `--silent`, коммит на сервер, `--dry-run` | `0`; `WouldArchive`; число архивов, `HEAD` клона и `.backup-state.json` прежние |
| `Backup_SilentRepoFilter_BacksUpOnlyThatRepository` | `owner/a`, `owner/b`, новый в `/user/repos`; `--silent --repo owner/a` | `0`; архив только `a`; конфиг без нового |
| `Backup_SilentAccountFilter_BacksUpOnlyThatAccount` | аккаунт `acme` с `acme/x` и отдельный `owner/a`; `--silent --account acme` | `0`; архив `acme/x`; у `owner/a` ни клона, ни архива |

История запусков, разбор аргументов и сводка уже проверены в `BackupHistoryEndToEndTests`, `CliApplicationTests` и `BackupSummaryRendererTests`; новые тесты их не повторяют.

## 6. Проверка

| Пункт приёмки | Как проверяется |
|---|---|
| 1 | Прогон решения без `Category=Smoke`, как в CI; `CliEndToEndTests` в прогоне CI pull request; CI зелёный |
| 2 | Сборка, тесты проекта `GitHubBackup.Cli.Tests`, `dotnet format --verify-no-changes` |
| 3–9 | Тесты из таблицы раздела 5 |

## 7. Вне рамок

- Интерактивные вопросы в настоящей консоли (FR-9.3): без TTY непроверяемо; покрыто `ConsoleDiscoveryDecisionHandlerTests`.
- Ctrl+C в дочернем процессе (NFR-4): на Windows надёжно не отправить; отмену покрывают тесты оркестратора.
- Код `2` из-за занятой блокировки запуска.
- `set-token` и `validate` как процесс.
- `timeout-minutes` и закрепление действий по SHA в `ci.yml` — отдельная задача.

## 8. Решения

1. **Exe** запускается из копии выхода проекта Cli во временной папке теста. Путь к выходу задаёт `AssemblyMetadata`. Так раскладка совпадает с продуктом, а `logs\` изолированы.
2. **Подмена адреса** через `GIT_CONFIG_GLOBAL` и `url.insteadOf` выполняет требование PRD «адрес git подменяется на file-URL» без изменения продукта.
3. **Непараллельная коллекция** для внутрипроцессных и новых сквозных тестов. Запущенный на машине GitHubBackup во время тестов даёт код `2`; это принято.
4. **Общие помощники** `GitHubApiStub` и `BackupConfigJson`; `BackupHistoryEndToEndTests` переходит на них.

Решения 1–4 приняты по рекомендациям плана: пользователь разрешил переходить к реализации без отдельного ревью плана, если нет вопросов.

## 9. Задачи

- [x] `AssemblyMetadata` `GitHubBackupCliOutputPath` в `GitHubBackup.Cli.Tests.csproj`
- [x] `CliInstallation`
- [x] `CliProcess` / `CliProcessResult`
- [x] `GitHubApiStub` и `BackupConfigJson`
- [x] `BackupRunLockTestGroup`; `BackupHistoryEndToEndTests` в коллекции и на общих помощниках
- [x] `CliEndToEndTests`: сценарии раздела 5
- [x] Сборка, тесты, `dotnet format --verify-no-changes`
- [x] Проверить `README.md` на описание состава тестов
