# План: #41 «F4.6: Run and archive history viewer»

| | |
|---|---|
| Issue | [#41](https://github.com/askrinnik/GitHubBackup/issues/41) |
| Lane | Feature |
| Сложность | L |
| Дата | 2026-11-14 |
| Требования | FR-12.6, FR-8.5 (а также FR-8.2–8.4, FR-6.1, FR-12.7) |

## 1. Цель

В WPF-приложении появляется экран «History» с двумя вкладками (FR-12.6):
- **Runs.** Список запусков из `data\history.db` (FR-8.2) с фильтрами по периоду, статусу и режиму. Для выбранного запуска показываются детали: элементы (FR-8.3) и события обнаружения (FR-8.4).
- **Archives.** История архивов выбранного репозитория из `.backup-state.json` (FR-8.5, FR-6.1): дата, хеш, размер, изменившиеся ссылки и наличие файла. Есть действия «Open folder» и «Show in Explorer».

Логика чтения и вычисления лежит в `Core` и `Infrastructure`, хост только показывает данные (FR-12.7).

## 2. Критерии приёмки

Из issue, дословно:
- [x] Unit-тесты ViewModel
- [x] Отсутствующий архив помечается
- [ ] Покрыто тестами, CI зелёный

Чего требует PRD и нет в issue:
- Список запусков фильтруется по периоду, статусу и режиму; фильтр по режиму включает флаг `DryRun` (FR-8.2, FR-12.6).
- Детали запуска содержат начало и конец, режим, `DryRun`, фильтр (`--account`/`--repo`), код возврата и счётчики по статусам. Для каждого элемента видны статус, длительность, ветка, HEAD, архив, изменившиеся ссылки и ошибка. Показываются и события обнаружения (FR-8.2–8.4).
- В истории архивов есть архивы и репозитория, и wiki (FR-6.1).
- Файл-заглушка OneDrive считается существующим, содержимое архива не читается (FR-6.2 п. 2).

Добавлено проверкой покрытия:
- (добавлено) Пустая история показывает сообщение «No runs».
- (добавлено) Если базу истории не удаётся прочитать, ошибка показывается замаскированной, и экран не падает.
- (добавлено) Если файла состояния нет, показывается «No archives». Если файл повреждён или принадлежит другому id, показывается текст `BackupStateException`.
- (добавлено) Если папки архивов нет, «Open folder» недоступна.
- (добавлено) Если архив не выбран или файла нет, «Show in Explorer» недоступна.
- (добавлено) Если дата «From» позже даты «To», показывается сообщение и запрос не выполняется.
- (добавлено) Если `backup-config.json` не читается, список репозиториев пуст и показывается ошибка.
- (добавлено) Если Explorer не запускается, показывается диалог ошибки.

## 3. Затронутые проекты и типы

**GitHubBackup.Core**
- `History/RunHistoryQuery.cs` (новый): record фильтра. Поля: `From` и `To` (`DateTimeOffset?`, полуинтервал `[From, To)` по `StartedAt`), `Mode` (`RunMode?`), `IsDryRun` (`bool?`), `ExitCode` (`ExitCode?`).
- `History/IRunHistoryStore.cs` (изменён): метод `QueryAsync(RunHistoryQuery, CancellationToken)`. Он возвращает запуски с элементами и событиями, новые первыми, и бросает `RunHistoryException`.
- `History/IArchiveHistoryProvider.cs` и `History/ArchiveHistoryProvider.cs` (новые): `LoadAsync(string archiveFolder, long repositoryId, string fullName, CancellationToken)` через `IBackupStateStore` и `IFileSystem`.
- `History/ArchiveHistory.cs` (новый): `ArchiveFolder`, `FolderExists`, `Archives`.
- `History/ArchiveHistoryEntry.cs` (новый): `Target`, `File`, `FullPath`, `CreatedAt`, `Branch`, `HeadCommit`, `SizeBytes`, `ChangedRefs`, `FileExists`.
- `Overview/RepositoryOverviewEntry.cs` (изменён): `required init`-свойства `RepositoryId` и `ArchiveFolder`.
- `Overview/RepositoryOverviewProvider.cs` (изменён): заполняет их из `BackupWorkItem`.
- `CoreServiceCollectionExtensions.cs` (изменён): регистрация `IArchiveHistoryProvider`.

**GitHubBackup.Infrastructure**
- `History/SqliteRunHistoryStore.cs` (изменён): реализует `QueryAsync`.

**GitHubBackup.App**
- `Shell/IExplorerLauncher.cs`, `Shell/ExplorerLauncher.cs` (новые): запуск `explorer.exe` через `IProcessRunner`.
- `ViewModels/HistoryViewModel.cs`, `RunHistoryViewModel.cs`, `RunHistoryRow.cs`, `ArchiveHistoryViewModel.cs`, `ArchiveHistoryRow.cs` (новые), при необходимости `HistoryFilterOption.cs`.
- `ViewModels/RepositoriesViewModel.cs` (изменён): `OpenHistoryCommand`, доступна при `IsIdle`.
- `Views/HistoryView.xaml` и `.xaml.cs` (новые), `App.xaml` (DataTemplate), `Views/RepositoriesView.xaml` (кнопка `HistoryButton`).
- `Hosting/AppServiceCollectionExtensions.cs`, `Hosting/AppLog.cs`.

## 4. Подход

**Core: история архивов.** Провайдер читает файл состояния через `IBackupStateStore.LoadAsync`; `BackupStateException` уходит к ViewModel. Для каждой секции (Repository, Wiki) изменившиеся ссылки считаются как `record.Refs.GetChangedRefs(previous?.Refs)`, где `previous` — предыдущая запись той же секции; у первого архива изменились все ссылки. Правило совпадает с `BackupOrchestrator`. Наличие файла проверяется `IFileSystem.File.Exists` по правилу `ChangeDetector`: заглушка OneDrive считается существующей. Итоговый список отсортирован по `CreatedAt` (момент UTC) по убыванию, у каждой записи указан `Target`.

**Core: папка архивов из обзора.** `RepositoryOverviewEntry` получает `RepositoryId` и `ArchiveFolder` из `BackupWorkItem.CreateAll`, поэтому история использует ту же папку, что и запуск.

**Infrastructure: `QueryAsync`.** `Mode`, `IsDryRun` и `ExitCode` фильтруются в SQL; порядок — `OrderByDescending(Id)` (запуски не пересекаются, NFR-SYS-1). Элементы и события загружаются через `Include` и `AsSplitQuery`, как в `GetAsync`. Период фильтруется в памяти по `StartedAt.UtcDateTime`: EF Core SQLite хранит `DateTimeOffset` как TEXT и не переводит сравнение в SQL. `SqliteException` оборачивается в `RunHistoryException` с путём базы.

**App: `ExplorerLauncher`.** Процесс запускается только через `IProcessRunner` (правило `external-processes.md`). Исполняемый файл — `explorer.exe` из папки Windows. Аргументы: `[folder]` для «Open folder» и `["/select," + filePath]` для «Show in Explorer». Код возврата игнорируется: `explorer.exe` возвращает `1` и при успехе.

**App: ViewModels.**
- Фильтр статуса работает по `ExitCode`: All, `Success`, `RepositoriesFailed`, `Critical`, `DiscoveryEvents`.
- Фильтр режима: All, `Interactive`, `Silent`, `Ui`; отдельный фильтр «Dry run»: All, Regular only, Dry run only.
- Период задаётся датами «From» и «To» включительно в местных днях; они переводятся в `[From 00:00, (To+1) 00:00)` с поясом `TimeProvider.LocalTimeZone`.
- Изменение фильтра перезапрашивает список. Ошибки маскируются `ISecretMasker`, пишутся в лог и показываются в свойстве ошибки.
- Репозитории для вкладки Archives берутся из `IRepositoryOverviewProvider`. `OpenFolderCommand` доступна, если папка есть; `ShowInExplorerCommand` — если выбран архив, файл которого есть. Ошибка Explorer показывается через `IDialogService.ShowError`.
- Строка с отсутствующим файлом получает в колонке «File» текст «Missing» и выделение стилем.

**Навигация.** Кнопка «History» в `RepositoriesView` доступна при `IsIdle`, как «Settings».

## 5. Тесты

- **Core.Tests:** `ArchiveHistoryProviderTests` (пустой файл, обе секции, первый и следующий архив, wiki сравнивается только с wiki, файл есть или нет, папки нет, ошибка хранилища, разные смещения времени); дополнения `RepositoryOverviewProviderTests` и `CoreServiceCollectionExtensionsTests`.
- **Infrastructure.IntegrationTests:** `SqliteRunHistoryStoreTests.QueryAsync_*` (пусто, без фильтра, режим, dry run, код возврата, границы периода, разные смещения, комбинация, отмена, нечитаемая база).
- **App.Tests:** `RunHistoryViewModelTests`, `ArchiveHistoryViewModelTests` (включая `MissingArchive_RowIsMissingTrue`), тесты `ArchiveHistoryRow`, `HistoryViewModelTests`, дополнения `RepositoriesViewModelTests`, `ExplorerLauncherTests`, `AppHostTests`.

## 6. Проверка

| Критерий | Как проверяется |
|---|---|
| Unit-тесты ViewModel | `RunHistoryViewModelTests`, `ArchiveHistoryViewModelTests`, `HistoryViewModelTests` |
| Отсутствующий архив помечается | `ArchiveHistoryProviderTests.LoadAsync_ArchiveFileMissing_FileExistsFalse`, `ArchiveHistoryViewModelTests.MissingArchive_RowIsMissingTrue` |
| Фильтры и детали запуска | `SqliteRunHistoryStoreTests.QueryAsync_*`, `RunHistoryViewModelTests` |
| «Open folder» / «Show in Explorer» | `ExplorerLauncherTests`, тесты команд |
| Покрыто тестами, CI зелёный | сборка, тесты решения, `dotnet format --verify-no-changes`, CI пулреквеста |

## 7. Вне объёма

- Дополнение истории архивов данными запусков («при необходимости», FR-8.5).
- Предвыбор на экране истории репозитория, выбранного в дереве: `INavigationService.NavigateTo<T>()` не передаёт параметр.
- Постраничная загрузка запусков.
- Очистка истории (FR-8.7).
- FlaUI-тест экрана истории и ручная проверка в запущенном приложении.

## 8. Решения

- Репозиторий для истории архивов выбирается на самом экране истории; предвыбора из дерева нет.
- Статус запуска — это его `ExitCode`. Отменённый запуск попадает в `Critical`; отдельного значения «Cancelled» в фильтре нет.
- «Show in Explorer» запускает `explorer.exe "/select,<путь>"` через `IProcessRunner`. P/Invoke `SHOpenFolderAndSelectItems` не используется.
- Explorer делит командную строку по запятым, а имя архива в `.backup-state.json` может содержать запятую (ревью безопасности, I2). Поэтому `ExplorerLauncher` не передаёт Explorer путь с запятой: «Show in Explorer» для файла с запятой в имени открывает папку архивов, а папка с запятой в пути даёт `ArgumentException`, который ViewModel показывает в диалоге ошибки.
- Решения приняты по умолчанию предложения планировщика: пользователь попросил реализовать без остановки, если по плану нет вопросов.

## 9. Задачи

- [x] Core: `RunHistoryQuery`, `IRunHistoryStore.QueryAsync`.
- [x] Core: `ArchiveHistory`, `ArchiveHistoryEntry`, `IArchiveHistoryProvider`, `ArchiveHistoryProvider`, регистрация.
- [x] Core: `RepositoryOverviewEntry.RepositoryId` и `ArchiveFolder`, заполнение в `RepositoryOverviewProvider`.
- [x] Core.Tests: `ArchiveHistoryProviderTests` и дополнения.
- [x] Infrastructure: `SqliteRunHistoryStore.QueryAsync`.
- [x] Infrastructure.IntegrationTests: `QueryAsync_*`.
- [x] App: `IExplorerLauncher`, `ExplorerLauncher`, `ExplorerLauncherTests`.
- [x] App: `RunHistoryViewModel`, `RunHistoryRow`.
- [x] App: `ArchiveHistoryViewModel`, `ArchiveHistoryRow`.
- [x] App: `HistoryViewModel`, `RepositoriesViewModel.OpenHistoryCommand`.
- [x] App: `HistoryView.xaml`, DataTemplate в `App.xaml`, кнопка `HistoryButton`.
- [x] App: регистрация в DI, тесты `AppHostTests`.
- [x] App.Tests: тесты ViewModel.
- [x] Сборка, тесты, `dotnet format --verify-no-changes`.
