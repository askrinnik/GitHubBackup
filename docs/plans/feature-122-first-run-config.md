# План: #122 F4.10: Create backup-config.json from the settings editor on first run

| | |
|---|---|
| Issue | [#122](https://github.com/askrinnik/GitHubBackup/issues/122) |
| Заголовок | F4.10: Create backup-config.json from the settings editor on first run |
| Лейн | Feature |
| Сложность | L |
| Дата | 2026-11-14 |

## 1. Цель

Без `backup-config.json` WPF-приложение не показывает ошибку. Главное окно показывает приглашение и кнопку «Open settings». Редактор настроек открывается с пустой конфигурацией, а Save создаёт валидный файл атомарно. После этого дерево строится заново без перезапуска. CLI (`backup`, `validate`) без файла завершается с кодом `2` и подсказкой, как создать файл. Требования: FR-12.1, FR-12.5 («Первый запуск»), FR-2.8, §7.3, FR-9.10, NFR-2 (PRD 0.27).

## 2. Критерии приёмки

Из issue:

1. PRD описывает поведение без `backup-config.json` (версия и история обновлены).
2. Без `backup-config.json` WPF-приложение даёт открыть редактор настроек с пустой конфигурацией.
3. Save в редакторе создаёт валидный `backup-config.json`. Если обязательные поля пусты, ошибки показываются у полей, и файл не создаётся.
4. После Save главное окно показывает дерево из нового файла без перезапуска.
5. CLI без файла завершается с кодом `2` и подсказкой: `The backup configuration file '<path>' does not exist. Create it in the settings editor of GitHubBackup.exe.`
6. Тесты: ViewModel, хранилище конфигурации, сквозной тест CLI, UI-тест FlaUI первого запуска.

Добавлено из PRD:

7. Главное окно показывает `No settings yet. Open the settings editor to create backup-config.json.` и кнопку «Open settings» вместо ошибки и дерева.
8. Пока файла нет, кнопки бэкапа недоступны.
9. Выход из редактора без Save файл не создаёт.
10. Над полями редактор показывает, что файла ещё нет и что Save его создаст.
11. Если прежнего файла не было, `.bak` не создаётся (FR-2.8).
12. Пустая конфигурация: текущая `schemaVersion`, пустой `backupRoot`, `sourcesRoot: null`, пустые `exclude`, `accounts`, `repositories`, `discoverAccessibleRepositories: false`.

Добавлено проверкой покрытия:

13. `validate` без файла тоже даёт код `2` и то же сообщение.
14. Папки файла нет (`DirectoryNotFoundException`): та же ветка «файла нет», что и при `FileNotFoundException`.
15. Прочие ошибки чтения (битый JSON, нет доступа, ошибки валидации) в главном окне и в редакторе ведут себя как раньше.

## 3. Затронутые проекты и типы

**GitHubBackup.Core**

- `BackupConfiguration/BackupConfigException.cs`: класс перестаёт быть `sealed`.
- `BackupConfiguration/BackupConfigNotFoundException.cs` (новый): наследник `BackupConfigException`; сообщение §7.3 строит один публичный статический метод `FormatError(path)`.
- `BackupConfiguration/IBackupConfigStore.cs`: в XML-doc `LoadAsync` — `<exception cref="BackupConfigNotFoundException">`.
- `Overview/RepositoryOverviewProvider.cs`: без изменений, исключение не оборачивается (фиксирует тест).

**GitHubBackup.Infrastructure**

- `BackupConfiguration/JsonBackupConfigStore.cs`: `ReadAsync` при `FileNotFoundException` или `DirectoryNotFoundException` бросает `BackupConfigNotFoundException`.

**GitHubBackup.App**

- `ViewModels/SettingsViewModel.cs`: свойство `NewConfigMessage` и производное `IsNewConfig`; в `LoadAsync` ветка `BackupConfigNotFoundException` → `Populate(new BackupConfig())`; после успешного Save сообщение сбрасывается.
- `Views/SettingsView.xaml`: `TextBlock` `SettingsNewConfigMessage` над полями.
- `ViewModels/RepositoriesViewModel.cs`: константа `ConfigMissingMessage`, флаг `IsConfigMissing`; ветка not found в `LoadAsync`; `CanStartBackup` учитывает флаг.
- `Views/RepositoriesView.xaml`: панель с `ConfigMissingMessage` и кнопкой `OpenSettingsFromFirstRunButton` («Open settings», `OpenSettingsCommand`).
- `Hosting/AppLog.cs`: событие уровня Information «файла нет, его создаст редактор» вместо Warning со стеком.

**Документация**

- `README.md`: файл создаёт редактор настроек; короткий абзац «Первый запуск»; код `2` и подсказка CLI.

## 4. Подход

1. Core: новый подтип `BackupConfigException`. Все существующие обработчики (CLI, оркестратор, `ConfigurationErrorMessages`) работают без правок и печатают `Errors[0]` — сообщение §7.3. Код `2` даёт существующий путь.
2. Infrastructure: хранилище бросает новый тип.
3. App: ViewModel различают ошибки по типу исключения, не по тексту. Пустая конфигурация — `new BackupConfig()`. Save переиспользует `_configStore.Validate`. `AtomicFileWriter` пишет без прежнего файла и без `.bak`. После Save — `NavigateTo<RepositoriesViewModel>()`; ViewModel transient и перечитывает дерево.
4. XAML с AutomationId для FlaUI.
5. README.

Флажок «Dry run» остаётся доступным: PRD отключает только кнопки бэкапа.

## 5. Тесты

- `GitHubBackup.Infrastructure.Tests`, `JsonBackupConfigStoreTests`: `LoadAsync_FileMissing_ThrowsNotFoundWithHint`, `LoadAsync_FolderMissing_ThrowsNotFound`, `LoadAsync_FileUnreadable_ThrowsBaseExceptionNotNotFound`. `ConfigurationErrorMessagesTests`: `TryGet_BackupConfigNotFoundException_ReturnsHint`.
- `GitHubBackup.Core.Tests`: `RepositoryOverviewProviderTests.LoadAsync_ConfigNotFound_PropagatesNotFoundException`; случай not found в `BackupOrchestratorTests`.
- `GitHubBackup.App.Tests`, `SettingsViewModelTests`: `Load_ConfigNotFound_FillsEmptyConfigAndShowsNewConfigMessage`, `Save_NewConfigEmptyBackupRoot_ShowsErrorAtFieldAndWritesNothing`, `Save_NewConfigFilled_SavesCurrentSchemaVersionAndNavigatesBack`, `Cancel_NewConfigNoChanges_NavigatesWithoutSaving`.
- `GitHubBackup.App.Tests`, `RepositoriesViewModelTests`: `Refresh_ConfigNotFound_SetsConfigMissingWithoutLoadError`, `Refresh_ConfigNotFound_CannotStartAnyScope`, `Refresh_ConfigNotFoundThenFound_ClearsConfigMissingAndEnablesBackup`, `Refresh_OtherConfigError_DoesNotSetConfigMissing`, `OpenSettings_ConfigMissing_NavigatesToSettings`.
- `GitHubBackup.Cli.Tests`, `CliEndToEndTests`: `Backup_BackupConfigMissing_ExitsCriticalWithHintAndLogPath`, `Validate_BackupConfigMissing_ExitsCriticalWithHint`.
- `GitHubBackup.App.UITests`: `AppLaunchTests.Launch_BackupConfigMissing_ShowsFirstRunMessageAndDisablesBackup` вместо `Launch_BackupConfigMissing_ShowsTreeLoadError`; новый класс `FirstRunTests`: `Save_FilledBackupRoot_CreatesConfigWithoutBackupAndShowsTree`, `Save_EmptyBackupRoot_ShowsErrorAtFieldAndCreatesNothing`, `Cancel_WithoutSave_ReturnsToFirstRunMessageAndCreatesNothing`.

## 6. Проверка

| Пункт | Как проверяется |
|---|---|
| 1 | Diff `docs/PRD.md` (0.27) |
| 2, 10, 12 | `SettingsViewModelTests.Load_ConfigNotFound_…`, `FirstRunTests.Save_FilledBackupRoot_…` |
| 3, 11 | `SettingsViewModelTests.Save_NewConfig…`, `FirstRunTests.Save_EmptyBackupRoot_…`, `FirstRunTests.Save_FilledBackupRoot_…` |
| 4 | `FirstRunTests.Save_FilledBackupRoot_…` |
| 5, 13 | `CliEndToEndTests.*_BackupConfigMissing_…`, `JsonBackupConfigStoreTests.LoadAsync_FileMissing_…` |
| 7, 8 | `RepositoriesViewModelTests.Refresh_ConfigNotFound_…`, `AppLaunchTests.Launch_BackupConfigMissing_…` |
| 9 | `SettingsViewModelTests.Cancel_NewConfigNoChanges_…`, `FirstRunTests.Cancel_WithoutSave_…` |
| 14 | `JsonBackupConfigStoreTests.LoadAsync_FolderMissing_ThrowsNotFound` |
| 15 | Существующие тесты ошибок загрузки, `Refresh_OtherConfigError_…`, `LoadAsync_FileUnreadable_…` |

## 7. Вне объёма

- Экран истории без файла по-прежнему показывает свою ошибку загрузки: PRD первый запуск для него не описывает.
- Save в несуществующую папку (`Backup:ConfigPath` в отсутствующем каталоге): показывается существующий диалог «Settings not saved», каталог не создаётся.
- Полная переработка README — #44.

## 8. Решения

- План утверждён без отдельного ревью: пользователь разрешил пройти workflow до pull request и смотреть изменения в нём.
- Заметка редактора: `The file '<path>' does not exist yet. Save creates it.`
- Первый запуск пишет в лог событие уровня Information с путём, без стека.
- AutomationId: `ConfigMissingMessage`, `OpenSettingsFromFirstRunButton`, `SettingsNewConfigMessage`.
- README: только правка утверждения о создании файла, короткий абзац «Первый запуск» и фраза про CLI; остальное — в #44.

## 9. Задачи

- [x] Core: `BackupConfigException` не `sealed`; `BackupConfigNotFoundException` с `FormatError`; doc `IBackupConfigStore.LoadAsync`
- [x] Infrastructure: `JsonBackupConfigStore.ReadAsync` бросает `BackupConfigNotFoundException`
- [x] Тесты хранилища и `ConfigurationErrorMessages`
- [x] Тесты Core: `RepositoryOverviewProvider`, `BackupOrchestrator`
- [x] App: событие `AppLog`
- [x] App: `SettingsViewModel` и `SettingsView.xaml`
- [x] App: `RepositoriesViewModel` и `RepositoriesView.xaml`
- [x] Тесты ViewModel
- [x] Сквозные тесты CLI
- [x] UI-тесты FlaUI
- [x] README
- [x] Сборка, тесты, `dotnet format`, UI-тесты
