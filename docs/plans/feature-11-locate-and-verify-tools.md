# План: #11 F1.4 — Поиск и проверка git, git-lfs и 7-Zip

| | |
|---|---|
| Issue | [#11](https://github.com/askrinnik/GitHubBackup/issues/11) F1.4: Locate and verify git, git-lfs and 7-Zip |
| Тип работы | Feature |
| Сложность | M |
| Дата | 2026-11-10 |

## 1. Цель

Нужен `IToolLocator` (PRD §8.2). Он находит `git.exe`: путь `Tools:GitPath` или поиск в `PATH` (FR-4.1, §7.2). Он находит `7z.exe`: путь `Tools:SevenZipPath`, по умолчанию `C:\Program Files\7-Zip\7z.exe` (FR-7.1). Он проверяет git-lfs командой `git lfs version`. Он запускает каждый инструмент через `IProcessRunner`, разбирает версию и пишет её в лог. Если инструмента нет, он бросает исключение с понятным сообщением. Вызывающий код превращает это исключение в критическую ошибку с кодом `2` (§4, FR-9.10, §9 шаг 2).

## 2. Критерии приёмки

Из issue (дословно):

1. Тест: неверный путь в настройках даёт понятную ошибку
2. Версии инструментов пишутся в лог
3. Покрыто тестами, CI зелёный

Добавлено по PRD и по сверке с кодом:

4. (добавлено) Если `Tools:GitPath` или `Tools:SevenZipPath` указывает на несуществующий файл, сообщение называет ключ и путь.
5. (добавлено) Если `GitPath` не задан (`null` или пустая строка), git ищется в `PATH` как `git.exe`. Записи `PATH` с неполным путём и пустые записи пропускаются, поэтому текущая папка не просматривается. Кавычки вокруг записи снимаются. Побеждает первая найденная запись.
6. (добавлено) Если в `PATH` нет `git.exe`, сообщение говорит об этом и предлагает задать `Tools:GitPath`.
7. (добавлено) Если `git lfs version` завершился с ненулевым кодом, ошибка сообщает, что Git LFS не установлен.
8. (добавлено) Ненулевой код возврата и `ProcessStartException` при проверке версии дают `ToolUnavailableException`. Сообщение называет инструмент и путь. Исходное исключение сохраняется как inner.
9. (добавлено) Если код возврата `0`, но версию не удалось разобрать, это тоже `ToolUnavailableException`, например когда путь указывает на чужой exe.
10. (добавлено) Отмена пробрасывает `OperationCanceledException` без обёртки.
11. (добавлено) Если `SevenZipPath` пуст, используется путь по умолчанию.
12. (добавлено) `IToolLocator` зарегистрирован в DI как singleton.

## 3. Затрагиваемые проекты и типы

**GitHubBackup.Core** (новая папка `Tools/`):

- `IToolLocator` — `Task<ToolSet> LocateAsync(CancellationToken cancellationToken)`.
- `LocatedTool` (`sealed record`) — `(string Path, string Version)`.
- `ToolSet` (`sealed record`) — `Git`, `GitLfsVersion` (string; git-lfs запускается через git, своего пути у него нет), `SevenZip`.
- `ToolUnavailableException` — по образцу `ProcessStartException`, `(string message, Exception? innerException = null)`. Тип лежит в `Core`: оркестратор и `validate` будут превращать его в `ExitCode.Critical`.

**GitHubBackup.Infrastructure:**

- `Tools/ToolLocator` (`internal sealed`) — зависимости: `IOptions<ToolsOptions>`, `IProcessRunner`, `IFileSystem`, `ILogger<ToolLocator>`. Свойство `internal Func<string, string?> GetEnvironmentVariable { get; init; } = Environment.GetEnvironmentVariable` подменяет `PATH` в тестах, как `ProcessRunner.KillProcessTree`.
- `Tools/ToolVersionParser` (`internal static partial`, `[GeneratedRegex]`) — `TryParseGit` (`git version 2.46.0.windows.1`), `TryParseGitLfs` (`git-lfs/3.5.1 (...)`), `TryParseSevenZip` (`7-Zip 24.08 (x64) : ...`, а также `7-Zip (a) ...`). Каждый метод берёт первую подходящую непустую строку stdout.
- `Tools/ToolLocatorLog` (`[LoggerMessage]`) — `ToolFound`: `"{Tool} {Version} found at {Path}"`, уровень Information.
- `Configuration/ToolsOptions` — константа `DefaultSevenZipPath`; инициализатор свойства использует её.
- `InfrastructureServiceCollectionExtensions` — `TryAddSingleton<IToolLocator, ToolLocator>()` и обновлённый `<summary>`.

**Тесты:** `Infrastructure.Tests/Tools`, `Infrastructure.IntegrationTests/Tools`, `InfrastructureServiceCollectionExtensionsTests`.

## 4. Подход

1. Сначала Core: интерфейс, записи и исключение.
2. `ToolLocator.LocateAsync` выполняет шаги по порядку:
   - git: путь из `GitPath` (проверка `IFileSystem.File.Exists`) или поиск в `PATH` через `IFileSystem`;
   - `git version`;
   - `git lfs version`;
   - 7z: путь из `SevenZipPath` или путь по умолчанию, затем проверка существования;
   - `7z` без аргументов: он печатает заголовок с версией и завершается с кодом `0`.

   Первая ошибка бросает `ToolUnavailableException`, и проверка дальше не идёт.
3. Рабочая папка `ProcessRequest` — папка самого инструмента (`Path.GetDirectoryName`). Она существует по построению и полностью определена.
4. Существование файла проверяется до `IProcessRunner`, хотя runner тоже его проверяет. Так сообщение называет ключ настройки (`Tools:GitPath`), а не только путь.
5. `ProcessStartException` оборачивается в `ToolUnavailableException`. `OperationCanceledException` не перехватывается.
6. Версии пишутся структурированной записью `ToolFound`. Строки вывода уже логирует `ProcessRunner` (FR-10.4).
7. Минимальные версии не проверяются: PRD их не задаёт.
8. Результат не кешируется: каждый вызов проверяет заново.

## 5. Тесты

**`GitHubBackup.Infrastructure.Tests/Tools/ToolVersionParserTests`** (`[Theory]`): настоящие строки git, git-lfs, 7-Zip (x64, x86, `(a)`); пустой вывод; чужой текст; версия не в первой строке.

**`GitHubBackup.Infrastructure.Tests/Tools/ToolLocatorTests`** (`MockFileSystem`, подмена `IProcessRunner` через NSubstitute, `RecordingLogger`, подменённый `PATH`):

- `LocateAsync_AllToolsPresent_ReturnsPathsAndVersions`
- `LocateAsync_AllToolsPresent_LogsEachVersion`
- `LocateAsync_GitPathConfiguredButMissing_ThrowsNamingKeyAndPath`
- `LocateAsync_SevenZipPathConfiguredButMissing_ThrowsNamingKeyAndPath`
- `LocateAsync_GitPathNotSet_FindsGitInPath`
- `LocateAsync_PathHasQuotedEntry_FindsGit`
- `LocateAsync_PathHasRelativeOrEmptyEntries_SkipsThem`
- `LocateAsync_SeveralPathEntriesHoldGit_UsesFirst`
- `LocateAsync_GitNotInPath_ThrowsSuggestingGitPath`
- `LocateAsync_PathVariableMissing_ThrowsSuggestingGitPath`
- `LocateAsync_GitLfsMissing_ThrowsNamingGitLfs`
- `LocateAsync_ToolExitsNonZero_ThrowsWithExitCode` (`[Theory]` по трём инструментам)
- `LocateAsync_ProcessStartFails_WrapsInToolUnavailable`
- `LocateAsync_VersionNotRecognized_Throws` (`[Theory]`)
- `LocateAsync_SevenZipPathEmpty_UsesDefaultPath`
- `LocateAsync_Cancelled_ThrowsOperationCanceled`
- `LocateAsync_RunsToolInItsOwnFolder` (проверка `ProcessRequest.WorkingDirectory` и `Arguments`)

**`GitHubBackup.Infrastructure.IntegrationTests/Tools/ToolLocatorTests`** (настоящие инструменты; на CI-раннере есть git, git-lfs и 7-Zip, §11):

- находит настоящие git, git-lfs и 7z, версии не пустые;
- в `CapturingLogger` есть записи `ToolFound` для трёх инструментов;
- `GitPath` указывает на несуществующий файл во временной папке — ошибка.

**`InfrastructureServiceCollectionExtensionsTests`:** `IToolLocator` разрешается как singleton.

## 6. Проверка

| Критерий | Чем проверяется |
|---|---|
| 1 | `ToolLocatorTests.LocateAsync_GitPathConfiguredButMissing_*`, `..._SevenZipPathConfiguredButMissing_*` (сообщение содержит ключ и путь) |
| 2 | `ToolLocatorTests.LocateAsync_AllToolsPresent_LogsEachVersion`; интеграционный тест на настоящих инструментах |
| 3 | сборка, тесты и `dotnet format --verify-no-changes`; CI на PR |
| 4–12 | соответствующие случаи `ToolLocatorTests`, `ToolVersionParserTests`, DI-тест |

## 7. Вне рамок

- Вызов `IToolLocator` при запуске и перевод `ToolUnavailableException` в `ExitCode.Critical` со строкой в stderr. Это шаг 2 порядка запуска (§9), его реализует оркестратор в [#19](https://github.com/askrinnik/GitHubBackup/issues/19); команда `validate` — [#26](https://github.com/askrinnik/GitHubBackup/issues/26).
- Минимальные версии инструментов: PRD их не задаёт.
- Передача путей в `IGitClient` и `IArchiver`, кеширование `ToolSet`.
- Переменные окружения git (`GIT_TERMINAL_PROMPT` и другие): для `git version` они не нужны, их задаёт `IGitClient`.

## 8. Решения

1. Подключение к CLI с кодом `2` не входит в этот issue. Его делает оркестратор (#19), который реализует §9.
2. Код `0` без распознанной версии — критическая ошибка (`ToolUnavailableException`).
3. Пустой `Tools:SevenZipPath` означает путь по умолчанию.
4. Результат `LocateAsync` не кешируется.
5. Сообщения об ошибке содержат путь из настроек: путь не секретен и делает ошибку понятной.

## 9. Задачи

- [x] Core: `Tools/IToolLocator`, `LocatedTool`, `ToolSet`, `ToolUnavailableException`
- [x] Infrastructure: `ToolsOptions.DefaultSevenZipPath`
- [x] Infrastructure: `Tools/ToolVersionParser` и `ToolVersionParserTests`
- [x] Infrastructure: `Tools/ToolLocatorLog`, `Tools/ToolLocator` (поиск в `PATH`, проверка файлов, запуск версий, обёртка ошибок)
- [x] Infrastructure: регистрация в `AddGitHubBackupInfrastructure`, обновлённый `<summary>`; DI-тест
- [x] Infrastructure.Tests: `Tools/ToolLocatorTests` (все случаи из раздела 5)
- [x] Infrastructure.IntegrationTests: `Tools/ToolLocatorTests` на настоящих инструментах
- [x] Сборка, тесты, `dotnet format --verify-no-changes`, проверка новых комментариев по `csharp.md`
