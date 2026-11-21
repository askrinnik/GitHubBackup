# План: #167 F3.23 — короткие описания внешних команд и запросов GitHub API, подробности на уровне Debug

| | |
|---|---|
| Issue | [#167](https://github.com/askrinnik/GitHubBackup/issues/167) F3.23: Short descriptions of external commands and GitHub API requests, details at Debug |
| Лейн | Feature |
| Сложность | L |
| Дата | 2026-11-21 |

## 1. Цель

Перед каждым запуском внешней программы (git, git-lfs, 7-Zip, GitHub CLI, Explorer, включая проверку версий при старте) программа пишет короткое английское описание на уровне `Information`. У записи два свойства: `Tool` и `Description`. Командная строка с окружением, строки stdout/stderr и успешное завершение уходят на уровень `Debug`. Перед каждым запросом к GitHub API программа тоже пишет описание на уровне `Information`, а ресурс, длительность и остаток лимита — на уровне `Debug`. Значения по умолчанию: файлы получают `Debug` программы, остальные приёмники — `Information`. Для записей `Microsoft.*` и `System.*` без правила `Override` действует нижняя граница `Information`. Требования: FR-10.4 (новая редакция), FR-10.7 (дополнение), новое FR-10.8, §7.2.

## 2. Критерии приёмки

Из issue, дословно:

1. PRD содержит текст из раздела «Изменение PRD»; версия PRD повышена, строка в истории изменений добавлена
2. Каждый запуск внешней программы пишет описание на уровне `Information` со свойствами `Tool` и `Description` до командной строки
3. Командная строка с окружением, строки stdout/stderr и успешное завершение пишутся на уровне `Debug`; ненулевой код, отмена и ошибка остановки — `Warning`; секреты замаскированы
4. Все места вызова `IProcessRunner` задают описание; описания не содержат веток, путей и URL
5. Каждый запрос к GitHub API пишет описание на уровне `Information`; ресурс, длительность и лимит — на уровне `Debug`
6. По умолчанию файлы получают записи `Debug` программы, а консоль CLI, живая лента UI и OTLP — только описания; записи `Debug` библиотек `Microsoft.*` и `System.*` в файлы не попадают
7. Ключами FR-10.7 полный вывод включается для любого приёмника и выключается для файлов
8. README описывает описания, подробности, значения по умолчанию и примеры перенастройки
9. Покрыто тестами (уровни и порядок записей `ProcessRunner`, описания мест вызова, запросы GitHub API, значения по умолчанию приёмников), CI зелёный

Добавлено по проверке покрытия:

10. (добавлено) `"Serilog:Endpoints:File:Level": null` в `appsettings.json` и пустое значение из переменной окружения означают `Serilog:MinimumLevel:Default`. Отсутствующий ключ означает `Debug`.
11. (добавлено) Пустое или пробельное `ProcessRequest.Description` отклоняется с `ArgumentException` до запуска процесса.
12. (добавлено) Предупреждение «Output abandoned» остаётся на уровне `Warning`. Маскирование командной строки, окружения и строк вывода не меняется. Строки stdout с `IsStandardOutputSecret` по-прежнему не пишутся в лог.
13. (добавлено) Если токена нет и `GetCurrentUserRepositoriesAsync` пропускает запрос, описание не пишется. Список из нескольких страниц даёт одно описание на операцию.
14. (добавлено) Если запрос к API завершился ошибкой, описание уже записано, а запись `Debug` о завершении не пишется. Тип исключения и сообщение не меняются.
15. (добавлено) Если ключа `File:Level` нет, редактор настроек (FR-12.5) показывает для `File` значение `Debug`.

## 3. Затрагиваемые проекты и типы

**GitHubBackup.Core**
- `Processes/ProcessRequest` — обязательное свойство `required string Description`. `LogLevel` задаёт уровень описания (по умолчанию `Information`). Новое свойство `DetailsLogLevel` задаёт уровень подробностей (по умолчанию `Debug`).
- `Processes/IProcessRunner` — документация: описание до запуска, подробности на уровне `DetailsLogLevel`, `ArgumentException` для пустого описания.
- `Logging/LogProperties` — константа `Description`.

**GitHubBackup.Infrastructure**
- `Processes/ProcessRunner` — проверяет `Description`; после `Validate` пишет запись-описание (с маскированием для защиты в глубину). `Starting`, `OutputLine` и `Exited` при коде 0 — на `DetailsLogLevel`.
- `Processes/ProcessRunnerLog` — запись `Describing` (EventId 7, уровень параметром, шаблон `"{Tool}: {Description}"`).
- `Git/GitCommandDescriptions` (новый, `internal static`) — константы описаний команд git.
- `Git/GitClient` — у `RunGitAsync` и `RunGitProcessAsync` параметр `LogLevel logLevel` заменяется обязательным `string description`. Каждый вызов передаёт константу. Шаги `SyncWorkingTreeAsync` получают описание. Три `LogLevel.Debug` у `ls-remote` убираются.
- `Archiving/SevenZipArchiver` — `RunAsync` получает описание.
- `Authentication/GitHubCliTokenSource` — `Description` в запросе.
- `Tools/ToolLocator` — `RunVersionAsync` получает описание; `LogLevel = LogLevel.Trace` убирается.
- `GitHub/OctokitGitHubApi` и `OctokitGitHubApiLog` — `Requesting` (EventId 4, Information) до операции и `RequestCompleted` (EventId 5, Debug, `Resource`, `Duration`) после успеха; время через `TimeProvider.GetTimestamp()`. `RateLimit` и `RateLimitLow` не меняются.
- `GitHub/OctokitGitHubTokenInspector` и новый `OctokitGitHubTokenInspectorLog` — логгер, описание, `RequestCompleted`, `RateLimit` (Debug).
- `Logging/EndpointLevelResolver` — нижняя граница `Information` для источников `Microsoft` и `System` (и их namespace), если не подошло ни одно правило `Override`. `SerilogOptions.Override` по умолчанию не меняется.
- `Logging/SerilogEndpointsOptions` — константа `DefaultFileLevel = "Debug"`.
- `Logging/FileLoggingServiceCollectionExtensions` — `PostConfigure<SerilogEndpointsOptions>`: если в `Serilog:Endpoints:File` нет дочернего ключа `Level`, ставится `Debug`.

**GitHubBackup.App**
- `Shell/ExplorerLauncher` — описания для `OpenFolderAsync` и `ShowFileAsync`.
- `SettingsViewModel`, `JsonLogLevelSettingsStore` — без изменений кода: `Current` видит `Debug` после `PostConfigure`.

**Документы:** `docs/PRD.md` (текст из issue, 0.55, строка истории), `README.md`.

### Каталог описаний

| Место вызова | Команда | Description |
|---|---|---|
| `GitClient.CloneAsync` | `git clone --no-checkout …` | `Cloning the repository without a checkout` |
| `GitClient.FetchAsync` | `git fetch origin --prune --prune-tags --tags --force` | `Fetching branches and tags from the server` |
| `GitClient.GetRemoteRefsAsync(request)` | `git ls-remote --symref origin HEAD refs/heads/* refs/tags/*` | `Reading the branches, tags and default branch of the server` |
| `GitClient.GetRemoteRefsAsync(request, remoteUrl)` | `git ls-remote --refs -- <url> …` | `Reading the branches and tags of the server without a clone` |
| `GitClient.ReadDefaultBranchAsync` | `git ls-remote --symref origin HEAD` | `Reading the default branch of the server` |
| `GitClient.SetRemoteUrlAsync` | `git remote set-url origin <url>` | `Pointing origin at the repository URL` |
| `GitClient.GetOriginUrlAsync` | `git config --get remote.origin.url` | `Reading the origin URL of the clone` |
| `GitClient.GetWorkingCopyStatusAsync` | `git --no-optional-locks status --porcelain=v2 …` | `Checking the working copy for changes and untracked files` |
| `GitClient.GetUnpushedBranchCommitsAsync` | `git rev-parse --verify --quiet refs/heads/<b>` | `Checking whether the local branch exists` |
| `GitClient.CurrentRefsCountArgumentsAsync` | `git rev-parse --verify --quiet <tracking ref>` | `Checking whether the remote-tracking branch exists` |
| `GitClient.CountUnpushedCommitsAsync` | `git rev-list --count …` | `Counting commits that are not on the server` |
| `GitClient.GetIgnoredPathsAsync` | `git ls-files -z --others --ignored --exclude-standard --directory` | `Listing ignored files` |
| `GitClient.ListRepositoryFoldersAsync` | `git ls-files -z --stage` | `Reading the index for submodules and symbolic links` |
| `GitClient.ReadLocalRefsAsync` | `git for-each-ref …` | `Reading the branches and tags of the clone` |
| `GitClient.SyncWorkingTreeAsync` | `git checkout -B <b> origin/<b> --` | `Switching the working tree to the target branch` |
| ↑ | `git reset --hard origin/<b> --` | `Resetting the working tree to the server state` |
| ↑ | `git clean -ffdx` | `Removing untracked and ignored files` |
| ↑ | `git clean -ffd` | `Removing untracked files and keeping ignored files` |
| ↑ | `git submodule sync --recursive` | `Synchronizing submodule URLs` |
| ↑ | `git submodule update --init --recursive --force` | `Checking out submodules` |
| ↑ | `git lfs pull` | `Downloading the LFS files of the checked-out branch` |
| `SevenZipArchiver.ArchiveAsync` | `7z a -tzip …` | `Creating the ZIP archive` |
| ↑ (`VerifyArchive`) | `7z t …` | `Testing the archive` |
| `GitHubCliTokenSource.GetTokenAsync` | `gh auth token --hostname github.com` | `Reading the token of the GitHub CLI` |
| `ToolLocator.LocateAsync` | `git version` | `Checking the git version` |
| ↑ | `git lfs version` | `Checking the Git LFS version` |
| ↑ | `7z` | `Checking the 7-Zip version` |
| `ExplorerLauncher.OpenFolderAsync` | `explorer <folder>` | `Opening a folder in Explorer` |
| `ExplorerLauncher.ShowFileAsync` | `explorer /select,<file>` | `Showing a file in Explorer` |
| `OctokitGitHubApi.GetAccountAsync` | `GET users/{login}` | `Requesting the account of an owner` |
| `OctokitGitHubApi.GetCurrentUserRepositoriesAsync` | `GET user/repos` | `Requesting the repositories of the authenticated user` |
| `OctokitGitHubApi.GetOrganizationRepositoriesAsync` | `GET orgs/{org}/repos` | `Requesting the repositories of an organization` |
| `OctokitGitHubApi.GetUserRepositoriesAsync` | `GET users/{login}/repos` | `Requesting the repositories of a user` |
| `OctokitGitHubApi.GetRepositoryAsync` | `GET repos/{owner}/{name}` | `Requesting a repository by owner and name` |
| `OctokitGitHubApi.GetRepositoryByIdAsync` | `GET repositories/{id}` | `Requesting a repository by its id` |
| `OctokitGitHubTokenInspector.InspectAsync` (токен есть) | `GET user` | `Checking the GitHub token` |
| `OctokitGitHubTokenInspector.InspectAsync` (без токена) | `GET rate_limit` | `Checking the anonymous rate limit` |

Заголовки операций остаются: `GitClientLog.Cloning`, `Updating`, `SevenZipArchiverLog.Creating`, `Verifying`. Записи, которая повторяла бы описание один в один, нет. `GitClientLog.SettingRemoteUrl`, `ToolLocatorLog.ToolFound` и `TokenSourceLog.*` сообщают путь, URL или результат и остаются.

## 4. Подход

1. **PRD.** Скопировать текст из `## Изменение PRD` в `docs/PRD.md`; версия 0.55, дата, строка истории.
2. **Core.** `ProcessRequest`, `LogProperties.Description`, документация `IProcessRunner`. `required` находит все места вызова.
3. **ProcessRunner.** Порядок: описание (`LogLevel`) → `Starting` (`DetailsLogLevel`) → строки (`DetailsLogLevel`) → `Exited` (`DetailsLogLevel` при коде 0, иначе `Warning`). `Cancelled`, `KillFailed`, `OutputAbandoned` — `Warning`. Описание пишется после `Validate`.
4. **Места вызова** по каталогу. Прежние `Debug` (`ls-remote`) и `Trace` (`ToolLocator`) убираются.
5. **GitHub API.** Описание и `RequestCompleted` в `OctokitGitHubApi.ExecuteAsync` и `OctokitGitHubTokenInspector`. Одно описание на операцию. Логин и имя репозитория не входят в описание; `Resource` — только в записи `Debug`.
6. **Значения по умолчанию.** `EndpointLevelResolver` поднимает уровень записей `Microsoft.*` и `System.*` без правила `Override` до `Information`; правило `Override` снимает границу. `File:Level` по умолчанию ставит `PostConfigure` по наличию ключа, чтобы `null` по-прежнему означал `MinimumLevel:Default`.
7. **Тесты** (раздел 5).
8. **README.**

## 5. Тесты

**GitHubBackup.Infrastructure.Tests**
- `ProcessRunnerTests`: пустое описание → `ArgumentException`.
- `GitClientTests`: описания по операциям; описания без ветки, пути, URL и токена; уровни по умолчанию для `ls-remote`.
- `SevenZipArchiverTests`, `GitHubCliTokenSourceTests`, `ToolLocatorTests`: описания.
- `OctokitGitHubApiTests`: `Requesting` (Information) и `RequestCompleted` (Debug) для каждого метода; пагинация → одно описание; ошибка → без `RequestCompleted`; анонимный пропуск → без описания.
- `OctokitGitHubTokenInspectorTests`: описания с токеном и без, `RequestCompleted`, `RateLimit`, токена нет в записях.
- `FileLoggingServiceCollectionExtensionsTests`: `File.Level` по умолчанию `Debug`; `null` и пустая строка → `MinimumLevel:Default`.
- `EndpointLevelResolverTests`: нижняя граница для `Microsoft.*` и `System.*`; правило `Override` снимает границу; `MicrosoftFoo` без точки не затрагивается.
- `JsonLogLevelSettingsStoreTests`: `Current.File == Debug` без секции.

**GitHubBackup.Infrastructure.IntegrationTests**
- `ProcessRunnerLoggingTests`: порядок и уровни записей, `Debug` выключен, явные уровни, отмена, ошибка остановки, секретный stdout; хост-тесты приёмки 6 и 7.
- `SerilogFileLoggingTests`: существующие тесты с явным `File:Level = null`; новый тест конфигурации по умолчанию.
- `AppSettingsHostTests`: без `appsettings.json` → `File == Debug`.

**GitHubBackup.App.Tests**
- `ExplorerLauncherTests`: описания.
- `SettingsViewModelTests`: store с `File = Debug` → выбран пункт `Debug`.

## 6. Проверка

| Пункт | Как проверяется |
|---|---|
| 1 | Ревью diff `docs/PRD.md` |
| 2, 3, 11, 12 | `ProcessRunnerLoggingTests`, `ProcessRunnerTests` |
| 4 | Тесты описаний мест вызова; компилятор (`required`) |
| 5, 13, 14 | `OctokitGitHubApiTests`, `OctokitGitHubTokenInspectorTests` |
| 6 | Хост-тест в `ProcessRunnerLoggingTests`, тест в `SerilogFileLoggingTests` |
| 7 | Хост-тест «`LiveLog` Debug, `File` Information» |
| 8 | Ревью README |
| 10 | `FileLoggingServiceCollectionExtensionsTests` |
| 15 | `JsonLogLevelSettingsStoreTests`, `AppSettingsHostTests`, `SettingsViewModelTests` |
| 9 | Сборка, тесты, формат, CI |

## 7. Вне объёма

- Explorer завершается с кодом 1, и это даёт `Warning` при открытии папки; поведение было и раньше.
- Описание и запись `Debug` на каждую страницу ответа API.
- Изменения UI редактора настроек.
- Удаление `ProcessRequest.LogLevel`.
- Новые ключи конфигурации.

## 8. Решения

Пользователь разрешил реализацию без отдельного ревью плана, если нет вопросов. Открытые вопросы закрыты по рекомендациям:

1. Каталог описаний принят как в таблице раздела 3.
2. `GitClientLog.SettingRemoteUrl` остаётся.
3. Для списков с пагинацией — одно описание на операцию.
4. Описание пишется после проверки путей: при отсутствующем exe будет только `ProcessStartException`.
5. `ProcessRequest.LogLevel` остаётся как уровень описания, как требует issue.
6. Нижняя граница `Information` для записей `Microsoft.*` и `System.*` без правила `Override` заменяет `Override` по умолчанию `Microsoft: Information` и `System: Information`. Причина: глобальный `Override` побеждает уровень приёмника, и записи `Information` библиотек проходили бы в приёмник с уровнем `Warning`. Решение принял пользователь во время реализации; текст PRD одобрен.

## 9. Задачи

- [x] Скопировать «Изменение PRD» в `docs/PRD.md`, версия 0.55, дата, строка истории
- [x] `ProcessRequest`: `required Description`, `DetailsLogLevel`; документация; `LogProperties.Description`
- [x] `ProcessRunner` и `ProcessRunnerLog`: проверка описания, `Describing`, подробности на `DetailsLogLevel`
- [x] `GitCommandDescriptions` и `GitClient`
- [x] Описания в `SevenZipArchiver`, `GitHubCliTokenSource`, `ToolLocator`, `ExplorerLauncher`
- [x] `OctokitGitHubApi` и `OctokitGitHubApiLog`
- [x] `OctokitGitHubTokenInspector` и `OctokitGitHubTokenInspectorLog`
- [x] Нижняя граница для библиотек в `EndpointLevelResolver`
- [x] `PostConfigure` для `File:Level`; `SerilogEndpointsOptions.DefaultFileLevel`
- [x] Обновить сломанные тесты
- [x] Новые тесты по разделу 5
- [x] README
- [x] Сборка, тесты, формат; гигиена комментариев
