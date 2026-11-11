# План: #16 F1.9: Change detector: is a new archive needed

| Поле | Значение |
|---|---|
| Issue | [#16](https://github.com/askrinnik/GitHubBackup/issues/16) |
| Заголовок | F1.9: Change detector: is a new archive needed |
| Направление | Feature |
| Сложность | M |
| Дата | 2026-11-11 |

## 1. Цель

Реализовать `IChangeDetector`. Он решает по алгоритму FR-6.2 (§6.6), нужен ли новый архив репозитория или wiki, и попутно приводит клон в актуальное состояние: клонирует или обновляет его (FR-4.3). Новый архив нужен при любом изменении веток, тегов или ветки checkout (FR-6.3). Повторное клонирование без изменений на сервере архив не создаёт (П-13). Если ссылки и ветка не изменились, клон проверяется одним `ls-remote`, без fetch (NFR-5).

## 2. Критерии приёмки

- Unit tests for every branch of the algorithm
- Integration test: a change of a non-default branch and of a tag yields a new archive
- Covered by tests, CI green
- (добавлено) Клона нет, последний архив есть, на сервере изменений нет: клон создаётся заново, решение `UpToDate` (П-13).
- (добавлено) Архивов нет совсем (`LastArchive is null`): клон обновляется без `ls-remote`, решение «архивировать».
- (добавлено) Заглушка OneDrive (атрибуты `Offline`/`RecallOnDataAccess`) считается существующим файлом; детектор вызывает только `File.Exists` и не читает файл.
- (добавлено) Пустой репозиторий (`GitSyncResult.IsEmpty`) никогда не даёт решения «архивировать» (FR-4.5).
- (добавлено) Ветка в настройках не задана: веткой checkout считается ветка по умолчанию на сервере; её смена даёт новый архив.
- (добавлено) Запрошенной ветки нет на сервере: результат несёт `BranchFallback = true`, в том числе при `UpToDate` (FR-4.4).
- (добавлено) `GitBranchNotFoundException`, `GitCommandException` и `OperationCanceledException` проходят наружу без перехвата (NFR-3, NFR-4).

## 3. Изменения PRD

Не нужны.

## 4. Затрагиваемые проекты и типы

**GitHubBackup.Core**, папка и пространство имён `Changes`:

- `IChangeDetector` — `Task<ChangeDetection> DetectAsync(ChangeDetectionRequest request, CancellationToken cancellationToken)`.
- `ChangeDetectionRequest` — `sealed record`: `required GitRepositoryRequest Git`, `required string RemoteUrl`, `required string ArchiveFolder`, `ArchiveRecord? LastArchive`. Вызывающий передаёт `BackupState.GetLastArchive(target)`, поэтому детектор не различает репозиторий и wiki.
- `ChangeDetection` — `sealed record`: `ChangeDecision Decision`, `ChangeReason Reason`, `string? Branch`, `bool BranchFallback`, `RefSnapshot Refs`.
- `ChangeDecision` — `UpToDate`, `ArchiveNeeded`, `NothingToArchive`.
- `ChangeReason` — `NoArchive`, `ArchiveFileMissing`, `RefsChanged`, `BranchChanged`, `Unchanged`, `EmptyRepository`.
- `ChangeDetector` — `public sealed class ChangeDetector(IGitClient, IFileSystem, ILogger<ChangeDetector>)`.
- `ChangeDetectorLog` — `[LoggerMessage]`, одно событие на решение.
- `CoreServiceCollectionExtensions` — `TryAddSingleton<IChangeDetector, ChangeDetector>()`.
- `GitHubBackup.Core.csproj` — ссылка на `System.IO.Abstractions` (архитектурные правила её разрешают).

**GitHubBackup.Core.Tests** — `System.IO.Abstractions.TestingHelpers`, `Changes/ChangeDetectorTests`, тест регистрации.

**GitHubBackup.Infrastructure.IntegrationTests** — `Changes/ChangeDetectorTests` на `LocalGitSandbox` и настоящем `GitClient`.

Infrastructure, Cli и App не меняются.

## 5. Подход

```
last = request.LastArchive
archiveMissing = last is null || !File.Exists(ArchiveFolder\last.File)
cloneExists   = Directory.Exists(ClonePath\.git)

if !cloneExists         -> sync = CloneAsync          (п. 1, П-13)
else if archiveMissing  -> sync = UpdateAsync         (п. 2, без ls-remote)
else:
    remote = GetRemoteRefsAsync                       (п. 3)
    if remote == last.Refs и ожидаемая ветка == last.Branch -> UpToDate, без fetch
    sync = UpdateAsync                                (п. 4)
return Decide(sync, last, archiveMissing)
```

`Decide`: `IsEmpty` → `NothingToArchive`; `last is null` → `NoArchive`; файла нет → `ArchiveFileMissing`; снимок отличается → `RefsChanged`; ветка отличается (ordinal) → `BranchChanged`; иначе `UpToDate`.

Ожидаемая ветка в быстрой проверке определяется без fetch: заданная ветка, если она есть в `remote`; иначе ветка по умолчанию (`GetDefaultBranchAsync`), если она есть в `remote`, с `BranchFallback = Git.Branch is not null`; иначе быстрая проверка не делает вывода и идёт в п. 4.

Клон считается существующим, если есть `<clone>\.git`. Папка без `.git` даёт `CloneAsync`, который падает с `GitCommandException`; детектор клон не удаляет. `ArchiveFolder` должен быть полным путём, иначе `ArgumentException`. Все исключения проходят наружу, токен в лог не попадает.

## 6. Тесты

**Unit** (`Core.Tests/Changes/ChangeDetectorTests`, NSubstitute `IGitClient`, `MockFileSystem`): клона нет × (нет архива, сервер не изменился, сервер изменился, файла архива нет); архивов нет; файла архива нет; заглушка OneDrive; ссылки и ветка не изменились; ветка не задана и не изменилась; смена ветки в настройках; смена ветки по умолчанию на сервере; запрошенной ветки нет → fallback; `[Theory]` изменений ссылок (выбранная ветка, другая ветка, добавление и удаление ветки, добавление, удаление и перенос тега); remote отличается, локальный снимок совпал; пустой репозиторий (обновление и клон); проброс `GitBranchNotFoundException` и отмены; неполный `ArchiveFolder`. Плюс тест регистрации в `CoreServiceCollectionExtensionsTests`.

**Интеграционные** (`Infrastructure.IntegrationTests/Changes/ChangeDetectorTests`): продвинута ветка не по умолчанию → архив; тег создан или перенесён → архив; сервер не изменился → `UpToDate` без `fetch` (проверка по логу `ProcessRunner`); удалённая папка клона без изменений на сервере → клон без архива (П-13); пустой репозиторий → `NothingToArchive`.

## 7. Проверка

| Критерий | Как проверяется |
|---|---|
| Unit-тесты на каждую ветку алгоритма | `Core.Tests` `ChangeDetectorTests` |
| Интеграция: ветка не по умолчанию и тег | `Infrastructure.IntegrationTests` `ChangeDetectorTests` |
| П-13, без fetch, пустой репозиторий | интеграционные тесты выше |
| Заглушка OneDrive | `DetectAsync_ArchiveIsCloudPlaceholder_CountsAsPresent` |
| CI зелёный | полная сборка, тесты решения, `dotnet format --verify-no-changes` |

## 8. Вне рамок

- Вызов детектора из оркестратора: выбор `BackupTarget`, загрузка `BackupState`, статусы в сводке, запись `ArchiveRecord` (FR-6.4).
- Пропуск wiki без git-репозитория (FR-5.2).
- Архивация (#17) и токен (#18).
- Восстановление папки клона без `.git`.
- Свойство `HeadCommit` в `ChangeDetection`.
- Проверка на настоящем OneDrive — только вручную.

## 9. Решения

- Если ветка не задана, быстрая проверка делает дополнительный `ls-remote --symref`, чтобы заметить смену ветки по умолчанию на GitHub. Альтернатива — сравнивать только ветку из настроек и пропускать такую смену до следующего изменения ссылок.
- Детектор сам клонирует или обновляет клон и возвращает ветку и снимок; оркестратор после него `UpdateAsync` не вызывает. Это совпадает с FR-6.2, где обновление клона — часть алгоритма.
- Core обращается к диску через `IFileSystem` (`System.IO.Abstractions`), как требует CLAUDE.md; архитектурные правила этот пакет для Core разрешают. Альтернатива — свой узкий интерфейс проверки существования файла.

## 10. Задачи

- [x] Пакеты `System.IO.Abstractions` (Core) и `System.IO.Abstractions.TestingHelpers` (Core.Tests)
- [x] `ChangeDecision`, `ChangeReason`, `ChangeDetectionRequest`, `ChangeDetection`, `IChangeDetector`
- [x] `ChangeDetector` и `ChangeDetectorLog`
- [x] Регистрация в `AddGitHubBackupCore`
- [x] Unit-тесты и тест регистрации
- [x] Интеграционные тесты
- [x] Сборка, тесты, `dotnet format --verify-no-changes`
- [x] Проверка добавленных комментариев
