# План: #80 Split GitClientTests into several classes to run integration tests in parallel

| | |
|---|---|
| Issue | [#80](https://github.com/askrinnik/GitHubBackup/issues/80) |
| Название | Split GitClientTests into several classes to run integration tests in parallel |
| Лейн | Test-authoring |
| Сложность | S |
| Дата | 2026-11-11 |

## 1. Цель

xUnit запускает классы параллельно, а тесты одного класса — друг за другом. `Git/GitClientTests` содержит 31 тест (с учётом случаев `[Theory]`) и идёт около 86 секунд. Это критический путь прогона `GitHubBackup.Infrastructure.IntegrationTests`. Задача — разбить класс на несколько классов по темам. Сценарии и проверки не меняются. Код продукта не меняется. PRD не меняется ([§11](../PRD.md#11-тестирование)).

## 2. Критерии приёмки

Из issue, дословно:
- [ ] `GitClientTests` разбит на классы, ни один из них не идёт дольше примерно 30 секунд
- [ ] Число тестов в проекте и их проверки не изменились
- [ ] Прогон `Infrastructure.IntegrationTests` заметно быстрее; замер до и после указан в комментарии к issue
- [ ] Покрыто тестами, CI зелёный

## 3. Замер «до»

`dotnet test --project src/GitHubBackup.Infrastructure.IntegrationTests/… --no-build --report-xunit-trx`, сумма длительностей по классам из TRX:

| Класс | Тестов | Сумма, с | Самый долгий, с |
|---|---|---|---|
| `GitClientTests` | 31 | 85,5 | 8,6 |
| `ChangeDetectorTests` | 7 | 25,0 | 4,8 |
| `CloneRelocatorTests` | 4 | 17,1 | 5,1 |
| `LocalGitRemoteTests` | 11 | 15,1 | 3,4 |

Проект: 102 теста, 1 мин 26,6 с по сводке раннера, 87,1 с по часам.

## 4. Подход

Общая подготовка уходит в абстрактный базовый класс `Git/GitClientTestBase`. Тела тестов переносятся без изменений: они и дальше вызывают `CreateClient()`, `Request()`, `GitAsync()`, `HeadAsync()`, `RefsAsync()` и поля `_sandbox`, `_clonePath`, `_runnerLogger`, `_clientLogger`.

```
GitClientTestBase (abstract, IDisposable)
  песочница, логгеры, папка клона, CreateClient, Request, GitAsync, HeadAsync, RefsAsync
        |
        +-- GitClientCloneTests            клонирование, ошибки клона, папка без .git
        +-- GitClientUpdateTests           обновление: новый коммит, force-push, локальные изменения, refs
        +-- GitClientRefSnapshotTests      снимки refs сервера и клона
        +-- GitClientBranchTests           ветка по умолчанию и откат на неё
        +-- GitClientEmptyRepositoryTests  пустой репозиторий
        +-- GitClientSubmoduleTests        сабмодули
        +-- GitClientTokenTests            токен не попадает в конфиги и лог
        +-- GitClientRemoteUrlTests        SetRemoteUrlAsync
```

Базовый класс реализует шаблон `Dispose(bool)`: анализаторы (`latest-recommended`, CA1063) требуют его для незапечатанного класса. Члены, которые нужны тестам, становятся `protected`. Наследники `sealed`.

Распределение и оценка по замеру «до» (сумма в секундах, последовательно внутри класса):

| Класс | Тесты | Случаев | Оценка, с |
|---|---|---|---|
| `GitClientCloneTests` | `CloneAsync_RemoteWithBranchesAndTags_…`, `CloneAsync_NonDefaultBranch_…`, `CloneAsync_PathLongerThanMaxPath_…`, `CloneAsync_RemoteMissing_…`, `Operation_ClonePathWithoutGitInsideAnotherRepository_…` | 8 | 11 |
| `GitClientUpdateTests` | `UpdateAsync_NewCommitOnServer_…`, `UpdateAsync_ServerHistoryForcePushed_…`, `UpdateAsync_LocalChanges_…`, `UpdateAsync_BranchDeletedAndTagsChanged_…` | 4 | 19 |
| `GitClientRefSnapshotTests` | `Snapshots_OneServerState_…`, `Snapshots_AnnotatedTag_…`, `Snapshots_ServerRefsChanged_…` | 3 | 11 |
| `GitClientBranchTests` | `GetDefaultBranchAsync_ServerHeadOnDevelop_…`, `CloneAsync_BranchNotSet_…`, `UpdateAsync_ServerDefaultBranchChanged_…`, `CloneAsync_BranchMissing_…`, `UpdateAsync_BranchDeletedOnServer_…`, `UpdateAsync_BranchAndServerHeadMissing_…` | 6 | 19 |
| `GitClientEmptyRepositoryTests` | `CloneAsync_EmptyRepository_…`, `UpdateAsync_EmptyRepository_…`, `UpdateAsync_FirstCommitPushedToEmptyRepository_…` | 3 | 3 |
| `GitClientSubmoduleTests` | `CloneAsync_NestedSubmodules_…`, `UpdateAsync_SubmoduleAdvanced_…` | 2 | 14 |
| `GitClientTokenTests` | `CloneAndUpdate_TokenSet_LeaveNoTokenInConfigsOrLog` | 1 | 6 |
| `GitClientRemoteUrlTests` | `SetRemoteUrlAsync_ExistingClone_…`, `SetRemoteUrlAsync_InvalidUrl_…` (2 случая), `SetRemoteUrlAsync_ClonePathNotFullyQualified_…` | 4 | 3 |
| **Итого** | | **31** | |

Самый долгий класс — около 19 с. После разбиения критическим путём станет `ChangeDetectorTests` (~25 с); он вне рамок. Ожидаемый прогон проекта — около 25–35 с. Под параллельной нагрузкой отдельные тесты идут дольше, чем в замере «до».

Мелочи:
- Константа токена нужна только `GitClientTokenTests` и переезжает туда.
- Над `SetRemoteUrlAsync_ExistingClone_ChangesOrigin` стоит ошибочный `/// <inheritdoc />` (тест ничего не наследует). Он удаляется; тестам XML-комментарий не нужен.
- Файл `GitClientTests.cs` удаляется.

## 5. Проверка

- `build-runner`, scope `full`: сборка, тесты решения, `dotnet format --verify-no-changes`.
- Число тестов `GitHubBackup.Infrastructure.IntegrationTests` — 102, как до разбиения.
- Сравнение имён тестов: множество `<метод>(<аргументы>)` из TRX «до» и «после» совпадает.
- Тела тестов совпадают с исходными (сравнение диффом без учёта перемещения).
- Замер «после» тем же способом; суммы по классам из TRX, ни один класс не дольше ~30 с.

## 6. Замер «после»

Тот же способ, два прогона подряд. Замер «до» в разделе 3 шёл без исключения папки `%TEMP%\GitHubBackup.Tests` из проверки Microsoft Defender, а прогоны «после» — уже с ним. Поэтому «до» перемерен с исключением на неизменном коде `origin/main` (временный detached worktree):

| Прогон | Defender-исключение | Тестов | По часам, с | `GitClientTests`, с |
|---|---|---|---|---|
| До | нет | 102 | 87,1 | 85,5 |
| До, 1 | есть | 102 | 96,5 | 94,9 |
| До, 2 | есть | 102 | 87,1 | 85,6 |
| После, 1 | есть | 102 | 39,6 | — |
| После, 2 | есть | 102 | 44,8 | — |

Исключение почти не меняет время «до»: прогон упирается в последовательный `GitClientTests`. Выигрыш даёт разбиение: примерно 87–96 с → 40–45 с.

Суммы по классам из TRX в параллельном прогоне завышены: классы делят процессор и диск, и каждый тест идёт в 1,5–2 раза дольше. Поэтому каждый новый класс измерен и отдельно (`--filter-class`), без соседей:

| Класс | Тестов | Отдельно, с | В общем прогоне 1, с |
|---|---|---|---|
| `GitClientUpdateTests` | 4 | 19,4 | 31,2 |
| `GitClientBranchTests` | 6 | 18,9 | 32,5 |
| `GitClientCloneTests` | 8 | 12,8 | 21,7 |
| `GitClientRefSnapshotTests` | 3 | 12,3 | 20,0 |
| `GitClientSubmoduleTests` | 2 | 12,0 | 21,9 |
| `GitClientTokenTests` | 1 | — | 11,7 |
| `GitClientEmptyRepositoryTests` | 3 | — | 7,2 |
| `GitClientRemoteUrlTests` | 4 | — | 6,5 |

Критический путь теперь — `ChangeDetectorTests` (38,2 с в общем прогоне 1). Он вне рамок.

## 7. Вне рамок

- Разбиение `ChangeDetectorTests` (по issue).
- Изменение сценариев, проверок или кода продукта.

## 8. Чек-лист

- [x] `GitClientTestBase` с общей подготовкой
- [x] Восемь классов по темам, `GitClientTests.cs` удалён
- [x] `build-runner` зелёный
- [x] Тот же набор тестов (102, имена совпадают)
- [ ] Замер «после» записан в план и в комментарий к issue
- [x] `README.md`: необязательный совет об исключении `%TEMP%\GitHubBackup.Tests` из проверки Microsoft Defender (добавлено по решению пользователя)

## Решения

- План утверждён без правок.
- После утверждения пользователь добавил в рамки задачи совет в `README.md` (раздел о тестах): исключение папки `%TEMP%\GitHubBackup.Tests` из проверки Microsoft Defender. Совет необязательный и не влияет на CI. Замер «до» шёл без исключения, а замер «после» — уже с ним. Поэтому «до» перемерен на неизменном коде `origin/main` с тем же исключением.
- Члены базового класса объявлены `private protected`: типы `GitClient` и `CapturingLogger<T>` внутренние, а тестовые классы xUnit публичные.
