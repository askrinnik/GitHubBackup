# План: #24 «F2.5: Wiki backup»

- Issue: [#24](https://github.com/askrinnik/GitHubBackup/issues/24) — F2.5: Wiki backup
- Lane: Feature
- Сложность: L
- Дата: 2026-11-11

## 1. Цель

Для каждого активного репозитория с включённой wiki (`has_wiki` в API) оркестратор обрабатывает `https://github.com/<owner>/<repo>.wiki.git` в папке `<sourcesRoot>\<owner>\<repo>.wiki` по тем же правилам, что и репозиторий: клонирование или обновление, определение изменений, архивация, запись состояния. Ветка всегда та, что на сервере по умолчанию. Если wiki включена, но её git-репозитория нет, wiki пропускается: в лог пишется отметка, статуса `Failed` нет. Архив wiki называется `<owner>_<repo>.wiki_<ts>_<hash>.zip`, лежит в папке архивов репозитория и записывается в секцию `wiki` файла `.backup-state.json`. Решение об архивации wiki принимается независимо от репозитория. Требования: FR-5.1–FR-5.4, FR-3.5, FR-6.1, FR-6.2, FR-7.2, §9 п. 8.

Уже готово и переиспользуется:
- `IRemoteUrlProvider.GetWikiUrl`;
- `IRepositoryPathResolver.GetWikiCloneFolder`;
- `BackupTarget.Wiki` в `ArchiveNameBuilder` (суффикс `.wiki`);
- `BackupState.Wiki` и `GetLastArchive`/`Append` по `BackupTarget`;
- `SevenZipArchiver` берёт корневую папку архива из имени `SourceFolder`, то есть `<repo>.wiki\` (FR-7.2).

## 2. Критерии приёмки

1. Интеграционный тест: wiki архивируется независимо от репозитория.
2. Отсутствующая wiki не даёт `Failed`.
3. Покрыто тестами, CI зелёный.
4. (добавлено) Если `has_wiki = false`, wiki не обрабатывается: нет git-команд, нет элемента результата.
5. (добавлено) Для репозитория `Unavailable` (Skipped) или исключённого маской wiki тоже не обрабатывается.
6. (добавлено) Wiki клонируется на ветке сервера по умолчанию: `Branch = null`, ветка из конфигурации к wiki не применяется (FR-5.1).
7. (добавлено) Ошибка wiki (кроме «не существует») даёт отдельный элемент `Failed` и не меняет статус репозитория; `Failed` репозитория не отменяет обработку wiki (FR-9.9, FR-5.4).
8. (добавлено) Если wiki уже была склонирована, а на сервере её больше нет, это тоже пропуск без `Failed`. Клон и архивы остаются.
9. (добавлено) Сбой запроса метаданных в API (`GitHubApiException`) даёт `Failed` у элемента wiki, а не у репозитория.

## 3. Затрагиваемые проекты и типы

**GitHubBackup.Core**
- `Git/GitCommandException` — снять `sealed`.
- `Git/GitRepositoryNotFoundException` (новый) — наследник `GitCommandException`: сервер сообщил, что репозитория по URL нет.
- `Git/IGitClient`, `Changes/IChangeDetector` — `<exception cref="GitRepositoryNotFoundException">` для `CloneAsync`, `UpdateAsync`, `GetRemoteRefsAsync`, `GetDefaultBranchAsync`, `DetectAsync`.
- `Runs/BackupWorkItem` — поле `WikiCloneFolder`, заполняется в `CreateAll` через `pathResolver.GetWikiCloneFolder`.
- `Runs/BackupItemResult` — свойство `BackupTarget Target { get; init; } = BackupTarget.Repository`. У wiki `Repository` = `owner/repo.wiki`.
- `Runs/BackupOrchestrator` — зависимость `IGitHubApi`; шаги параметризуются целью, после каждого репозитория обрабатывается его wiki.
- `Runs/BackupOrchestratorLog` — `WikiDisabled` (Debug), `WikiNotFound` (Information, FR-5.2), `WikiLookupFailed` (Error).

**GitHubBackup.Infrastructure**
- `Git/GitClient.RunGitAsync` — при коде 128 и маркере отсутствия репозитория в stderr бросается `GitRepositoryNotFoundException`. Сообщение и маскирование те же.

Хосты (`Cli`, `App`) не меняются.

## 4. Подход

**Источник `has_wiki`.** В `backup-config.json` флага нет (§7.3); источник — API. Оркестратор получает флаг из `IGitHubApi.GetRepositoryAsync(owner, name)` в приватном `IsWikiEnabledAsync(item, ct)`. #25 добавит первым источником метаданные, собранные обнаружением; запрос к API останется запасным путём для `--repo`.

**«Wiki не существует».** Код возврата git один — 128; stderr только уточняет вид ошибки. Маркеры:
- `Repository not found.` — ответ сервера GitHub при HTTP 404 (не локализуется);
- `does not appear to be a git repository` — ответ git на локальный путь или `file://` (для тестов без сети).

Для самого репозитория это исключение остаётся обычным `Failed`.

**Оркестратор:**
1. `BackupWorkItem.WikiCloneFolder`.
2. Обобщить `BackupAsync` и `ArchiveAsync` по цели: `BackupTarget`, папка клона, URL remote, ветка (`item.Branch` или `null`).
3. В `RunAsync` после результата репозитория вызвать `ProcessWikiAsync(item, ...)`, возвращающий `BackupItemResult?`: `null` для неактивного репозитория, при `has_wiki = false`, при `GitRepositoryNotFoundException`; иначе результат с `Target = Wiki`. Свой log scope `owner/repo.wiki`, своя длительность; отмена пробрасывается.
4. Файл состояния читается дважды; запись wiki через `AppendAsync`, который перечитывает файл.

## 5. Тесты

- `Core.Tests/Runs/BackupOrchestratorTests`: по умолчанию `HasWiki = false`; случаи `WikiEnabledAndChanged_ArchivesWikiSeparately`, `WikiDisabled_ProcessesRepositoryOnly`, `WikiNotFound_SkipsWithoutResultAndLogs`, `WikiFails_ReportsWikiFailedAndKeepsRepositoryArchived`, `RepositoryFails_StillProcessesWiki`, `WikiUpToDate_UsesWikiLastArchive`, `RepositoryUnavailable_DoesNotQueryWiki`, `WikiLookupFails_ReportsWikiFailed`, `RepositoryNotFoundForRepository_ReportsFailed`, `CancelledDuringWiki_Throws`.
- `Core.Tests/Runs/BackupWorkItemTests`: `CreateAll` заполняет `WikiCloneFolder`.
- `Infrastructure.Tests/Git/GitClientTests`: `[Theory]` классификации (оба маркера → новое исключение; другой текст или код 1 → `GitCommandException`; маскирование).
- `Infrastructure.IntegrationTests/Git/GitClientTests`: clone и ls-remote несуществующего `file://` remote.
- `Infrastructure.IntegrationTests/Runs/BackupOrchestratorTests`: `WikiChangesAlone_ArchivesOnlyWiki` (приёмка 1, проверка корневой папки ZIP), `WikiMissing_DoesNotFail` (приёмка 2).

## 6. Вне рамок

- Обнаружение в оркестраторе и метаданные из него — #25.
- Dry-run для wiki, запись wiki в историю SQLite (FR-8.3), UI-представление wiki.
- Принудительная английская локаль git.

## 7. Решения

Приняты на ревью плана:

1. **Источник `has_wiki`** — `IGitHubApi.GetRepositoryAsync` на каждый активный репозиторий. Без токена действует лимит 60 запросов в час; при его исчерпании элемент wiki получает `Failed`. Правка PRD не нужна.
2. **Отсутствующая wiki** — только запись в лог уровня Information (FR-5.2), без элемента результата.
3. **Локализация git** — окружение git не меняется. Серверный маркер `Repository not found.` не локализуется; при проблемах с локальным маркером заводится отдельная задача.
4. **Wiki при `Failed` репозитория** — обрабатывается всё равно (FR-5.4).
5. **Имя wiki в сводке** — `owner/repo.wiki`.

## 8. Задачи

- [x] Core: снять `sealed` с `GitCommandException`, добавить `GitRepositoryNotFoundException`.
- [x] Core: `<exception>` в `IGitClient` и `IChangeDetector`.
- [x] Infrastructure: классификация в `GitClient.RunGitAsync`.
- [x] Infrastructure.Tests: `[Theory]` классификации.
- [x] IntegrationTests: `GitClientTests` для несуществующего remote.
- [x] Core: `BackupWorkItem.WikiCloneFolder`; тесты `BackupWorkItemTests`.
- [x] Core: `BackupItemResult.Target`.
- [x] Core: `BackupOrchestrator` — `IGitHubApi`, обобщение по цели, `IsWikiEnabledAsync`, `ProcessWikiAsync`.
- [x] Core: сообщения в `BackupOrchestratorLog`.
- [x] Core.Tests: новые случаи оркестратора.
- [x] IntegrationTests: `BackupOrchestratorTests` для wiki.
- [x] Сборка, тесты, формат; проверка комментариев.
