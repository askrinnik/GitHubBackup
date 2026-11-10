# План: #10 F1.3 — Запуск внешних процессов с логированием вывода

| Поле | Значение |
|---|---|
| Issue | [#10](https://github.com/askrinnik/GitHubBackup/issues/10) |
| Заголовок | F1.3: Run external processes with output logging |
| Лейн | Feature |
| Сложность | L |
| Дата | 2026-11-10 |

## 1. Цель

Нужна единая точка запуска внешних инструментов (git, git-lfs, 7z): `IProcessRunner`. Он принимает аргументы, рабочую папку и переменные окружения. Он пишет в лог:

- до запуска — команду с замаскированными секретами;
- во время работы — каждую строку stdout и stderr отдельной записью со свойствами `Tool`, `Stream`, `Repository`;
- после завершения — код возврата и длительность.

Успех определяется только кодом возврата. Отмена через `CancellationToken` завершает всё дерево процессов. Требования: FR-10.3, FR-10.4, FR-10.6, FR-11.3, NFR-1, NFR-4.

## 2. Критерии приёмки

Из issue (дословно):

1. Тест: секрет из переменной окружения не попадает в лог
2. Тест: отмена завершает долгий процесс
3. Вывод в stderr при коде 0 не считается ошибкой
4. Покрыто тестами, CI зелёный

Добавлено по PRD и по сверке с кодом:

5. (добавлено) Значения переменных окружения никогда не пишутся в лог. В записи о запуске есть только их имена (FR-11.3, NFR-1).
6. (добавлено) Если дочерний процесс напечатал значение секретной переменной, строка в логе замаскирована `***` — и в файлах лога, и в тексте, который runner передаёт в `ILogger` (защита на случай приёмника без `MaskingTextFormatter`, например OTLP из FR-10.5).
7. (добавлено) Записи строк вывода внутри `BeginRepositoryScope` несут `Repository`, `Tool` и `Stream` (FR-10.3, FR-10.4).
8. (добавлено) Ненулевой код возврата не бросает исключение: runner возвращает результат с этим кодом.
9. (добавлено) Отсутствующий исполняемый файл и отсутствующая рабочая папка дают `ProcessStartException`, сообщение называет путь.
10. (добавлено) Если токен отменён до запуска, процесс не стартует, runner бросает `OperationCanceledException`.
11. (добавлено) Аргументы с пробелами и кавычками доходят до процесса без изменений: они передаются через `ArgumentList`, без shell.
12. (добавлено) Пустой вывод даёт пустые списки строк и ни одной записи о строках.

## 3. Затрагиваемые проекты и типы

**GitHubBackup.Core** (папка `Processes/`):

- `IProcessRunner` — `Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)`.
- `ProcessRequest` (`sealed record`) — `required string FileName` (полный путь), `IReadOnlyList<string> Arguments = []`, `required string WorkingDirectory` (полный путь), `IReadOnlyList<ProcessEnvironmentVariable> EnvironmentVariables = []`.
- `ProcessEnvironmentVariable` (`sealed record`) — `(string Name, string Value, bool IsSecret = false)`.
- `ProcessResult` (`sealed record`) — `ExitCode`, `Duration`, `StandardOutput` и `StandardError` (строки без маскирования, для разбора в `IGitClient`), `Succeeded => ExitCode == 0`.
- `ProcessStartException` — процесс не удалось запустить (нет файла, нет рабочей папки, `Win32Exception` из `Process.Start`).
- `Logging/LogProperties` — константы `Tool` и `Stream`.
- `CoreServiceCollectionExtensions` — `TryAddSingleton(TimeProvider.System)`.

**GitHubBackup.Infrastructure** (папка `Processes/`):

- `ProcessRunner` (`internal sealed`) — реализация на `System.Diagnostics.Process`; зависимости `ISecretMasker`, `IFileSystem`, `TimeProvider`, `ILogger<ProcessRunner>`.
- `ProcessCommandLine` (`internal static`) — `Format(fileName, arguments)` строит строку команды для лога.
- `ProcessRunnerLog` (`internal static partial`, `[LoggerMessage]`) — `Starting`, `OutputLine`, `Exited`, `Cancelled`.
- `InfrastructureServiceCollectionExtensions` — регистрация `IProcessRunner`.

**Тесты:** `Infrastructure.Tests/Processes`, `Infrastructure.IntegrationTests/Processes`, помощники чтения лога в `TestUtilities/TemporaryContentRoot`, тесты DI, правила в `ArchitectureTests`.

## 4. Подход

1. Абстракция, модели и `ProcessStartException` лежат в `Core`; реализация — в `Infrastructure`.
2. Проверки до запуска: `ThrowIfCancellationRequested`; `FileName` и `WorkingDirectory` — полные пути, иначе `ArgumentException`; существование файла и папки через `IFileSystem`, иначе `ProcessStartException`.
3. Значение каждой переменной с `IsSecret` регистрируется в `ISecretMasker` до запуска. Несекретные значения не регистрируются.
4. Запись о запуске: `Tool`, замаскированная строка команды, рабочая папка, только имена переменных окружения.
5. `ProcessStartInfo`: `UseShellExecute=false`, `CreateNoWindow=true`, аргументы через `ArgumentList`, явная рабочая папка, перенаправление stdin/stdout/stderr (stdin закрывается сразу), UTF-8, переменные поверх унаследованного окружения.
6. stdout и stderr читаются параллельно построчно; каждая непустая строка логируется замаскированной со `Stream` = `stdout`/`stderr`.
7. `Repository` берётся из ambient-scope `BeginRepositoryScope`.
8. Длительность — через `TimeProvider`. Запись о выходе: `Information` при коде 0, `Warning` при ненулевом.
9. Отмена наблюдается и до выхода процесса, и при ожидании закрытия потоков. При отмене runner вызывает `Kill(entireProcessTree: true)`; ошибку kill он пишет в лог (`KillFailed`) и не бросает. Затем runner ждёт выхода и закрытия потоков, но не дольше `PipeDrainTimeout`. Если потоки держит процесс вне дерева, runner прекращает чтение и закрывает потоки. В конце — запись `Cancelled` и проброс `OperationCanceledException`. Любое другое исключение после старта тоже убивает дерево процессов перед пробросом.
10. Runner — singleton без состояния.

## 5. Тесты

- `ProcessCommandLineTests` — объединение аргументов, кавычки, пустой аргумент, путь с пробелами.
- `ProcessRunnerTests` — относительные пути, отсутствующий файл и папка, отмена до запуска.
- DI: `IProcessRunner` разрешается; `TimeProvider` зарегистрирован как `TimeProvider.System`.
- `ProcessRunnerProcessTests` (настоящий `cmd.exe`, без сети) — код 0, ненулевой код, stderr при коде 0, пустой вывод, аргументы с пробелами и кавычками, рабочая папка, переменная окружения, отмена долгого процесса (`ping -n 60 127.0.0.1` как внук runner).
- `ProcessRunnerLoggingTests` — секрет не попадает в файлы лога, секрет маскируется до `ILogger`, свойства `Tool`/`Stream`/`Repository`, порядок записей и `ExitCode`/`Duration`.
- `ArchitectureTests` — `Core` не зависит от `System.Diagnostics.Process`; в `Infrastructure` от него зависит только `ProcessRunner`.

## 6. Проверка

| Критерий | Чем проверяется |
|---|---|
| 1 | `ProcessRunnerLoggingTests` (секрет в файлах лога и до `ILogger`) |
| 2 | `ProcessRunnerProcessTests.RunAsync_CancelledWhileRunning_KillsProcessTreeAndThrows` |
| 3 | `ProcessRunnerProcessTests.RunAsync_StderrWithExitCodeZero_IsSucceeded` |
| 4 | полная сборка, все тесты, `dotnet format --verify-no-changes`, CI на PR |

## 7. Вне рамок

- `IToolLocator`, `IGitClient` и набор переменных `GIT_CONFIG_*`, `GIT_TERMINAL_PROMPT`, `GCM_INTERACTIVE` — отдельные issue.
- Таймауты процессов: PRD их не описывает.
- Удаление временных файлов после отмены (FR-7.4) — забота `IArchiver` и оркестратора.
- Трейсы OpenTelemetry для процессов (FR-10.5).
- Кодировка вывода 7-Zip — решается в issue `IArchiver`.

## 8. Решения

1. Строки stdout и stderr пишутся в лог на уровне `Information`.
2. `Repository` берётся из ambient-scope оркестратора, а не из поля `ProcessRequest`.
3. Секретность переменной отмечает вызывающий код (`IsSecret`), а не runner по шаблону имени.
4. `ProcessResult` хранит stdout и stderr без маскирования; текст для сводки и истории маскирует вызывающий код.
5. `TimeProvider.System` регистрируется в `AddGitHubBackupCore`.

## 9. Задачи

- [x] Core: `Processes/IProcessRunner`, `ProcessRequest`, `ProcessEnvironmentVariable`, `ProcessResult`, `ProcessStartException`
- [x] Core: `LogProperties.Tool`, `LogProperties.Stream`; `TimeProvider.System` в `AddGitHubBackupCore`
- [x] Infrastructure: `ProcessCommandLine`, `ProcessRunnerLog`, `ProcessRunner`, регистрация в DI
- [x] TestUtilities: помощники чтения лога в `TemporaryContentRoot`, `SerilogFileLoggingTests` на них
- [x] Infrastructure.Tests и Core.Tests: модульные тесты и тесты DI
- [x] Infrastructure.IntegrationTests: `TestScript`, `ProcessRunnerProcessTests`, `ProcessRunnerLoggingTests`
- [x] ArchitectureTests: правила для `System.Diagnostics.Process`
- [x] Сборка, тесты, `dotnet format --verify-no-changes`, проверка комментариев
