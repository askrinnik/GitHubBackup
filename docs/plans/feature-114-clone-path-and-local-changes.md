# План: F2.9 — свой путь к клону и защита локальных изменений в клонах

| | |
|---|---|
| Issue | [#114](https://github.com/askrinnik/GitHubBackup/issues/114) F2.9: Custom clone path and protection of local changes in clones |
| Лейн | Feature |
| Сложность | L |
| Дата | 2026-11-14 |

## 1. Цель

Задача добавляет два свойства.

- У записи репозитория появляется необязательный ключ `clonePath`. Он задаёт абсолютный путь ко всей папке клона. Wiki лежит в `<clonePath>.wiki`. Если ключ задан, изменён или удалён, программа переносит клон на новое место.
- Перед любым действием, которое меняет рабочее дерево или ветки существующего клона (обновление FR-4.3, перенос по FR-3.10 и FR-2.5), программа проверяет клон. Правило действует для всех клонов: в `sourcesRoot` и в `clonePath`, для репозитория и для wiki. Если проверка не пройдена, элемент получает `Failed` с причиной, а клон остаётся нетронутым.

В клоне из `clonePath` игнорируемые файлы сохраняются (`git clean -ffd`) и не попадают в архив.

Требования: новые FR-3.10, FR-4.8, FR-4.9. Изменяются FR-2.5, FR-3.5, FR-3.6, FR-3.8, FR-3.9, FR-4.2, FR-4.3, FR-7.2, FR-9.5, §7.3, §9, П-10, П-12.

## 2. Критерии приёмки

Пункты из issue (дословно):
- PRD обновлён: §6.3, §6.4, §6.7, §7.3, FR-3.5, FR-4.3, FR-9.5, версия и строка истории
- Тест: запись с `clonePath` клонируется в указанную папку, wiki — в `<clonePath>.wiki`; запись без `clonePath` — по FR-3.5
- Тест: относительный `clonePath`, совпадающие папки клонов и вложенные папки клонов — ошибки валидации; `clonePath` внутри `backupRoot` — предупреждение
- Тест: смена и удаление `clonePath` переносят клон; занятая новая папка — `Failed`, ничего не удалено; `--dry-run` ничего не переносит
- Тест: каждая из четырёх причин (незакоммиченные изменения, неотслеживаемые файлы, другая ветка, неотправленные коммиты) даёт `Failed` с причиной, клон не изменён, архив не создан, код возврата `1`, остальные репозитории обработаны
- Тест: проверка работает для клонов в `sourcesRoot` и в `clonePath`, для репозитория и для wiki
- Тест: при непройденной проверке перенос клона (смена `clonePath`, переименование) не выполняется
- Тест: чистый клон на правильной ветке обновляется как раньше
- Тест: в `clonePath` игнорируемые файлы остаются после обновления и не попадают в архив; в `sourcesRoot` они удаляются, как раньше
- Тест: `--dry-run` показывает `Failed` с той же причиной и ничего не меняет
- README обновлён
- Покрыто тестами, CI зелёный

Добавлено по проверке покрытия:
- (доб.) Если на сервере нет изменений, быстрая проверка FR-6.2 даёт `UpToDate` даже при локальных изменениях. Клон при этом не меняется.
- (доб.) Отсоединённый HEAD считается «другой веткой». Если настроенной ветки нет на сервере, ожидается ветка по умолчанию (FR-4.4).
- (доб.) Папка клона существует, не пуста и без `.git`: элемент получает `Failed`, папка не меняется. Пустая папка клонируется.
- (доб.) `origin` существующего клона указывает не на URL репозитория: элемент получает `Failed` ещё до быстрой проверки. Существующая рабочая копия с правильным `origin` принимается как клон.
- (доб.) При переименовании клон из `clonePath` не переносится: меняется только URL remote. Клон из `clonePath` не удаляется ни при каком сбое.
- (доб.) Если запись переходит между списками (смена владельца), `clonePath` сохраняется.
- (доб.) `clonePath` в корне диска, равный `sourcesRoot`, внутри `sourcesRoot` или содержащий его — ошибка валидации.
- (доб.) Перенос между томами не выполняется: `Failed` с причиной, ничего не удалено.
- (доб.) `backup-config.json` пишет `clonePath` только тогда, когда ключ задан.
- (доб.) `validate` проверяет новые правила, потому что использует ту же загрузку.

## 3. Изменения требований (PRD 0.23 → 0.24)

Все правки — в `docs/PRD.md`.

- **Шапка:** `Версия документа` = `0.24 (черновик)`, `Дата` = `2026-11-14`.
- **FR-2.5**, абзац «Папка клона»: перед переносом выполняется проверка FR-4.8. Если она не пройдена, клон остаётся под старым именем и не удаляется; перенос повторяется при следующих запусках по FR-3.10. Если перенести не удалось, старый клон удаляется и клонируется заново (как раньше). Клон из `clonePath` не зависит от имени репозитория: он не переносится и только получает новый URL remote; если URL сменить не удалось, такой клон не удаляется.
- **FR-3.5:** правило действует, если у записи не задан `clonePath` (FR-3.10).
- **Новое FR-3.10. Свой путь к клону (`clonePath`).** Ключ записи репозитория (в `repositories` аккаунта и в отдельных `repositories`) — абсолютный путь ко всей папке клона. Wiki — `<clonePath>.wiki`. Игнорируемые файлы остаются при обновлении (FR-4.3), архив содержит только `.git` и отслеживаемые файлы (FR-7.2), папки `<clonePath>` и `<clonePath>.wiki` программа никогда не удаляет. Прежние папки клона и wiki хранятся в `<sourcesRoot>\.clone-locations.json` с ключом по GitHub ID; файл пишется атомарно в конце обработки и не пишется в проверочном прогоне. При смене папки клон переносится: сначала проверка FR-4.8; занятая цель или сбой переноса (например, разные тома) — `Failed`, ничего не удаляется; после переноса `origin` указывает на URL репозитория. Если файла нет или он не читается, прежняя папка неизвестна, старый клон остаётся на месте.
- **FR-3.6:** в список абсолютных путей добавляется `clonePath`.
- **FR-3.8:** предупреждение также для папок клона из `clonePath` и `<clonePath>.wiki` внутри `backupRoot` или папки архивов.
- **FR-3.9:** в дерево `<sourcesRoot>\` добавляется `.clone-locations.json`; второй пример раскладки для `clonePath`.
- **FR-4.2:** `git clone` выполняется, если папки нет или она пуста.
- **FR-4.3:** обновление только после успешной проверки FR-4.8; п. 4 — `git clean -ffd` для клона из `clonePath`; фраза «Локальные изменения при этом теряются» заменяется объяснением, почему работа пользователя не теряется.
- **Новое FR-4.8. Проверка клона перед изменением.** Четыре причины (незакоммиченные изменения, в том числе в сабмодулях; неотслеживаемые неигнорируемые файлы; другая ветка или отсоединённый HEAD; коммиты, которых нет в `origin/<ветка>`, а без неё — ни в одной `origin/*`). Только чтение, без fetch: ожидаемая ветка — через `git ls-remote`, неотправленные коммиты — по уже имеющимся `origin/*`. Исход: `Failed` с причиной, называющей папку и все проблемы; код `1`; остальные репозитории обрабатываются. Быстрая проверка FR-6.2 п. 3 клон не меняет, поэтому проверка перед ней не нужна.
- **Новое FR-4.9. Папка, которая не является клоном репозитория.** Непустая папка без `.git` — `Failed`; пустая клонируется. `origin` не совпадает с URL репозитория (без учёта регистра, завершающего `/` и `.git`) — `Failed` до быстрой проверки.
- **FR-7.2:** корневая папка архива — имя папки клона на диске; архив клона из `clonePath` содержит только `.git` и отслеживаемые файлы, включая сабмодули.
- **FR-9.5:** `.clone-locations.json` не меняется; проверки FR-4.8 и FR-4.9 выполняются как в обычном запуске; перенос только показывается: `The clone would be moved from '<old>' to '<new>'.`
- **§7.3:** `clonePath` в примере; описание ключа; правила валидации (абсолютный, не корень диска, без совпадений и вложенности с папками клона и wiki других записей, не пересекается с `sourcesRoot`); предупреждение FR-3.8 для `clonePath`.
- **§9, шаг 9:** перенос клона → определение изменений → проверка клона → обновление → архивация → запись состояния; в конце — запись `.clone-locations.json`.
- **П-10, П-12:** обновляются под `clonePath` и проверку.
- **История:** строка 0.24 от 2026-11-14.

## 4. Затрагиваемые проекты и типы

**GitHubBackup.Core**
- `AccountRepositoryConfig`, `RepositoryConfig`: `string? ClonePath`.
- `IRepositoryPathResolver`, `RepositoryPathResolver`: `GetCloneFolder` и `GetWikiCloneFolder` с параметром `clonePath`.
- `BackupConfigValidator`: правила §7.3 и FR-3.8.
- `BackupWorkItem`: `HasClonePath`; `CreateAll` передаёт `ClonePath`.
- `IGitClient`: `GetOriginUrlAsync`, `GetWorkingCopyStatusAsync` (`WorkingCopyStatus`), `GetIgnoredPathsAsync`.
- `GitRepositoryRequest`: `KeepIgnoredFiles`.
- `Clones/`: `ICloneGuard`, `CloneGuard`, `CloneCheckResult`, `LocalChangesException`, `CloneFolderException`, `ICloneLocationStore`, `CloneLocations`, `CloneLocation`.
- `ChangeDetector`: проверки FR-4.9 и guard перед обновлением и в превью.
- `ICloneRelocator`, `CloneRelocator`: `CloneRelocationRequest` (`DeleteOnFailure`), guard до переноса, `CloneRelocation(Result, Reason)`; новые значения `CloneRelocationResult`: `LocalChanges`, `MoveFailed`, `RemoteUrlNotSet`.
- `DiscoveryDecisionApplier`: токен в `ApplyAsync`, `ClonePath` в планах переименования и в `ApplyRename`; для `clonePath` `DeleteOnFailure = false`.
- `BackupOrchestrator`: перенос по записям, запись `.clone-locations.json` (в том числе при отмене), `KeepIgnoredFiles`, `ExcludedPaths`, сообщение dry-run.
- `ArchiveRequest`: `ExcludedPaths`.
- Логи `[LoggerMessage]` для новых событий; регистрация в DI.

**GitHubBackup.Infrastructure**
- `GitClient`: `clean -ffd`; `config --get remote.origin.url`; `--no-optional-locks status --porcelain=v2 --branch`, `rev-list --count`; `ls-files --others --ignored --exclude-standard --directory` с рекурсией по сабмодулям и отбросом путей вне клона.
- `GitStatusParser` (internal): разбор porcelain v2.
- `SevenZipArchiver`: список исключений в `TemporaryFolder`, `-x@<file> -scsUTF-8`, удаление в `finally`.
- `JsonCloneLocationStore`: `<sourcesRoot>\.clone-locations.json` через `AtomicFileWriter`; нет файла или файл повреждён → пустой набор и предупреждение.
- `BackupConfigJsonContext`: не пишет `clonePath`, когда он `null`.

**Хосты:** код не меняется, сообщения идут в существующую колонку сводки; `validate` использует ту же загрузку.

**Документы и харнесс:** `docs/PRD.md`, `README.md`, `.claude/rules/external-processes.md`.

## 5. Подход

Порядок: PRD → Core → Infrastructure → тесты → README и правило.

Обработка одного элемента (репозиторий, затем wiki):

```
прежняя папка из .clone-locations.json → нужен перенос? ─да→ guard (FR-4.8) → перенос + set-url ─сбой→ Failed
                                             │нет
                                             ▼
папка непуста без .git? → Failed (FR-4.9)
есть .git → origin совпадает? ─нет→ Failed (FR-4.9)
          → быстрая проверка (ls-remote) → UpToDate (guard не нужен)
          → guard (FR-4.8) ─проблемы→ Failed
          → обновление (clean -ffd для clonePath) → архив (исключения для clonePath)
```

**Принятые решения**

- **Р-1.** Прежняя папка клона хранится в `<sourcesRoot>\.clone-locations.json` с ключом по GitHub ID. `.backup-state.json` не подходит: он в облачной папке архивов, меняет место при правке `path` и нарушил бы FR-6.4. Запись обновляется, только когда клон лежит в текущей папке. Сбой записи — предупреждение в логе. Записи без ID пропускаются.
- **Р-2.** Неотправленные коммиты считаются без fetch, по имеющимся `refs/remotes/origin/*`. Ожидаемая ветка — через `ls-remote`. `status` выполняется с `--no-optional-locks`.
- **Р-3.** На пути `UpToDate` guard не выполняется. В dry-run guard выполняется, если превью не `UpToDate`.
- **Р-4.** Игнорируемые файлы исключаются из архива списком исключений 7-Zip (`ls-files --others --ignored --exclude-standard --directory` по клону и сабмодулям). Файл списка лежит в `<sourcesRoot>\.tmp`.
- **Р-5.** Корневая папка архива клона из `clonePath` — имя папки на диске (`MyRepo\`).
- **Р-6.** Существующая папка: нет или пуста → клонировать; непуста без `.git` → `Failed`; `.git` и совпадающий `origin` → использовать как клон; другой `origin` (в том числе SSH) → `Failed`; `.git`-файл (связанный worktree) → `Failed`.
- **Р-7.** Перенос по записи — через `CloneRelocator` с `DeleteOnFailure = false`: guard → занятая цель → перенос → set-url. Перенос между томами → `Failed`. При переименовании (FR-2.5) для `sourcesRoot` остаётся откат с удалением, но guard выполняется до переноса.
- **Р-8.** `ApplyAsync` получает токен, потому что guard при переименовании выполняет `ls-remote`.
- **Р-9.** `clonePath`, равный `sourcesRoot`, внутри него или содержащий его, — ошибка валидации. Папка wiki резервируется всегда.
- **Р-10.** Новых экранов и правки `clonePath` в UI нет.
- **Р-11.** Порядок проблем в сообщении фиксирован: `uncommitted changes, untracked files, branch 'x' instead of 'y'` (или `detached HEAD instead of branch 'y'`), `N unpushed commit(s)`.

## 6. Тесты

**GitHubBackup.Core.Tests**
- `BackupConfigValidatorTests`: относительный путь, корень диска, совпадение с другой записью, с папкой wiki, вложенность, пересечение с `sourcesRoot`, совпадение с папкой клона по умолчанию, предупреждения для `backupRoot` и папки архивов, разные пути без ошибок.
- `RepositoryPathResolverTests`: `clonePath` задан, wiki с суффиксом, завершающий разделитель.
- `BackupWorkItemTests`: `HasClonePath` и папка клона.
- `CloneGuardTests`: чистый клон; каждая из четырёх причин; все причины по порядку; отсоединённый HEAD; ветки нет на сервере; пустой сервер; wiki.
- `ChangeDetectorTests`: локальные изменения → исключение без обновления; быстрая проверка без guard; чужой `origin`; непустая папка без `.git`; пустая папка; превью.
- `CloneRelocatorTests`: guard не пройден; сбой переноса без удаления; сбой set-url без удаления.
- `DiscoveryDecisionApplierTests`: переименование записи с `clonePath`; переход между списками сохраняет `ClonePath`; локальные изменения; токен.
- `BackupOrchestratorTests`: перенос и запись новой папки; нет прежней папки; занятая цель; локальные изменения; dry-run; отмена; сбой записи; `KeepIgnoredFiles` и `ExcludedPaths`.

**GitHubBackup.Infrastructure.Tests**
- `GitClientTests`: `clean -ffd`/`-ffdx`, `--no-optional-locks`, нет `origin`.
- `GitStatusParserTests`: строки porcelain v2.
- `SevenZipArchiverTests`: список исключений передаётся и удаляется; без исключений аргументы не меняются.
- `JsonBackupConfigStore*Tests`: чтение `clonePath`, `null` не пишется.
- `JsonCloneLocationStoreTests`: нет файла, повреждённый файл, круговая запись, атомарность.

**GitHubBackup.Infrastructure.IntegrationTests** (`LocalGitSandbox`, реальные git и 7z)
- `GitClientWorkingCopyTests`: статус по каждой причине, игнорируемые файлы не мешают, статус не меняет индекс и ссылки, список игнорируемых путей с сабмодулем, URL `origin`.
- `GitClientUpdateTests`: `clean -ffd` сохраняет игнорируемые файлы, `-ffdx` удаляет.
- `SevenZipArchiverTests`: исключённых записей нет в архиве.
- `CloneRelocatorTests`: реальный клон с изменением не переносится.
- `BackupOrchestratorTests` (основная приёмка): клон в `clonePath` и wiki рядом; без `clonePath`; смена, удаление, добавление `clonePath`; занятая цель; dry-run переноса; Theory «4 причины × место × репозиторий/wiki»; перенос при локальных изменениях не выполняется (смена `clonePath`, переименование); чистый клон обновляется; игнорируемые файлы в `clonePath` и в `sourcesRoot`; dry-run с локальными изменениями; `UpToDate` при неизменном сервере; непустая папка без `.git`; существующая рабочая копия; чужой `origin`.

**GitHubBackup.Cli.Tests:** `ValidateCommandHandlerTests` — относительный `clonePath`.

## 7. Проверка

| Пункт приёмки | Как проверяется |
|---|---|
| PRD обновлён | ревью diff `docs/PRD.md` по §3 |
| `clonePath` и wiki; без `clonePath` — FR-3.5 | интеграционные тесты оркестратора, `RepositoryPathResolverTests` |
| Ошибки и предупреждение валидации | `BackupConfigValidatorTests`, `ValidateCommandHandlerTests` |
| Перенос, занятая цель, dry-run | интеграционные тесты оркестратора |
| Четыре причины → `Failed`, код `1`, остальные обработаны | Theory оркестратора, `CloneGuardTests` |
| `sourcesRoot`/`clonePath`, репозиторий и wiki | оси той же Theory |
| Без переноса при непройденной проверке | тесты смены `clonePath` и переименования |
| Чистый клон обновляется | интеграционный тест и существующие полные циклы |
| Игнорируемые файлы | интеграционные тесты оркестратора и 7z |
| Dry-run → тот же `Failed` | интеграционный тест оркестратора |
| README | ревью |
| CI зелёный | сборка, тесты, `dotnet format`, CI pull request |

## 8. Вне объёма

- Правка `clonePath` в UI и в вопросах CLI.
- Перенос клона между томами копированием.
- `origin` по SSH и другим адресам: требуется `https://github.com/<owner>/<repo>.git`.
- Сервер переписал историю (force-push), а локальная `origin/<ветка>` устарела: проверка опирается на `origin/*` в клоне.
- Предупреждение «архивы внутри `clonePath`».
- Связанные worktree (`.git` в виде файла) как `clonePath`.
- Отдельный этап прогресса и телеметрии для проверки клона.

## 9. Решения

- Пользователь поручил выполнить задачу без отдельного ревью плана, если нет вопросов по планированию. Подлинных блокеров нет; решения Р-1…Р-11 приняты консервативно.
- Три решения выходят за букву issue и вынесены в отчёт пользователю: Р-6 (`origin` только https, SSH → `Failed`), Р-9 (`clonePath` внутри `sourcesRoot` — ошибка), Р-5 (корневая папка архива — имя папки на диске).

## 10. Задачи

- [x] Правки PRD (0.24)
- [x] `ClonePath` в модели; `BackupConfigJsonContext` не пишет `null`
- [x] `RepositoryPathResolver` с `clonePath`; вызывающие места
- [x] `BackupWorkItem.HasClonePath`
- [x] `BackupConfigValidator`: правила `clonePath`
- [x] `WorkingCopyStatus`, новые методы `IGitClient`, `KeepIgnoredFiles`
- [x] `GitClient` и `GitStatusParser`
- [x] `Core/Clones`: guard, исключения; DI
- [x] `ChangeDetector`: FR-4.9 и guard
- [x] `CloneRelocator`: запрос, guard, `DeleteOnFailure`, новые результаты
- [x] `DiscoveryDecisionApplier`: токен, `ClonePath`
- [x] `ICloneLocationStore` + `JsonCloneLocationStore`; DI
- [x] `BackupOrchestrator`: перенос, запись мест, dry-run, `KeepIgnoredFiles`, `ExcludedPaths`
- [x] `ArchiveRequest.ExcludedPaths` + `SevenZipArchiver`
- [x] Логи для новых событий
- [x] Unit-тесты Core и Infrastructure
- [x] Интеграционные тесты
- [x] Тест CLI `validate`
- [x] README
- [x] `.claude/rules/external-processes.md`
- [x] Сборка, тесты, формат; гигиена комментариев
