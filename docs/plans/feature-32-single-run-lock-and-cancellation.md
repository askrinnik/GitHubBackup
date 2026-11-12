# План: #32 F3.7 — блокировка запуска и отмена

- Issue: [#32](https://github.com/askrinnik/GitHubBackup/issues/32) — F3.7: Single-run lock and cancellation
- Lane: Feature
- Сложность: L
- Дата: 2026-11-12

## 1. Цель

Нужно обеспечить два свойства запуска.

- Одновременно идёт только один запуск бэкапа (NFR-SYS-1). Программа захватывает именованный mutex ОС. Второй экземпляр завершается с кодом `2` и сообщением «бэкап уже выполняется» (FR-9.10).
- Ctrl+C в CLI корректно останавливает текущий процесс git или 7z (NFR-4). Временные файлы удаляются (FR-7.4). Пользователь видит сводку того, что успело обработаться (FR-9.7).

Блокировка захватывается на шаге 4 §9, после получения токена и до очистки `.tmp`.

Что уже есть в коде:

- `ProcessRunner` при отмене убивает дерево процессов.
- `SevenZipArchiver` при отмене удаляет временные файлы.
- `BackupOrchestrator.EmptyTemporaryFolder` очищает `.tmp`.

Чего не хватает:

1. Блокировки нет совсем.
2. `Program.cs` передаёт `CancellationToken.None`. Хост собран на `HostApplicationBuilder`, поэтому в нём зарегистрирован `ConsoleLifetime`. Он перехватывает Ctrl+C и вызывает `StopApplication()`, но `ApplicationStopping` никто не слушает. Ctrl+C, скорее всего, игнорируется и запуск идёт до конца.
3. `BackupOrchestrator.RunAsync` пробрасывает `OperationCanceledException` наружу, его никто не ловит: нет ни сводки, ни кода возврата.

## 2. Критерии приёмки

- [x] Тест: второй экземпляр получает код 2
- [x] Тест: отмена во время архивации не оставляет `*.zip.tmp`
- [ ] Покрыто тестами, CI зелёный
- [x] (добавлено) Если блокировка занята, программа не очищает `.tmp` и не запускает обнаружение. Она пишет одну строку ошибки и путь к логу.
- [x] (добавлено) Блокировка освобождается при любом исходе: успех, критическая ошибка, отмена, исключение.
- [x] (добавлено) Mutex, брошенный упавшим процессом (abandoned), считается захваченным, в лог пишется предупреждение.
- [x] (добавлено) Ctrl+C отменяет токен команды. После отмены выводится сводка: обработанные элементы со своими статусами, прерванный элемент — `Cancelled`, необработанные — `Skipped`. Код возврата — `2`.
- [x] (добавлено) Отмена во время обнаружения или принятия решений даёт сводку без элементов, строку об отмене и код `2`.
- [x] (добавлено) Если архив уже перенесён в папку архивов, запись в файл состояния добавляется и после отмены.
- [x] (добавлено) Отмена `validate` и `set-token` даёт одну строку в stderr и код `2`, а не необработанное исключение.

## 3. Изменения PRD (версия следующая, нужен апрув до кода)

- **FR-9.8:** новая строка таблицы — `Cancelled` | «Обработка прервана отменой (Ctrl+C, кнопка в UI)».
- **FR-9.7, дополнение:** «При отмене сводка показывает элементы, обработанные до отмены, прерванный элемент со статусом `Cancelled` и необработанные элементы со статусом `Skipped` (причина — отмена запуска), затем строку об отмене».
- **FR-9.10, код `2`:** «…, блокировка, сбой API при обнаружении, **отмена запуска пользователем**».
- **NFR-SYS-1, дополнение:** «Mutex `Global\GitHubBackup.BackupRun` — один на машину, общий для всех сеансов, пользователей и любых `sourcesRoot`. Dry-run тоже захватывает блокировку. Mutex, брошенный упавшим процессом, считается свободным».
- §14: новая строка истории изменений.

## 4. Затрагиваемые проекты и типы

**GitHubBackup.Core**

- `Runs/IRunLock` (новый) — `Task<IDisposable?> TryAcquireAsync(CancellationToken)`; `null`, если блокировку держит другой запуск.
- `Runs/BackupItemStatus` — добавить `Cancelled`.
- `Runs/BackupRunResult` — `IsCancelled { get; init; }`; `ExitCode`: `IsCancelled` → `Critical`, первым вместе с `CriticalErrors`.
- `Runs/BackupOrchestrator` — захват и освобождение блокировки через `using`, обработка отмены в цикле, запись состояния после переноса архива с `CancellationToken.None`.
- `Runs/BackupOrchestratorLog` — `RunLockHeld`, `RunCancelled`.

**GitHubBackup.Infrastructure**

- `Runs/NamedMutexRunLock` (новый, internal) — держит `Mutex` на выделенном потоке.
- `Runs/NamedMutexRunLockLog` (новый) — брошенный mutex, отказ в доступе.
- `InfrastructureServiceCollectionExtensions` — `TryAddSingleton<IRunLock, NamedMutexRunLock>()`.

**GitHubBackup.Cli**

- `CliApplication` — связывает `IHostApplicationLifetime.ApplicationStopping` с токеном команды; ловит `OperationCanceledException` при отменённом токене → `Critical` и строка «The operation was cancelled.».
- `BackupSummaryRenderer` — строка `Cancelled: the run was stopped before it finished.`, `Cancelled` в итогах и в `GetStyle`.

## 5. Подход

1. **PRD.** Сначала правка по §3, потом код.
2. **Core: блокировка.** Сразу после `tokenProvider.GetTokenAsync` — `using var handle = await runLock.TryAcquireAsync(...)`. `null` → `Stop(["A backup is already running."], …)`: код `2`, `.tmp` не тронут. Dry-run тоже берёт блокировку.
3. **Core: отмена.** Тело `RunAsync` после захвата — `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)`. Прерванный элемент — `Cancelled`, оставшиеся — `Skipped` с сообщением «Not processed: the run was cancelled.». Отмена до цикла даёт `new([], warnings, []) { IsCancelled = true }`. Существующие фильтры в `ProcessAsync`/`ProcessWikiAsync` остаются.
4. **Core: точка фиксации.** `ArchiveAsync` вызывает `stateStore.AppendAsync(..., CancellationToken.None)`: архив уже лежит под окончательным именем, запись о нём не теряется.
5. **Infrastructure: mutex.** `ReleaseMutex` нужно вызывать в потоке-владельце, поэтому захват и освобождение идут на выделенном фоновом потоке: `new Mutex(false, name)`, `WaitOne(0)`, результат через `TaskCompletionSource`; `Dispose` хэндла сигналит потоку, тот вызывает `ReleaseMutex`. `AbandonedMutexException` → захвачено + предупреждение. `UnauthorizedAccessException` → занято. Имя по умолчанию `Global\GitHubBackup.BackupRun`; internal-конструктор с именем для тестов (`Local\GitHubBackup.Tests.<guid>`).
6. **Cli.** `CreateLinkedTokenSource(cancellationToken, lifetime.ApplicationStopping)` в `CliApplication.RunAsync`. Убийство процессов и удаление временных файлов уже есть в `ProcessRunner` и `SevenZipArchiver`.

## 6. Тесты

- **Core.Tests / `BackupOrchestratorTests`:** блокировка занята → код 2, `.tmp` и обнаружение не тронуты; порядок захвата; освобождение при успехе, остановке, отмене; dry-run берёт блокировку; три существующих теста отмены переписать на `..._ReturnsCancelledResult`; отмена между элементами; отмена после переноса архива всё равно пишет состояние.
- **Core.Tests / `BackupRunResultTests`:** `IsCancelled` → `Critical`, побеждает `Failed` и события обнаружения.
- **Infrastructure.IntegrationTests / `NamedMutexRunLockTests`:** свободен; занят другим экземпляром → `null`; после освобождения; abandoned → захват + предупреждение; освобождение из другого потока.
- **Infrastructure.IntegrationTests / `SevenZipArchiverTests`:** отмена реального 7z → нет `*.zip.tmp`/`*.zip` ни в `.tmp`, ни в папке архивов.
- **Infrastructure.Tests:** DI — `IRunLock` разрешается в `NamedMutexRunLock`.
- **Cli.Tests:** `BackupSummaryRendererTests` (отмена с элементами и без), `BackupCommandHandlerTests` (отмена → сводка и 2; блокировка → ошибка и 2), `CliApplicationTests` (`StopApplication()` отменяет токен команды; `OperationCanceledException` → 2 без стека).

## 7. Проверка

| Критерий | Как проверить |
|---|---|
| Второй экземпляр → код 2 | `NamedMutexRunLockTests`, `BackupOrchestratorTests`, `BackupCommandHandlerTests` |
| Отмена при архивации не оставляет временных файлов | `SevenZipArchiverTests` (integration) |
| Ctrl+C останавливает git/7z, выводит сводку | `CliApplicationTests`, `BackupOrchestratorTests` |
| CI зелёный | сборка, тесты без smoke, `dotnet format --verify-no-changes`, CI pull request |

## 8. Вне рамок

- Запись истории в SQLite при отмене: `IRunHistoryStore` ещё не реализован.
- Кнопка отмены в UI (фаза 4); оркестратор уже общий для обоих хостов.
- Недоделанный клон после отмены во время `git clone` — отдельный bug-issue, если `ChangeDetector` его не обрабатывает.
- Отмена во время интерактивного вопроса Spectre: ввод блокирующий, Ctrl+C сработает после ответа.
- Второй Ctrl+C для принудительного завершения.

## 9. Открытые вопросы

1. Код возврата при отмене — рекомендация `2`.
2. Прерванный элемент — новый статус `Cancelled`, необработанные — `Skipped` с причиной.
3. Mutex — `Global\GitHubBackup.BackupRun`, один на машину.
4. Dry-run тоже захватывает блокировку.
5. Mutex другого пользователя с запрещающим ACL — считать блокировку занятой (код 2).

## Решения

Приняты на ревью плана (2026-11-12):

1. Отмена запуска даёт код `2`.
2. Прерванный элемент — новый статус `Cancelled`, необработанные — `Skipped` с причиной; правка FR-9.7 и FR-9.8.
3. Mutex `Global\GitHubBackup.BackupRun`, один на машину; mutex без доступа (ACL другого пользователя) считается занятым.
4. Dry-run захватывает блокировку.

## 10. Задачи

- [x] Правка PRD (FR-9.7, FR-9.8, FR-9.10, NFR-SYS-1, §14)
- [x] Core: `IRunLock`, `BackupItemStatus.Cancelled`, `BackupRunResult.IsCancelled` и `ExitCode`
- [x] Core: `BackupOrchestrator` — блокировка, сводка при отмене, `AppendAsync` с `CancellationToken.None`, логи
- [x] Infrastructure: `NamedMutexRunLock` на выделенном потоке, регистрация в DI
- [x] Cli: `CliApplication` (связь с `ApplicationStopping`, обработка отмены), `BackupSummaryRenderer`
- [x] Тесты Core, Infrastructure (unit и integration), Cli
- [x] `README.md`: коды возврата и Ctrl+C, если описаны
- [x] Сборка, тесты без smoke, `dotnet format --verify-no-changes`
