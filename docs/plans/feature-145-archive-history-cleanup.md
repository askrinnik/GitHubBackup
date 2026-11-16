# План: #145 F4.15: Archive history repository table, old archive cleanup and wiki status

| Поле | Значение |
|---|---|
| Issue | [#145](https://github.com/askrinnik/GitHubBackup/issues/145) |
| Lane | Feature |
| Сложность | L |
| Дата | 2026-11-16 |

## 1. Цель

Вкладка Archives показывает таблицу всех репозиториев конфигурации: счётчики архивов (вместе с wiki), размер, дату последнего архива и итог wiki по последнему запуску из истории. Под ней — архивы выбранного репозитория, включая файлы без записи. Кнопка «Delete old archives» удаляет старые архивы по FR-7.8: файлы уходят в Корзину Windows, `.backup-state.json` записывается атомарно, под блокировкой запуска. Wiki без страниц получает элемент `Skipped` с причиной `Wiki has no pages` в сводке и истории; код возврата не меняется.

Требования: FR-5.2, FR-7.6, FR-7.8, FR-8.5, FR-8.7, FR-9.5, FR-9.8, FR-12.6, NFR-SYS-1, NFR-2 (PRD 0.44).

## 2. Критерии приёмки

Из issue, дословно:
1. PRD 0.44 (FR-5.2, FR-7.6, FR-7.8, FR-8.5, FR-8.7, FR-9.8, FR-12.6, NFR-SYS-1, §2.2, §12) committed as the first commit of the branch
2. Archives tab shows a repository table with columns In state, On disk, Missing, Not in state, Size, Last archive, Wiki; columns sortable
3. Counts include the repository together with its wiki; only files named per FR-7.3 and FR-5.3 count
4. Selecting a row shows the repository's archives in the lower grid, including "Not in state" files
5. An unreadable .backup-state.json shows as an error in that repository row; other rows work
6. "Delete old archives" keeps the last repository archive and last wiki archive per FR-7.8; other files go to the Recycle Bin
7. Records without files are removed from .backup-state.json; files without a record are removed only if the target keeps an archive on disk
8. A confirmation with file count, size and record count is shown before cleanup; declining changes nothing
9. Cleanup does not start while a backup runs (run mutex) and says so
10. A file that cannot be moved to the Recycle Bin stays with its record; errors are shown as a list
11. .backup-state.json is written atomically after cleanup; both tables reload
12. An empty wiki yields a Skipped item with reason "Wiki has no pages" in the CLI and UI summary and in history; exit code unchanged
13. The Wiki column shows OK, No pages, Failed, Cancelled, Off, — per FR-12.6
14. README describes the new Archives tab and cleanup
15. Visible items verified in the running app (netwright)
16. Covered by tests (Core, Infrastructure, ViewModel, FlaUI), CI green

Добавлено из PRD и проверки покрытия:
- A1 (FR-12.6). Колонки «Repository» и «Account»; по умолчанию порядок конфигурации; кнопка очистки доступна, только когда очистка что-то удалит; при нечитаемом state недоступна (FR-7.8).
- A2 (FR-7.8). Если файла последнего архива нет, его запись остаётся, и остаётся самый новый архив из state с файлом на диске вместе с записью.
- A3 (FR-7.8). Если цель не сохраняет ни одного файла на диске, её файлы без записи не трогаются (и у цели без записей).
- A4 (FR-7.8). Если запись `.backup-state.json` не удалась, перемещённые файлы остаются в Корзине, их записи — в state; пользователь видит ошибку.
- A5 (FR-7.8). Очистка пишет в лог репозиторий, перемещённые файлы, удалённые записи, ошибки.
- A6 (FR-7.8). Файл никогда не удаляется безвозвратно: нет Корзины у тома, Корзина выключена, файл больше лимита — файл остаётся, ошибка видна.
- A7 (FR-12.6). Wiki = `—`, если история не читается, у репозитория нет элементов, или элемент репозитория `Skipped`/`Failed`/`Cancelled` без элемента wiki.
- A8 (FR-12.6). У файла без записи состояние «Not in state», дата, цель и хеш из имени, ветка пустая.
- A9 (FR-5.2, FR-9.5). В dry-run wiki без git-репозитория тоже получает `Skipped` с `Wiki has no pages`, без `WouldClone`.
- A10. Если под блокировкой план отличается от подтверждённого, очистка ничего не меняет, сообщает об этом, таблицы перечитываются.

## 3. Изменения требований

FR-9.5 говорит, что wiki без git-репозитория в dry-run «пропускается с отметкой в логе, строки нет (FR-5.2)». Это противоречит новому FR-5.2. Новая формулировка: элемент `Skipped` с причиной `Wiki has no pages` и отметка в логе, строки `WouldClone` нет. Решение — в разделе «Решения».

## 4. Затрагиваемые проекты и типы

**GitHubBackup.Core**
- `Archiving/IArchiveNameBuilder`, `ArchiveNameBuilder`: `TryParse(fileName, owner, repository, out ParsedArchiveName)` — без учёта регистра, по FR-7.3/FR-5.3, длина хеша из `BackupOptions.ShortHashLength`; разбор посимвольный, без regex.
- `Archiving/ParsedArchiveName` (новый record): `Target`, `CreatedAt`, `ShortHash`.
- `History/ArchiveHistoryEntry`: `IsInState`, `DiskSizeBytes` (`long?`), nullable `Branch`.
- `History/ArchiveHistory`: `InStateCount`, `OnDiskCount`, `MissingCount`, `NotInStateCount`, `SizeOnDiskBytes`.
- `History/ArchiveHistoryProvider`: перечисляет `*.zip` в папке через `IFileSystem`, добавляет файлы без записи; `TimeProvider` для смещения времени из имени.
- `State/BackupState`: `WithoutArchives(target, files)`.
- `State/IBackupStateStore.ReplaceAsync(archiveFolder, state, ct)`: атомарная перезапись существующего файла; файл другого репозитория не трогает.
- `History/IRunHistoryStore.GetLatestRepositoryRunsAsync(repositories, ct)` → `fullName → LatestRepositoryRun` (без учёта регистра).
- `History/LatestRepositoryRun` (record): `Repository`, `Wiki?` из одного запуска.
- `History/WikiStatus` (enum) и `WikiStatusResolver.Resolve` — таблица FR-12.6.
- `History/RepositoryArchiveSummary`, `IArchiveSummaryProvider`, `ArchiveSummaryProvider`: overview + история архивов каждого репозитория + один запрос к истории запусков; ошибки state и файлов — ошибка строки; `RunHistoryException` — `Unknown` у всех и лог.
- `Cleanup/ArchiveCleanupPlanner`, `ArchiveCleanupPlan` (equality по содержимому).
- `Cleanup/IArchiveCleanupService`, `ArchiveCleanupService` (`PrepareAsync`, `ExecuteAsync`), `ArchiveCleanupResult` (`Completed`, `RunLockHeld`, `PlanChanged`), `ArchiveCleanupLog`.
- `Files/IRecycleBin`, `RecycleOutcome`.
- `Runs/BackupOrchestrator.ProcessWikiAsync`: на `GitRepositoryNotFoundException` логирует `WikiNotFound` и возвращает `FinishWiki(... Skipped, WikiNoPagesMessage ...)`; `WikiNoPagesMessage = "Wiki has no pages"`.
- `CoreServiceCollectionExtensions`: регистрации.

**GitHubBackup.Infrastructure**
- `History/SqliteRunHistoryStore.GetLatestRepositoryRunsAsync`: проекция без `Include`, без миграции.
- `State/JsonBackupStateStore.ReplaceAsync` через `AtomicFileWriter`.
- `Files/ShellRecycleBin` (`internal`), регистрация; `AllowUnsafeBlocks` для сгенерированного interop.

**GitHubBackup.App**
- `ViewModels/ArchiveHistoryViewModel`: таблица `ArchiveRepositoryRow`, `DeleteOldArchivesCommand`, подтверждение, сообщения, перезагрузка с сохранением выбора.
- `ViewModels/ArchiveRepositoryRow` (новый), `ArchiveHistoryRow` («Not in state»).
- `Views/HistoryView.xaml`: `DataGrid` `ArchiveRepositoryList` с сортировкой, `GridSplitter` `ArchiveRepositorySplitter`, `ArchiveHistoryList`, `DeleteOldArchivesButton`.
- `AppLog`: `ArchiveSummaryLoadFailed`, `ArchiveCleanupFailed`.

**Документы**: `docs/PRD.md`, `README.md`, этот план.

## 5. Подход

1. **Один источник счётчиков** — `ArchiveHistoryProvider`: правило FR-8.5 реализовано один раз. «On disk» = записи с файлом + файлы без записи; «Size» — сумма `Length` файлов (у заглушек OneDrive длина логическая, без скачивания).
2. **FR-7.8 в чистой функции** `ArchiveCleanupPlanner`, по каждой цели: keep = последняя запись; если её файла нет — плюс последняя запись с файлом; остальные записи с файлом → Корзина и удаление записи; остальные записи без файла → удаление записи; файлы без записи → Корзина, только если в keep есть файл.
3. **Подтверждение без блокировки, выполнение под блокировкой.** `PrepareAsync` строит план без блокировки; `ExecuteAsync` берёт `IRunLock.TryAcquireAsync` (занято → `RunLockHeld`), строит план заново и сравнивает с подтверждённым (иначе `PlanChanged`). Mutex не держится во время модального диалога.
4. **Выполнение**: файлы → `IRecycleBin.RecycleAsync`; state без записей перемещённых файлов и без записей без файлов; один `ReplaceAsync`, если state изменился; ошибки — в результат; всё в лог.
5. **Корзина (`ShellRecycleBin`) — главный риск.** `SHFileOperation` и `Microsoft.VisualBasic.FileSystem.DeleteFile` с `FOF_ALLOWUNDO` молча удаляют навсегда, если Корзина недоступна, — не подходят.
   - `SHQueryRecycleBinW` для корня тома: ошибка → файл не трогается, «The volume of '<path>' has no Recycle Bin».
   - `IFileOperation` на STA-потоке, по одному `DeleteItem` + `PerformOperations` на файл; флаги `FOF_ALLOWUNDO | FOF_NO_UI | FOFX_RECYCLEONDELETE | FOFX_EARLYFAILURE`; `IFileOperationProgressSink.PreDeleteItem` отменяет операцию, если в `dwFlags` нет `TSF_DELETE_RECYCLE_IF_POSSIBLE`.
   - Успех = нет `GetAnyOperationsAborted`, `hrDelete` успешен и файла нет на диске.
   - Риск: защита опирается на флаг Shell; том без Корзины в CI не воспроизвести — ручная проверка; логика решения покрыта unit-тестом `ShouldAllowDelete(flags)`.
6. **Wiki-статус**: один запрос к истории; сопоставление по текущему `owner/name`.
7. **Пустая wiki** идёт через `FinishWiki`: span, метрика, сводка, история и код возврата обрабатывают `Skipped` штатно.

## 6. Тесты

- **Core.Tests**: `ArchiveNameBuilderTests` (`TryParse`, обратный разбор `Build`), `ArchiveHistoryProviderTests`, `WikiStatusResolverTests`, `ArchiveSummaryProviderTests`, `ArchiveCleanupPlannerTests`, `ArchiveCleanupServiceTests`, `BackupOrchestratorTests` (пустая wiki — `Skipped`, в том числе dry-run, span).
- **Infrastructure.Tests**: `JsonBackupStateStoreReplaceTests`, `ShellRecycleBinTests.ShouldAllowDelete`.
- **Infrastructure.IntegrationTests**: `SqliteRunHistoryStoreTests` (последний запуск, wiki того же запуска, регистр), `ShellRecycleBinTests` (перемещение, занятый файл, несуществующий путь), `BackupOrchestratorTests` (wiki `Skipped`).
- **Cli.Tests**: `BackupSummaryRendererTests`.
- **App.Tests**: `ArchiveHistoryViewModelTests`, `ArchiveRepositoryRowTests`, `ArchiveHistoryRowTests`, `RunSummaryViewModelTests`.
- **App.UITests**: `ArchiveHistoryTests` — числа строки, выбор, «Not in state», подтверждение с ответом No, сортировка.

## 7. Проверка

| Пункт | Как |
|---|---|
| 1 | Первый коммит ветки содержит только `docs/PRD.md` |
| 2, 4, 13 | `ArchiveHistoryViewModelTests`, `ArchiveHistoryTests`, netwright |
| 3 | `ArchiveHistoryProviderTests`, `ArchiveNameBuilderTests` |
| 5 | `ArchiveSummaryProviderTests`, ViewModel-тест; netwright с испорченным state |
| 6, 7 | `ArchiveCleanupPlannerTests`, `ArchiveCleanupServiceTests` |
| 8 | ViewModel-тест отказа, FlaUI-тест No |
| 9 | `ArchiveCleanupServiceTests`; вручную во время бэкапа |
| 10, A6 | `ShellRecycleBinTests`, `ArchiveCleanupServiceTests`; вручную — том без Корзины |
| 11 | `JsonBackupStateStoreReplaceTests`, ViewModel-тест перезагрузки |
| 12 | `BackupOrchestratorTests`, `BackupSummaryRendererTests`, `RunSummaryViewModelTests` |
| 14 | Ревью `README.md` |
| 15 | netwright на временном content root |
| 16 | build, тесты, UI-тесты, format, CI |

## 8. Вне рамок

- Автоматическая очистка архивов и очистка истории запусков.
- Сохранение высот панелей и сортировки Archives в `ui-settings.json`.
- Wiki-статус переименованного репозитория по старому имени (`—` до следующего запуска).
- Индекс по `RunItems.Repository` (нужна миграция).
- Очистка нескольких репозиториев за раз.

## 9. Решения

- Правка FR-9.5 входит в ту же версию PRD 0.44 (строка 0.44 истории дополнена), коммит 0.
- Интеграционные тесты `ShellRecycleBinTests` идут в обычном прогоне (локально и в CI) и кладут в Корзину мелкие временные файлы с понятным именем.
- Строка репозитория с нечитаемым `.backup-state.json`: в ячейке «In state» красное `Error`, полный текст в подсказке и над нижней таблицей при выборе; остальные числа пустые.
- Раскладка, правила очистки, Корзина вместо безвозвратного удаления, источник статуса wiki (история запусков), уровни лога без изменений, один PR из коммитов 0–3 — согласованы до планирования.

## 10. Задачи

**Коммит 0 — PRD 0.44**
- [ ] Правка FR-9.5; `docs/PRD.md` — первый коммит ветки.

**Коммит 1 — таблица репозиториев со счётчиками**
- [x] `ParsedArchiveName`, `TryParse` + тесты.
- [x] `ArchiveHistoryEntry`, счётчики `ArchiveHistory`, файлы без записи в `ArchiveHistoryProvider` + тесты.
- [x] `RepositoryArchiveSummary`, `ArchiveSummaryProvider` (wiki пока `Unknown`), регистрация + тесты.
- [x] `ArchiveRepositoryRow`, «Not in state» в `ArchiveHistoryRow`, `ArchiveHistoryViewModel` + тесты.
- [x] `HistoryView.xaml`: master-detail, `GridSplitter`, сортировка, AutomationId.
- [x] FlaUI `ArchiveHistoryTests`: таблица, выбор, сортировка.
- [x] README: вкладка Archives; этот план.

**Коммит 2 — очистка старых архивов**
- [x] `BackupState.WithoutArchives`, `ReplaceAsync` + тесты.
- [x] `ArchiveCleanupPlan`, `ArchiveCleanupPlanner` + тесты.
- [x] `IRecycleBin`, `RecycleOutcome`, `ShellRecycleBin`, регистрация + тесты.
- [x] `ArchiveCleanupService`, `ArchiveCleanupResult`, `ArchiveCleanupLog`, регистрация + тесты.
- [x] `DeleteOldArchivesCommand`, подтверждение, сообщения, перезагрузка + тесты; кнопка в XAML.
- [x] FlaUI: подтверждение и ответ No.
- [x] README: очистка, Корзина, блокировка.
- [ ] Ручная проверка тома без Корзины.

**Коммит 3 — статус wiki**
- [x] `ProcessWikiAsync`: `Skipped` с `Wiki has no pages`; тесты Core, Integration, CLI, App.
- [x] `LatestRepositoryRun`, `GetLatestRepositoryRunsAsync` + интеграционные тесты.
- [x] `WikiStatus`, `WikiStatusResolver` + тесты; подключение к `ArchiveSummaryProvider`.
- [x] Колонка Wiki с подсказкой `No pages` + тесты.
- [x] README: колонка Wiki, пустая wiki в сводке и dry-run.
- [ ] Сборка, тесты, format; netwright; отметить задачи плана.
