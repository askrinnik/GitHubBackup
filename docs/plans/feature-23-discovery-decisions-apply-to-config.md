# План: #23 «F2.4: Discovery decisions and applying them to the config»

| | |
|---|---|
| Issue | [#23](https://github.com/askrinnik/GitHubBackup/issues/23) |
| Название | F2.4: Discovery decisions and applying them to the config |
| Lane | Feature (`area:core`, `area:infrastructure`) |
| Сложность | L |
| Дата | 2026-11-11 |

PRD: §6.2 (FR-2.4, FR-2.5, FR-2.6, FR-2.8), §6.3 (FR-3.1–FR-3.5), §7.3, П-12, §9 (шаги 5–6).

## 1. Цель

В `GitHubBackup.Core` появляется стратегия `IDiscoveryDecisionHandler`. Через неё хост принимает решения по новым (FR-2.6) и переименованным (FR-2.5) репозиториям. Реализации стратегии для CLI и WPF будут в фазах 3 и 4.

Новый сервис `IDiscoveryDecisionApplier` применяет к `BackupConfig` результат `DiscoveryResult` (#22) и решения стратегии:
- **новый репозиторий:** добавить, пропустить или исключить;
- **пропавший:** выставить `Unavailable`;
- **вернувшийся:** выставить `Active` и записать уведомление (FR-2.4);
- **переименованный:** вариант «а» или «б» для папки архивов, перенос клона и wiki-клона под новое имя с новым remote URL; если перенос не удался, старый клон удаляется и клонируется заново (П-12).

Конфиг сохраняется через существующий `IBackupConfigStore.SaveAsync`: запись атомарная, предыдущая версия уходит в `.bak` (FR-2.8).

## 2. Критерии приёмки

Из issue:
1. Тесты применения каждого решения к модели настроек.
2. Интеграционный тест переноса клона при переименовании.
3. Покрыто тестами, CI зелёный.

Добавлено (по PRD и по сверке с кодом):

4. (добавлено, FR-2.4) Для пропавшего репозитория ничего не удаляется: клон, архивы и `.backup-state.json` остаются. Вернувшийся репозиторий снова получает `Active`, в лог пишется уведомление `Information`, и он попадает в результат применения.
5. (добавлено, FR-2.5, П-12) Если клон не удалось перенести, старый клон удаляется, включая read-only объекты git. Новый клон создаёт обычный шаг бэкапа того же запуска: `ChangeDetector` клонирует, когда `<new>\.git` нет. Интеграционный тест проверяет всю цепочку.
6. (добавлено, FR-2.8) Конфиг сохраняется только через `IBackupConfigStore.SaveAsync`. Если изменений нет (только «пропустить» или пустой результат), `SaveAsync` не вызывается и лишний `.bak` не появляется.
7. (добавлено, FR-2.6) Стратегия получает предложение для каждого нового репозитория:
   - атрибуты `GitHubRepository`: URL, owner, видимость, fork, archived, ветка по умолчанию, wiki;
   - аккаунт (или `null` для отдельного репозитория);
   - предлагаемую запись: имя, ID, `branch = null`, итоговую папку архивов из `IRepositoryPathResolver`.
8. (добавлено, сверка с кодом) Сначала собираются все решения, потом выполняются действия. Отмена во время выбора (`OperationCanceledException`) не меняет ни модель, ни диск.
9. (добавлено) «Исключить» добавляет маску без дублей (`OrdinalIgnoreCase`):
   - для репозитория аккаунта — `name` в `exclude` аккаунта или `owner/name` в глобальный `exclude`;
   - для отдельного репозитория — только глобальный `exclude`; выбор `Account` даёт `ArgumentException`.
10. (добавлено, FR-2.5) Перенос в другой аккаунт:
    - новый owner — отслеживаемый аккаунт: запись переходит в список этого аккаунта;
    - иначе запись переходит в группу отдельных репозиториев с `url`;
    - у отдельной записи обновляется `url`.
11. (добавлено) Вариант «а» не может переименовать папку архивов (целевая папка уже есть, другой том, `IOException`). Тогда применяется вариант «б»: старая папка записывается в `path`, в лог пишется предупреждение. Если старой папки нет, обновляется только имя. Если у записи уже есть явный `path`, папка архивов не трогается в обоих вариантах.
12. (добавлено) Сбой `SaveAsync` откатывает уже выполненные переименования папок архивов (best effort) и пробрасывается. Клоны до этого момента не трогаются.
13. (добавлено, NFR-3) Сбой переноса клона одного репозитория не прерывает остальные. Он логируется и попадает в результат; наверх уходит только отмена.

## 3. Изменения PRD

Не нужны. Повторное клонирование выполняет обычный шаг бэкапа в том же запуске (§9, шаг 8; механизм тот же, что в П-13). Это реализация FR-2.5, а не новое поведение.

## 4. Затрагиваемые проекты и типы

**GitHubBackup.Core**

`Discovery/`:
- `IDiscoveryDecisionHandler` — стратегия:
  - `Task<NewRepositoryDecision> DecideNewAsync(NewRepositoryProposal, CancellationToken)`;
  - `Task<RenameDecision> DecideRenameAsync(RenameProposal, CancellationToken)`.
- `NewRepositoryProposal(GitHubRepository Repository, string? AccountLogin, string ArchiveFolder)` — предложение по FR-2.6.
- `NewRepositoryDecision` (record) — `Action` (`NewRepositoryAction`), `Branch?`, `Path?`, `ExclusionScope?`. Фабрики `Add(branch, path)`, `Skip`, `Exclude(scope)`. Переиспользуется существующий `ExclusionScope`.
- `NewRepositoryAction` (enum) — `Add`, `Skip`, `Exclude`.
- `RenameProposal(RenamedRepository Renamed, string CurrentArchiveFolder, string RenamedArchiveFolder)` — текущая папка архивов и папка после варианта «а».
- `RenameDecision` (enum) — `RenameArchiveFolder` (а), `KeepArchiveFolder` (б).
- `IDiscoveryDecisionApplier` / `DiscoveryDecisionApplier` (`sealed`). Зависимости: `IBackupConfigStore`, `IRepositoryPathResolver`, `ICloneRelocator`, `IFileSystem`, `ILogger<…>`. Метод `ApplyAsync(BackupConfig config, DiscoveryResult discovery, IDiscoveryDecisionHandler handler, string gitPath, CancellationToken)` возвращает `DiscoveryApplyResult`.
- `DiscoveryApplyResult` — `Added`, `Skipped`, `Excluded`, `MarkedUnavailable`, `Reactivated`, `Renamed` (список `RenameOutcome`), `ConfigSaved`.
- `RenameOutcome(string OldFullName, string NewFullName, RenameDecision Requested, RenameDecision Applied, CloneRelocationResult Clone, CloneRelocationResult? Wiki)`.
- `ICloneRelocator` / `CloneRelocator` (`sealed`). Зависимости: `IFileSystem`, `IGitClient`, `ILogger<…>`. Метод `RelocateAsync(string gitPath, string oldFolder, string newFolder, string remoteUrl, CancellationToken)` возвращает `CloneRelocationResult`.
- `CloneRelocationResult` (enum) — `NotPresent`, `Moved`, `DeletedForReclone`, `DeleteFailed`.
- `DiscoveryDecisionApplierLog`, `CloneRelocatorLog` (`[LoggerMessage]`):
  - Warning — недоступен, fallback на «б», перенос не удался, удаление не удалось;
  - Information — вернулся, добавлен, исключён, переименован, перенесён.

Другие папки Core:
- `Git/IGitClient` — новый `SetRemoteUrlAsync(GitRepositoryRequest request, string remoteUrl, CancellationToken)`: `git remote set-url origin <url>`.
- `GitHub/IRemoteUrlProvider`, `GitHubRemoteUrlProvider` — новый `GetWikiUrl(owner, name)`: `https://github.com/<owner>/<name>.wiki.git`, с той же проверкой имён.
- `CoreServiceCollectionExtensions` — `TryAddSingleton` для `IDiscoveryDecisionApplier` и `ICloneRelocator`.

**GitHubBackup.Infrastructure**
- `Git/GitClient.SetRemoteUrlAsync`:
  - проверка `ClonePath`;
  - `remoteUrl` не пустой и не начинается с `-`, иначе `ArgumentException`;
  - аргументы списком `["remote", "set-url", "origin", remoteUrl]`, рабочая папка — клон, окружение без токена.
- `Git/GitClientLog` — сообщение о смене URL; URL проходит через маскировщик секретов, как в `CloneAsync`.

**Тестовые проекты:** `Core.Tests`, `Infrastructure.IntegrationTests` (раздел 6).

## 5. Подход

1. **Core, контракты.** Записи предложений и решений, стратегия, `GetWikiUrl`, `SetRemoteUrlAsync` в `IGitClient`.

2. **`CloneRelocator.RelocateAsync`:**
   - `oldFolder` не существует → `NotPresent`.
   - `oldFolder` — reparse point → не трогать, Warning, `DeleteFailed` (правило `BackupOrchestrator`).
   - Иначе: создать родителя `newFolder`, затем `Directory.Move(old, new)`. Оба клона под одним `sourcesRoot`, поэтому перемещение — это переименование.
   - Целевая папка уже есть → неудачный перенос.
   - После переноса: `SetRemoteUrlAsync(newFolder, remoteUrl)`. Если git упал (`GitCommandException`, `ProcessStartException`), удалить перенесённый клон → `DeletedForReclone`.
   - Перенос не удался (`IOException`, `UnauthorizedAccessException`) → удалить старый клон: снять `ReadOnly` с файлов, затем удалить рекурсивно → `DeletedForReclone`. Если удаление не удалось → Warning, `DeleteFailed`. Наверх уходит только отмена.

3. **`DiscoveryDecisionApplier.ApplyAsync`:**
   1. **Сбор решений** без побочных эффектов: `NewInAccounts`, `NewStandalone`, `Renamed` в порядке результата.
   2. **План переименований:** целевое размещение (тот же аккаунт / другой отслеживаемый аккаунт / отдельный) и папки архивов до и после.
   3. **Вариант «а»:** если папки различаются, старая существует, а новой нет — перенос с созданием родителя; при неудаче — «б». Если старой папки нет, обновляется только имя. Выполненные переносы запоминаются для отката.
   4. **Мутация модели:** добавления, маски исключений, `Missing` → `Unavailable`, `Returned` → `Active`, переименования (имя или `url`, перенос записи между списками, `path` при «б»).
   5. **Сохранение:** если есть изменения → `SaveAsync`; при исключении — откат переносов папок архивов в обратном порядке, затем `throw`. После сбоя переданный `config` считается испорченным (`<remarks>`).
   6. **Клоны:** только после успешного сохранения — `RelocateAsync` для клона и wiki-клона.

4. **Почему так:**
   - решения собираются до действий, поэтому отмена в диалоге безопасна;
   - архивы переносятся до сохранения и откатываются при сбое, поэтому конфиг не указывает на пустую папку;
   - клоны трогаются после сохранения, поэтому сбой записи не оставит клоны под именем, которого нет в конфиге;
   - повторное клонирование не дублирует логику clone: её уже реализует `ChangeDetector`, а ошибки изолирует оркестратор (NFR-3).

5. **Безопасность:** папки строятся только через `IRepositoryPathResolver`, URL — только через `IRemoteUrlProvider`; токен в `SetRemoteUrlAsync` не передаётся; удаление клона только в fallback переименования; для пропавших и вернувшихся нет ни одного удаления.

## 6. Тесты

**`GitHubBackup.Core.Tests`**

`Discovery/DiscoveryDecisionApplierTests` (`MockFileSystem`, NSubstitute для `IBackupConfigStore`, `ICloneRelocator`, `IDiscoveryDecisionHandler`):
- новый в аккаунте: Add с веткой и путём / с `null`; Skip без сохранения; Exclude `Account` и `Global`; маска уже есть — без дубля;
- новый отдельный: Add → `RepositoryConfig` с `url`; Exclude `Global`; Exclude `Account` → `ArgumentException`;
- предложение FR-2.6 содержит папку архивов по умолчанию;
- `Missing` → `Unavailable`, файлы не тронуты, relocator не вызван;
- `Returned` → `Active`; `Returned` и `Renamed` вместе;
- переименование: «б»; «а»; «а» без старой папки; «а» при существующей целевой папке → «б»; «а» при `IOException` → «б»; явный `path` не трогается;
- перенос владельца: в другой отслеживаемый аккаунт; к неотслеживаемому owner; отдельная запись;
- порядок: `RelocateAsync` после `SaveAsync` для клона и wiki; сбой `SaveAsync` → откат папки архивов, relocator не вызван;
- пустой результат → без сохранения;
- отмена в стратегии → модель и диск без изменений.

`Discovery/CloneRelocatorTests` (`MockFileSystem`, NSubstitute `IGitClient`): нет папки; успех; целевая папка существует; `Move` бросает; удаление падает; `SetRemoteUrlAsync` бросает; reparse point; отмена.

Другие: `GitHubRemoteUrlProviderTests.GetWikiUrl`; `CoreServiceCollectionExtensionsTests` для новых сервисов.

**`GitHubBackup.Infrastructure.IntegrationTests`**

`Discovery/CloneRelocatorTests` (`LocalGitSandbox`, настоящие `GitClient` и `FileSystem`):
- `RelocateAsync_CloneExists_MovesCloneAndSetsNewRemoteUrl`;
- `RelocateAsync_MoveFails_DeletesOldCloneAndChangeDetectorReclones`;
- `RelocateAsync_WikiClone_MovesWikiFolder`.

`Git/GitClientTests`: `SetRemoteUrlAsync_ExistingClone_ChangesOrigin`, `SetRemoteUrlAsync_UrlStartsWithDash_Throws`, нефиксированный путь → `ArgumentException`.

## 7. Проверка

| Пункт | Чем проверяется |
|---|---|
| 1 | `DiscoveryDecisionApplierTests` |
| 2 | `CloneRelocatorTests.RelocateAsync_CloneExists_MovesCloneAndSetsNewRemoteUrl`, `…WikiClone…` (интеграционные) |
| 3 | `build-runner`: сборка, тесты, `dotnet format --verify-no-changes`; зелёный CI в PR |
| 4 | `ApplyAsync_Missing_*`, `ApplyAsync_Returned_*` |
| 5 | `CloneRelocatorTests` (unit) и интеграционный `…MoveFails_DeletesOldCloneAndChangeDetectorReclones` |
| 6 | `ApplyAsync_*` проверяют вызов `SaveAsync` и его отсутствие; атомарность и `.bak` покрыты `JsonBackupConfigStoreSaveTests` |
| 7–13 | соответствующие кейсы `DiscoveryDecisionApplierTests` и `CloneRelocatorTests` |

## 8. Вне рамок

- Реализации `IDiscoveryDecisionHandler`: CLI (интерактив и `--silent`) — фаза 3; диалог WPF — фаза 4.
- Вызов обнаружения и применения решений из `BackupOrchestrator` (§9, шаги 5–6), `--repo`, `--account`, dry-run, коды возврата — #25.
- Бэкап wiki (FR-5.x) — #24. Здесь только перенос существующего wiki-клона и `GetWikiUrl`.
- Межпроцессная блокировка записи конфига (NFR-SYS-1) — фаза 3.
- Если целевая папка клона уже существовала (устаревший кэш), она не удаляется.

## 9. Решения

Пользователь разрешил начать реализацию без отдельного ревью плана, если нет открытых вопросов. Решения по умолчанию:
1. Повторное клонирование после неудачного переноса выполняет шаг бэкапа того же запуска (`ChangeDetector`), а не `CloneRelocator`.
2. Если вариант «а» не удался, автоматически применяется «б» с предупреждением.
3. Для отдельного репозитория «исключить» возможно только в глобальный `exclude`.
4. Пропавшие и вернувшиеся репозитории применяются автоматически, без вопроса стратегии. «Сообщает» и «уведомление» — это лог и списки в `DiscoveryApplyResult`, которые хост покажет в фазах 3 и 4.
5. Перенос репозитория к неотслеживаемому owner переводит запись в группу отдельных репозиториев.
6. (по итогам security review) Если папка под новым именем уже существует, `CloneRelocator` ничего не удаляет и не переносит и возвращает `CloneRelocationResult.TargetOccupied` с предупреждением: в папке может лежать клон другого репозитория. Старый клон удаляется только при сбое самого переноса.
7. (по итогам security review) Имя репозитория с суффиксом `.wiki` недопустимо, как и с `.git`: wiki-клон репозитория `x` лежит в папке `x.wiki`, и имена совпали бы.

## 10. Задачи

- [x] Core: `GetWikiUrl` в `IRemoteUrlProvider` / `GitHubRemoteUrlProvider` и тесты
- [x] Core: `IGitClient.SetRemoteUrlAsync`; Infrastructure: `GitClient.SetRemoteUrlAsync`, `GitClientLog`; интеграционные тесты в `GitClientTests`
- [x] Core: `NewRepositoryProposal`, `NewRepositoryDecision`, `NewRepositoryAction`, `RenameProposal`, `RenameDecision`, `IDiscoveryDecisionHandler`
- [x] Core: `ICloneRelocator`, `CloneRelocator`, `CloneRelocationResult`, `CloneRelocatorLog` и unit-тесты
- [x] Core: `IDiscoveryDecisionApplier`, `DiscoveryDecisionApplier`, `DiscoveryApplyResult`, `RenameOutcome`, `DiscoveryDecisionApplierLog` и тесты
- [x] Core: регистрация в `AddGitHubBackupCore` и тест DI
- [x] IntegrationTests: `Discovery/CloneRelocatorTests` (перенос, fallback с повторным клонированием, wiki)
- [x] Сборка, тесты, `dotnet format --verify-no-changes`; проверка новых `///`-комментариев
