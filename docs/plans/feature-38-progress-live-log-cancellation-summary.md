# План: #38 F4.3 «Progress, live log, cancellation and summary»

- Issue: [#38](https://github.com/askrinnik/GitHubBackup/issues/38) F4.3: Progress, live log, cancellation and summary
- Lane: Feature
- Сложность: L (одно issue, три этапа A, B, C, по коммиту на этап)
- Дата: 2026-11-13
- Статус: утверждён 2026-11-13
- Требования: `docs/PRD.md` §6.12: FR-12.3, FR-12.4. Также FR-9.7, FR-9.8 (`Cancelled`), FR-8.8, FR-8.9, NFR-4, FR-10.6, NFR-1.

## 1. Цель

Пока идёт запуск из главного окна, UI показывает текущий репозиторий и операцию и живую ленту лога. Кнопка отмены работает как Ctrl+C в CLI: ядро завершает текущую операцию, удаляет временные файлы и записывает историю (FR-12.3, NFR-4, FR-8.8). По завершении UI показывает сводку с той же информацией, что FR-9.7 (FR-12.4). Сводка заменяет временную строку `LastRunMessage` из #37.

## 2. Критерии приёмки

Из issue, дословно:
- Интерфейс не блокируется во время бэкапа
- Unit-тесты ViewModel
- Покрыто тестами, CI зелёный

Из PRD (в issue их нет):
- Сводка содержит таблицу по элементам: репозиторий, статус, архив, длительность, ошибка. Затем итоги по статусам и путь к логу (FR-9.7).
- Сводка показывает предупреждения `backup-config.json` и предупреждение о том, что запуск не записан в историю (FR-8.9).
- Отменённый запуск: обработанные элементы, прерванный элемент `Cancelled`, необработанные элементы `Skipped`, затем строка об отмене. Итоги включают `Cancelled` (FR-9.7, FR-9.8).
- Отменённый запуск записывается в историю, в том числе отменённый до захвата блокировки (FR-8.8).
- Токен никогда не попадает в ленту лога и в сводку (FR-10.6, NFR-1).

Добавлено проверкой покрытия:
- Запуск остановлен критической ошибкой. Сводка показывает ошибки и путь к логу, таблицы нет (как CLI).
- Dry run. В начале сводки строка `Dry run: nothing was …`, итоги по статусам dry run, строки найденного обнаружением (`New:`, `Missing:`, `Renamed:`, `Returned:`).
- Запуск без элементов: строка `No repositories to back up.`.
- Отмена до захвата блокировки (ядро бросает `OperationCanceledException`). Сводка показывает строку об отмене и путь к логу, диалога нет.
- Кнопка отмены доступна только во время запуска. После нажатия она выключается, и UI показывает «Cancelling…».
- Новый запуск очищает прошлую сводку, прогресс и ленту.
- Пользователь закрывает окно во время запуска. Окно спрашивает подтверждение. «Да»: запуск отменяется, окно ждёт его завершения и закрывается. «Нет»: запуск продолжается.
- Часть B. Прогресс приходит из фонового потока. После завершения запуска запоздавшее событие прогресса не меняет экран.
- Часть C. Объём лога: лента виртуализована, хранит последние 5 000 записей, строки приходят в UI пачками.
- Неожиданное исключение ядра: диалог `RunFailedTitle`, как в #37. Сводки нет.

## 3. Изменения требований

Не нужны. Поведение при закрытии окна во время запуска выводится из NFR-4 и FR-8.8 (§10, вопрос 3).

## 4. Затронутые проекты и типы

### Часть A: отмена, сводка, закрытие окна

**GitHubBackup.Core**
- `Runs/BackupRunSummary` (новый). Содержимое сводки FR-9.7, общее для двух хостов (FR-12.7): перечисленные статусы и итоги (с `Cancelled`), строки обнаружения dry run, `FormatDuration`, константы `DryRunHeader`, `CancelledLine`, `NoRepositoriesLine`. Сейчас эта логика лежит приватно в `BackupSummaryRenderer` CLI.

**GitHubBackup.Infrastructure**
- `Logging/LogFiles`: `GetTextLogPath(string contentRootPath, TimeProvider timeProvider)`, путь текстового лога текущего дня. Сейчас это `CliLogPath.Get`.

**GitHubBackup.Cli**
- `BackupSummaryRenderer`: берёт статусы, итоги, строки обнаружения и формат длительности из `BackupRunSummary`. Публичные константы ссылаются на константы Core.
- `CliLogPath` удалить. `BackupCommandHandler` вызывает `LogFiles.GetTextLogPath`.

**GitHubBackup.App**
- `Runs/BackupRunService`: `Task.Run(() => orchestrator.RunAsync(request, cancellationToken))` без токена вторым аргументом.
- `ViewModels/RunSummaryViewModel` (новый, неизменяемый): `Header`, `Errors`, `Warnings`, `Rows`, `DiscoveryLines`, `CancelledLine`, `TotalsLine`, `LogPath`, `HasItems`.
- `ViewModels/RunSummaryRow` (новый record): `Repository`, `Status`, `Archive`, `Duration`, `Error`.
- `ViewModels/ICloseGuard` (новый): `Task<bool> CanCloseAsync()`.
- `ViewModels/RepositoriesViewModel`: `Summary` вместо `LastRunMessage`; `IHostEnvironment` и `TimeProvider` для пути к логу; критические ошибки и `HistoryWarning` идут в сводку; `OperationCanceledException` даёт сводку отменённого запуска; реализует `ICloseGuard`.
- `ViewModels/MainWindowViewModel`: `CanCloseAsync()` передаёт вызов в `CurrentViewModel as ICloseGuard`.
- `Views/MainWindow.xaml.cs`: обработчик `Closing` без логики: `e.Cancel = true`, `await viewModel.CanCloseAsync()`, затем повторный `Close()` с флагом.
- `Views/RepositoriesView.xaml`: кнопка `Cancel` (`StartBackupCancelCommand`, `CancelBackupButton`), «Cancelling…», панель сводки (`RunSummaryErrors`, `RunSummaryWarnings`, `RunSummaryTable`, `RunSummaryTotals`, `RunSummaryLogPath`).

### Часть B: текущий репозиторий и операция

- Core: `Runs/BackupProgress`, `Runs/BackupProgressStage`, `BackupRunRequest.Progress`, отчёты в `BackupOrchestrator`; `ChangeDetectionRequest.OperationProgress`, отчёты в `ChangeDetector`.
- App: `Runs/IUiDispatcher`, `Hosting/WpfUiDispatcher`, `Runs/UiProgress<T>`; прогресс в `IBackupRunService` и `RepositoriesViewModel` (номер запуска отсекает запоздавшие события); панель прогресса во View; регистрация в DI.

### Часть C: живая лента лога

- Infrastructure: `Logging/ILiveLogFeed`, `Logging/LiveLogEntry`, `Logging/LiveLogSink` (маскирование через `ISecretMasker`, неизменяемый снимок подписчиков); подключение в `AddGitHubBackupLogging`.
- App: `ViewModels/RunLogViewModel` (пачки через один `IUiDispatcher.Post`, предел 5 000); подписка в `RepositoriesViewModel` на время запуска; `ListBox` `RunLog` с виртуализацией и автопрокруткой.

## 5. Подход

### Часть A
1. Core: вынести из `BackupSummaryRenderer` в `BackupRunSummary` статусы, итоги, строки обнаружения и `FormatDuration`. `BackupSummaryRendererTests` подтверждают, что вывод CLI не изменился.
2. Infrastructure: `LogFiles.GetTextLogPath`, удалить `CliLogPath`.
3. App: убрать токен из `Task.Run`, иначе ранняя отмена не дойдёт до `RecordingBackupOrchestrator` и запуск не попадёт в историю (FR-8.8).
4. App, ViewModel: `Summary = null` в начале; сводка из результата; сводка отмены по `OperationCanceledException`; диалог для другого исключения. `CanCloseAsync()`: нет запуска → `true`; пользователь отказался → `false`; иначе отмена, `await StartBackupCommand.ExecutionTask`, `true`.
5. App, View: кнопка отмены, панель сводки, `Closing` в `MainWindow`. Проверка через Netwright.

### Часть B
`BackupProgress` → `BackupRunRequest.Progress` → отчёты в `ChangeDetector` и `BackupOrchestrator` → `IUiDispatcher`/`UiProgress<T>` → ViewModel → View.

### Часть C
`LiveLogSink` → регистрация в `AddGitHubBackupLogging` → `RunLogViewModel` → подписка в `RepositoriesViewModel` → `ListBox`.

## 6. Решения (спорные)

| Решение | Альтернатива и почему отклонена |
|---|---|
| #38 остаётся одним issue. Работа идёт тремя этапами: A (отмена, сводка, закрытие), B (прогресс), C (живой лог). Каждый этап собирается, проходит тесты и становится отдельным коммитом на ветке issue. | Разбить на три issue: пользователь выбрал одно issue с коммитом на этап. |
| Содержимое сводки выносится в Core `BackupRunSummary`, CLI и UI пользуются им. | Повторить логику в `RunSummaryViewModel`: нарушает FR-12.7, сводки хостов разойдутся. |
| Критические ошибки и `HistoryWarning` показываются в панели сводки, без модального диалога. | Оставить диалоги #37: FR-9.7 включает эти данные в сводку. Неожиданное исключение по-прежнему идёт в диалог. |
| Отмена до захвата блокировки показывает сводку со строкой `Cancelled: …`. | Строка CLI `Error: The operation was cancelled.`. |
| `Task.Run` без токена. | С токеном: ранняя отмена не дойдёт до ядра, запуск не будет записан (FR-8.8). |
| Закрытие окна во время запуска: подтверждение, затем отмена, ожидание и закрытие. | Закрыть сразу: история не пишется, `.tmp` остаётся (NFR-4). Запретить закрытие: неудобно. |
| Кнопка отмены привязана к сгенерированной `StartBackupCancelCommand`. | Своя команда и флаг `IsCancelling`: `wpf-mvvm.md` требует `…CancelCommand`. |
| B: явный `IProgress<BackupProgress>` в `BackupRunRequest`. | Свойства событий лога или `ActivityListener`: зависят от уровня лога или глобального слушателя. |
| B, C: `IUiDispatcher` (`Post`), пачки через один `Post`, без таймера. | `Progress<T>` в ViewModel: недетерминированные тесты. Таймер: лишняя задержка. |
| C: sink и `ILiveLogFeed` в Infrastructure. | Интерфейс в Core: Core о нём не знает. MEL-провайдер в App: маскирование пришлось бы повторить. |
| C: в UI хранятся последние 5 000 записей. | Без предела: память растёт. |

## 7. Тесты

### Часть A
- Core.Tests: `Runs/BackupRunSummaryTests` (статусы по виду запуска, итоги, строки обнаружения, `FormatDuration`).
- Infrastructure.Tests: `LogFilesTests.GetTextLogPath_LocalDate_ReturnsDatedPathInLogsFolder`.
- Cli.Tests: `BackupSummaryRendererTests`, `BackupCommandHandlerTests` остаются зелёными.
- App.Tests: `RunSummaryViewModelTests` (элементы, критические ошибки, dry run, отмена, пустой запуск, предупреждения); `RepositoriesViewModelTests` (сводка вместо `LastRunMessage`, ошибки в сводке, отмена, очистка при повторном запуске, `StartBackupCancel_*`, `CanClose_*`); `MainWindowViewModelTests` (`CanClose_*`); `BackupRunServiceTests.RunAsync_TokenCancelledBeforeStart_StillCallsOrchestrator`; `AppHostTests`.

### Часть B
- `BackupOrchestratorTests` и `ChangeDetectorTests` (отчёты о прогрессе), `UiProgressTests`, `RepositoriesViewModelTests` (прогресс, запоздавшие события, очистка), `BackupRunServiceTests`.

### Часть C
- `LiveLogSinkTests` (текст, маскирование, без подписчиков, отписка, многопоточность), интеграционный `SerilogFileLoggingTests`, `RunLogViewModelTests`, `RepositoriesViewModelTests` (подписка и очистка).

## 8. Проверка

| Пункт | Как проверяется |
|---|---|
| Интерфейс не блокируется | `BackupRunService` и `Task.Run`, `BackupRunServiceTests`, Netwright |
| Unit-тесты ViewModel | `RepositoriesViewModelTests`, `RunSummaryViewModelTests`, `MainWindowViewModelTests`, (C) `RunLogViewModelTests` |
| Сводка = FR-9.7 | `BackupRunSummaryTests`, `RunSummaryViewModelTests`, `BackupSummaryRendererTests` |
| Отмена, история, временные файлы | `StartBackupCancel_*`, `RunAsync_TokenCancelledBeforeStart_*`, существующие тесты отмены ядра |
| Закрытие окна во время запуска | `CanClose_*` |
| Токен не попадает в ленту и сводку | (C) `LiveLogSinkTests`, тесты ViewModel с маскированием |
| Покрыто тестами, CI зелёный | сборка, тесты без Smoke, `dotnet format --verify-no-changes`, CI PR |
| Окно в запущенном приложении | Netwright без `backup-config.json`: `#CancelBackupButton` выключена; после `#BackupAllButton` видны `#RunSummaryErrors` и `#RunSummaryLogPath` |

## 9. Вне объёма

- Диалог обнаружения: #39.
- FlaUI-тест прогресса, отмены и сводки: #42 (F4.7).
- Подробности отменённого запуска в истории UI: FR-12.6.
- Кнопка «Открыть лог»: FR-12.4 требует только путь.

## 10. Открытые вопросы

Все вопросы решены на ревью 2026-11-13: пользователь принял рекомендованные ответы 2–6 (см. §6 «Решения»).

1. ~~Разбиение~~ — решено: одно issue, три коммита по этапам.
2. Критические ошибки и `HistoryWarning` показываются в панели сводки, а не в диалоге. Это меняет решение из плана #37.
3. Закрытие окна во время запуска: подтверждение, отмена, ожидание завершения, закрытие.
4. Перенос содержимого сводки в Core `BackupRunSummary` и перевод CLI на него делаются в #38.
5. Отмена до захвата блокировки показывает строку `Cancelled: …`, а не `Error: The operation was cancelled.`.
6. (C) Лента показывает те же уровни, что файловый лог; предел 5 000 записей.

## 11. Сложность

- Всё issue: L. Больше 20 файлов в Core, Infrastructure, Cli и App; маршалинг из фоновых потоков, пачки лога, ожидание отмены при закрытии окна.
- Этап A: M (верхняя граница), около 12 тонких файлов. Этапы B и C: M каждый, по 6–8 файлов.
- Разработчик работает на Opus (сложность L), по одному вызову на этап.

## 12. Задачи

Часть A:
- [x] Core: `Runs/BackupRunSummary` + `BackupRunSummaryTests`
- [x] Cli: `BackupSummaryRenderer` на `BackupRunSummary`; `BackupSummaryRendererTests` зелёные
- [x] Infrastructure: `LogFiles.GetTextLogPath` + тест; удалить `CliLogPath`, поправить `BackupCommandHandler`
- [x] App: `BackupRunService` без токена в `Task.Run` + тест
- [x] App: `RunSummaryRow`, `RunSummaryViewModel` + тесты
- [x] App: `RepositoriesViewModel`: `Summary`, сводка отмены, `ICloseGuard`; обновить тесты
- [x] App: `ICloseGuard`, `MainWindowViewModel.CanCloseAsync`, `MainWindow` `Closing` + тесты
- [x] App: `RepositoriesView.xaml`: кнопка отмены, «Cancelling…», панель сводки
- [x] App.Tests: `AppHostTests`
- [x] Сборка, тесты без Smoke, `dotnet format --verify-no-changes`
- [ ] Проверка через Netwright

Часть B:
- [ ] Core: `BackupProgress`, `BackupProgressStage`, `BackupRunRequest.Progress`, `ChangeDetectionRequest.OperationProgress`
- [ ] Core: отчёты в `ChangeDetector` и `BackupOrchestrator` + тесты
- [ ] App: `IUiDispatcher`, `WpfUiDispatcher`, `UiProgress<T>`, DI + тесты
- [ ] App: прогресс в `IBackupRunService`, `RepositoriesViewModel`, View + тесты

Часть C:
- [ ] Infrastructure: `ILiveLogFeed`, `LiveLogEntry`, `LiveLogSink`, подключение + тесты
- [ ] App: `RunLogViewModel` + тесты; подписка; `ListBox` `RunLog`
- [ ] Netwright: лента видна
