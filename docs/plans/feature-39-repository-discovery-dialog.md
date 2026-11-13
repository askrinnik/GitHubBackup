# План: F4.4 — диалог обнаружения репозиториев

| | |
|---|---|
| Issue | [#39 F4.4: Repository discovery dialog](https://github.com/askrinnik/GitHubBackup/issues/39) |
| Lane | Feature |
| Сложность | L |
| Дата | 2026-11-13 |
| Требования | FR-12.2, FR-2.5, FR-2.6 (также FR-2.4, FR-2.8, FR-2.9, FR-9.5, FR-12.7) |

## 1. Цель

Нужно добавить в WPF-приложение диалог обнаружения (FR-12.2). Это таблица новых, пропавших, переименованных и вернувшихся репозиториев с данными FR-2.6. В строке и для всех строк сразу доступны действия «добавить / пропустить в этот раз / исключить» (FR-2.6), для переименованных — выбор варианта а/б (FR-2.5). Ветку и путь можно править прямо в таблице. Решения применяет тот же `DiscoveryDecisionApplier` из `Core`, что и CLI (FR-12.7). Запись остаётся атомарной (FR-2.8), пропавшие и вернувшиеся обрабатываются как раньше (FR-2.4), недопустимые имена до диалога не доходят (FR-2.9).

Главное ограничение кода: `IDiscoveryDecisionHandler` спрашивает по одному репозиторию (`DecideNewAsync`, `DecideRenameAsync`), а пропавшие и вернувшиеся он не получает вовсе. Таблица с действиями «для всех» и с информационными строками требует, чтобы обработчик заранее видел все находки. Поэтому в `Core` добавляется один метод интерфейса (см. «Подход»).

## 2. Критерии приёмки

1. Unit-тесты ViewModel (issue).
2. Решения применяются к настройкам через `Core` (issue).
3. Покрыто тестами, CI зелёный (issue).
4. (добавлено) Диалог показывает новые, пропавшие, переименованные и вернувшиеся репозитории с данными FR-2.6.
5. (добавлено) Действия по строке и для всех: добавить / пропустить / исключить; для переименованных — вариант а/б; ветка и путь правятся в таблице.
6. (добавлено) WPF-хост регистрирует UI-обработчик в DI: бэкап из главного окна показывает диалог. Cancel и закрытие диалога ведут себя определённым образом (п. 12).
7. (добавлено) Строки пропавших и вернувшихся только информационные, без действий: по FR-2.4 статус `Unavailable`/`Active` ставится без вопроса. Это уже делает `DiscoveryDecisionApplier`.
8. (добавлено) «Исключить» для репозитория аккаунта даёт две области: account и global. Для отдельного репозитория — только global: иначе `DiscoveryDecisionApplier.EnsureValid` бросает `ArgumentException`. То же делает CLI.
9. (добавлено) Ветка проверяется через `BackupConfigValidator.IsValidBranch`, путь — через `BackupConfigValidator.IsAbsolutePath`. Пока есть неверное значение, кнопка Apply отключена. Пустая ветка даёт `null` (ветка по умолчанию). Путь, пустой или равный предложенной папке (без учёта регистра и завершающего разделителя), даёт `null`.
10. (добавлено) Если у переименованного `CurrentArchiveFolder` равна `RenamedArchiveFolder`, выбор а/б недоступен и решение равно `KeepArchiveFolder`. Так делает и CLI.
11. (добавлено) Dry run диалог не показывает: оркестратор в dry run не вызывает applier (FR-9.5). На `ConfirmIdSaveAsync` UI-обработчик отвечает `true`.
12. (добавлено) Cancel или закрытие диалога (X, Esc) отменяют весь запуск, как Ctrl+C в интерактивной консоли. Ни одно решение не применяется, настройки и диск не меняются. Запуск записывается в историю как отменённый, сводка показывает отмену. Если токен запуска отменён, пока диалог открыт, диалог закрывается и действует то же правило.
13. (добавлено) Диалог показывается, если у находок есть хотя бы одна строка, включая случай «только пропавшие/вернувшиеся». Кнопка Apply тогда работает как «Continue».

## 3. Затронутые проекты и типы

**GitHubBackup.Core**
- `Discovery/DiscoveryReview.cs` (новый): record со списками `New` (`NewRepositoryProposal`), `Renamed` (`RenameProposal`), `Missing` (`TrackedRepository`), `Returned` (`ReturnedRepository`) — все находки до вопросов по отдельным репозиториям.
- `Discovery/IDiscoveryDecisionHandler.cs`: новый метод `Task ReviewAsync(DiscoveryReview review, CancellationToken cancellationToken)`.
- `Discovery/DiscoveryDecisionApplier.cs`: сначала строит все предложения (по аккаунтам, затем отдельные, затем переименования), потом один раз вызывает `ReviewAsync`, затем задаёт вопросы по отдельным репозиториям в прежнем порядке.
- `Discovery/IDiscoveryDecisionApplier.cs`: в doc добавить вызов `ReviewAsync`.
- `Discovery/AutomaticDiscoveryDecisionHandler.cs`: `ReviewAsync` возвращает завершённую задачу, при отменённом токене — отменённую.
- `BackupConfiguration/ArchiveFolderComparison.cs` (новый): `public static bool IsSameFolder(string, string)` — перенос приватного `ConsoleDiscoveryDecisionHandler.IsSameFolder`, чтобы два хоста не дублировали правило (FR-12.7).

**GitHubBackup.Cli**
- `ConsoleDiscoveryDecisionHandler.cs`: `ReviewAsync` ничего не делает (CLI спрашивает по одному, FR-9.3) и использует `ArchiveFolderComparison.IsSameFolder`.

**GitHubBackup.App**
- `Discovery/DialogDiscoveryDecisionHandler.cs` (новый): один экземпляр на запуск. В `ReviewAsync` создаёт `DiscoveryReviewViewModel` и показывает диалог через `IDiscoveryDialogService`. После Apply сохраняет решения по ID репозитория. Cancel вызывает колбэк отмены запуска и бросает `OperationCanceledException`. `DecideNewAsync` и `DecideRenameAsync` берут сохранённые решения; если решения нет — `Skip` / `KeepArchiveFolder`. `ConfirmIdSaveAsync` возвращает `true`.
- `Discovery/IDiscoveryDecisionHandlerFactory.cs` + `DialogDiscoveryDecisionHandlerFactory.cs` (новые): `IDiscoveryDecisionHandler Create(Action cancelRun)`.
- `Dialogs/IDiscoveryDialogService.cs` (новый): `Task<bool> ShowAsync(DiscoveryReviewViewModel viewModel, CancellationToken cancellationToken)`, где `true` = Apply.
- `Dialogs/WpfDiscoveryDialogService.cs` (новый): через `Dispatcher.InvokeAsync` открывает `DiscoveryReviewWindow` модально, владелец — главное окно. `cancellationToken.Register` закрывает окно на UI-потоке.
- `ViewModels/DiscoveryReviewViewModel.cs` (новый): `Rows`, команды `AddAll`, `SkipAll`, `ExcludeAll`, `RenameAllArchiveFolders` (а), `KeepAllArchiveFolders` (б), `Apply` (CanExecute: нет ошибок), `Cancel`.
- `ViewModels/DiscoveryRowViewModel.cs` (базовый) + строки `New`, `Renamed`, `Missing`, `Returned`: данные FR-2.6, вид строки, `IsActionable`, редактируемые `Branch`/`Path` с ошибками, `Variant` и `CanChooseVariant`.
- `ViewModels/NewRepositoryChoice.cs` (новый enum выбора в строке).
- `Views/DiscoveryReviewWindow.xaml(.cs)` (новый): `DataGrid` с колонками FR-2.6, `ComboBox` действия и варианта, `TextBox` ветки и пути с ошибкой, кнопки «для всех», Apply/Cancel, `AutomationProperties.AutomationId`.
- `Runs/BackupRunService.cs`: на каждый запуск создаёт linked `CancellationTokenSource` и обработчик `factory.Create(linked.Cancel)`.
- `Hosting/AppServiceCollectionExtensions.cs`: убрать `DeferredDiscoveryDecisionHandler`, зарегистрировать фабрику и `IDiscoveryDialogService`.
- Удалить `Discovery/DeferredDiscoveryDecisionHandler.cs`.

## 4. Подход

1. **Core.** Добавить `DiscoveryReview`, `ReviewAsync` и `ArchiveFolderComparison`. Перестроить applier: сначала все предложения, потом `ReviewAsync`, потом вопросы по одному.
   - Отдельный метод, а не default interface method: NSubstitute для метода, который возвращает `Task`, отдаёт завершённую задачу, поэтому прежние тесты и поведение CLI не меняются.
   - `ReviewAsync` вызывается до любых изменений, поэтому отмена на нём ничего не трогает.
2. **Cli.** Пустой `ReviewAsync`, перенос `IsSameFolder` в Core.
3. **App, ViewModel.** Значения по умолчанию взяты из CLI: новая строка — Add с веткой и путём по умолчанию, переименованная — б (`KeepArchiveFolder`). «Exclude all» даёт самую узкую допустимую область. Действия «для всех» не трогают информационные строки.
4. **App, обработчик и сервис.** `DialogDiscoveryDecisionHandler` не ссылается на WPF; на UI-поток переходит только `WpfDiscoveryDialogService`.
5. **Отмена.** `BackupOrchestrator` ловит `OperationCanceledException` только при отменённом токене. Поэтому Cancel диалога сначала отменяет linked-токен запуска (колбэк `cancelRun`), затем бросает исключение. Результат — `IsCancelled`, запуск записан в историю, сводка показывает отмену.
6. **DI и окно.** Регистрации, XAML, AutomationId.
7. Удалить `DeferredDiscoveryDecisionHandler` и его тесты, обновить `AppHostTests`.

## 5. Тесты

- **Core.Tests:** `DiscoveryDecisionApplierTests` (review до первого решения, отмена review ничего не сохраняет, только пропавшие/вернувшиеся); `AutomaticDiscoveryDecisionHandlerTests` (`ReviewAsync`); `ArchiveFolderComparisonTests`.
- **Cli.Tests:** `ConsoleDiscoveryDecisionHandlerTests.ReviewAsync_WritesNothing`.
- **App.Tests:** `DiscoveryReviewViewModelTests` (строки всех видов с полями FR-2.6, значения по умолчанию, области исключения, действия «для всех», а/б, одинаковые папки, валидация ветки и пути, Apply/Cancel); `DialogDiscoveryDecisionHandlerTests`; `BackupRunServiceTests` (фабрика, linked-токен); `AppHostTests` (фабрика и сервис диалога).

## 6. Проверка

| Пункт | Как |
|---|---|
| 1, 4, 5, 7–10, 13 | `DiscoveryReviewViewModelTests` |
| 2 | `DialogDiscoveryDecisionHandlerTests` + `DiscoveryDecisionApplierTests` |
| 6 | `AppHostTests`, `BackupRunServiceTests` |
| 11 | тесты оркестратора для dry run + `ConfirmIdSaveAsync_ReturnsTrue` |
| 12 | `DialogDiscoveryDecisionHandlerTests`, `BackupRunServiceTests`, `DiscoveryDecisionApplierTests` |
| 3 | build, тесты, format, CI pull request |

## 7. Вне объёма

- FlaUI-тест диалога в `GitHubBackup.App.UITests`: нужна заглушка GitHub API, это отдельная задача.
- Вопрос о сохранении ID в dry run для UI: остаётся автоматическое «да» (FR-2.10).
- Редактор настроек (FR-12.5).
- Изменений PRD не нужно: `ReviewAsync` — внутренний API.

## 8. Решения

- Пользователь разрешил идти по workflow без отдельного ревью плана, если нет открытых вопросов. Допущения приняты по аналогии с CLI:
  - Cancel или закрытие диалога отменяют весь запуск (как Ctrl+C).
  - Диалог показывается и при одних пропавших или вернувшихся.
  - По умолчанию: Add для новых, б для переименованных; «Exclude all» — account для репозиториев аккаунта, global для отдельных.

## 9. Задачи

- [x] Core: `DiscoveryReview`, `IDiscoveryDecisionHandler.ReviewAsync`, `ArchiveFolderComparison.IsSameFolder`
- [x] Core: `DiscoveryDecisionApplier` — предложения, затем `ReviewAsync`, затем вопросы; doc `IDiscoveryDecisionApplier`
- [x] Core: `AutomaticDiscoveryDecisionHandler.ReviewAsync`
- [x] Core.Tests: applier, automatic handler, `ArchiveFolderComparisonTests`
- [x] Cli: пустой `ReviewAsync`, `ArchiveFolderComparison`; тест
- [x] App: ViewModel строк и `DiscoveryReviewViewModel`
- [x] App.Tests: `DiscoveryReviewViewModelTests`
- [x] App: `IDiscoveryDialogService`, `DialogDiscoveryDecisionHandler`, фабрика
- [x] App.Tests: `DialogDiscoveryDecisionHandlerTests`
- [x] App: `BackupRunService` — linked CTS и обработчик из фабрики; тесты
- [x] App: `DiscoveryReviewWindow` и `WpfDiscoveryDialogService`
- [x] App: регистрации DI; удалить `DeferredDiscoveryDecisionHandler`; `AppHostTests`
- [x] Build, тесты, format; проверка комментариев
