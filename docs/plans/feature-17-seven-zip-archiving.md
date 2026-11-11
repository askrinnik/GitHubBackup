# План: #17 F1.10: 7-Zip archiving: name, create, verify, move

| Поле | Значение |
|---|---|
| Issue | [#17](https://github.com/askrinnik/GitHubBackup/issues/17) |
| Заголовок | F1.10: 7-Zip archiving: name, create, verify, move |
| Направление | Feature |
| Сложность | M |
| Дата | 2026-11-11 |

## 1. Цель

Реализовать `IArchiveNameBuilder` (Core) и `IArchiver` (Infrastructure) по §6.7 PRD: FR-7.1–FR-7.7, П-4, с учётом §6.3 (FR-3.9: `<sourcesRoot>\.tmp\`) и §7.2 (`Tools:SevenZipPath`, `Backup:ShortHashLength`, `Backup:VerifyArchive`).

- `IArchiveNameBuilder` строит имя `<owner>_<repo>[.wiki]_<yyyyMMdd-HHmmss>_<shortHash>.zip`. Время берётся из `TimeProvider.GetLocalNow()`.
- `IArchiver` создаёт ZIP в `<sourcesRoot>\.tmp\`, корень внутри архива — папка клона вместе с `.git`. Затем архив проверяется через `7z t` (если проверка включена) и переносится в папку архивов. При ошибке и при отмене временные файлы удаляются (NFR-4).

## 2. Критерии приёмки

- Integration test: the archive extracts into a working repository (`git fsck`)
- Unit test of the name (with a TimeProvider fake)
- Test: a failed verification leaves no files in the archive folder
- Covered by tests, CI green
- (добавлено) Длина `shortHash` равна `Backup:ShortHashLength` (по умолчанию 10, допустимо 4–40, §7.2). Время форматируется инвариантно.
- (добавлено, FR-5.3) Для wiki имя — `<owner>_<repo>.wiki_…`.
- (добавлено, FR-7.2) Все записи архива начинаются с `<repo>/`, `.git` входит в архив; после распаковки `git status --porcelain` пуст.
- (добавлено, FR-7.5, П-4) При `VerifyArchive=false` команда `7z t` не запускается.
- (добавлено, FR-7.4) На одном томе — `Move`; на разных — копирование во временное имя в папке назначения, переименование, удаление исходника.
- (добавлено, FR-7.4) Если создание или перенос не удались, в `.tmp` и в папке архивов не остаётся файлов.
- (добавлено, NFR-4) При отмене временные файлы удаляются, `OperationCanceledException` пробрасывается.
- (добавлено, FR-7.7) 7-Zip запускается только через `IProcessRunner`, вывод пишется в лог с `Tool=7z`.
- (добавлено) Ненулевой код 7-Zip даёт исключение с командой, кодом возврата и хвостом stderr.

## 3. Изменения PRD

Не нужны.

## 4. Затрагиваемые проекты и типы

**GitHubBackup.Core**, папка `Archiving\`:

- `IArchiveNameBuilder` — `ArchiveName Build(string owner, string repository, BackupTarget target, string headCommit)`.
- `ArchiveName` — record `(string FileName, DateTimeOffset CreatedAt)`: время в имени совпадает с `createdAt` в файле состояния.
- `ArchiveNameBuilder` — зависит от `TimeProvider` и `IOptions<BackupOptions>`; проверяет owner и repo через `GitHubUrl`, хеш — hex не короче `ShortHashLength`; формат времени инвариантный.
- `IArchiver` — `Task<ArchiveResult> CreateAsync(ArchiveRequest request, CancellationToken cancellationToken)`.
- `ArchiveRequest` — `SevenZipPath`, `SourceFolder`, `TemporaryFolder`, `DestinationFolder`, `FileName`; все пути полные.
- `ArchiveResult` — `(string Path, long SizeBytes)`.
- `ArchiveCommandException(message, command, exitCode)` — по образцу `GitCommandException`.
- `AddGitHubBackupCore` — регистрирует `IArchiveNameBuilder`.

**GitHubBackup.Infrastructure**, папка `Archiving\`:

- `SevenZipArchiver : IArchiver` (internal sealed) — зависит от `IProcessRunner`, `IFileSystem`, `IOptions<BackupOptions>`, `ILogger<SevenZipArchiver>`.
- `SevenZipArchiverLog` — `[LoggerMessage]`: создание, проверка, проверка пропущена, перенос, ошибки очистки и удаления исходника (Warning).
- `AddGitHubBackupInfrastructure` — регистрирует `IArchiver`.

**Пакеты:** `Microsoft.Extensions.TimeProvider.Testing` в `src/Directory.Packages.props` и в `GitHubBackup.Core.Tests.csproj`.

## 5. Подход

1. Имя: `$"{owner}_{repoOrWiki}_{local:yyyyMMdd-HHmmss}_{hash[..len].ToLowerInvariant()}.zip"`, `local = timeProvider.GetLocalNow()`.
2. `CreateAsync`:
   1. Проверить полные пути и существование папки клона.
   2. Создать `TemporaryFolder` и `DestinationFolder`.
   3. Временное имя — `<stem>.<Guid:N>.zip` в `.tmp`.
   4. Создание: рабочая папка — родитель клона; `a -tzip -mcu=on -bsp0 -sccUTF-8 -y -- <tmpPath> <cloneFolderName>`. `--` защищает имя, которое начинается с `-`.
   5. Проверка (если включена): `t -bsp0 -sccUTF-8 -- <tmpPath>`.
   6. Ненулевой код → `ArchiveCommandException` с командой и последними строками stderr.
   7. `ThrowIfCancellationRequested` перед переносом.
   8. Перенос: тома сравниваются по корню пути без учёта регистра. Один том — `File.Move(..., overwrite: false)`. Разные — `Copy` в `<final>.<Guid:N>.tmp`, `Move` в финальное имя, удаление исходника. Существующий файл назначения не перезаписывается (FR-7.6).
   9. Размер — из `FileInfo.Length`.
3. Очистка: `catch` вокруг шагов 4–8 удаляет временные файлы best-effort (как `AtomicFileWriter.TryDelete`) и пробрасывает исключение.
4. Вывод 7z пишет в лог `ProcessRunner`; секретов в аргументах нет.

## 6. Тесты

- `Core.Tests/Archiving/ArchiveNameBuilderTests` (`FakeTimeProvider` с явной зоной): репозиторий, wiki, дополнение нулями, не-григорианская культура, длина хеша 4/10/40, хеш в нижнем регистре, неверный хеш, неверные owner и repo. Плюс случай в `CoreServiceCollectionExtensionsTests`.
- `Infrastructure.Tests/Archiving/SevenZipArchiverTests` (`MockFileSystem`, `FaultInjectingFileSystem`, NSubstitute `IProcessRunner`): успех с проверкой аргументов; сбой проверки не оставляет файлов (критерий приёмки); ненулевые коды `a`; проверка выключена; межтомный перенос; сбой переноса; отмена; файл назначения уже есть; нет папки клона; сбой очистки не скрывает исходную ошибку. Плюс случай в `InfrastructureServiceCollectionExtensionsTests`.
- `Infrastructure.IntegrationTests/Archiving/SevenZipArchiverTests` (настоящий 7z, `LocalGitSandbox`): архив распаковывается в рабочий репозиторий (`git fsck --full`, `git status --porcelain`, HEAD совпадает); вывод 7z в логе; имя папки, начинающееся с `-`.

7-Zip в тестах берётся по пути по умолчанию, как в `ToolLocatorTests`; образ `windows-latest` его содержит.

## 7. Проверка

| Критерий | Как проверяется |
|---|---|
| Распаковка в рабочий репозиторий | `CreateAsync_Clone_ArchiveExtractsIntoWorkingRepository` |
| Unit-тест имени с fake TimeProvider | `ArchiveNameBuilderTests` |
| Неудачная проверка не оставляет файлов | `SevenZipArchiverTests.CreateAsync_VerificationFails_LeavesNoFilesInArchiveOrTemporaryFolder` |
| Остальные ветки | одноимённые тесты из раздела 6 |
| CI зелёный | полная сборка, тесты решения, `dotnet format --verify-no-changes` |

## 8. Вне рамок

- Вызов из оркестратора и запись `ArchiveRecord` (FR-6.4), пропуск пустого репозитория (FR-4.5).
- Очистка «осиротевших» файлов в `.tmp` после аварийного завершения процесса.
- Межтомный перенос на реальных разных дисках — только unit-тест на `MockFileSystem`. Тома, смонтированные в папку или через `subst`, определяются по корню пути; при совпадении корней `File.Move` скопирует файл неатомарно.
- Метрика размера архивов (FR-10.5).

## 9. Решения

- Код 7-Zip `1` (warning, например заблокированный файл) считается ошибкой: архив может быть неполным и не переносится. Альтернатива — считать его успехом с предупреждением.
- Если при межтомном переносе архив уже на месте, а исходник в `.tmp` не удалился, это успех с Warning в логе. Альтернатива — ошибка всего шага.
- Длина хеша берётся из `Backup:ShortHashLength` (по умолчанию 10, §7.2), а не жёстко 10.
- `IArchiveNameBuilder` живёт и регистрируется в Core; `ArchiveName` возвращает и `CreatedAt`.
- Добавлен тестовый пакет `Microsoft.Extensions.TimeProvider.Testing` ради `FakeTimeProvider`.

## 10. Задачи

- [x] Пакет `Microsoft.Extensions.TimeProvider.Testing`
- [x] Core: `IArchiveNameBuilder`, `ArchiveName`, `ArchiveNameBuilder`
- [x] Core: `IArchiver`, `ArchiveRequest`, `ArchiveResult`, `ArchiveCommandException`
- [x] Core: регистрация `IArchiveNameBuilder`
- [x] Core.Tests: `ArchiveNameBuilderTests`, тест регистрации
- [x] Infrastructure: `SevenZipArchiver` и `SevenZipArchiverLog`
- [x] Infrastructure: регистрация `IArchiver`
- [x] Infrastructure.Tests: `SevenZipArchiverTests`, тест регистрации
- [x] Infrastructure.IntegrationTests: `SevenZipArchiverTests`
- [x] Сборка, тесты, `dotnet format --verify-no-changes`; проверка комментариев
