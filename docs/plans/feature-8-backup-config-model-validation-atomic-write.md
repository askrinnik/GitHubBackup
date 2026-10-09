# План: #8 — F1.1: backup-config.json model, validation and atomic write

- Issue: [#8](https://github.com/askrinnik/GitHubBackup/issues/8)
- Линия: Feature (`type:feature`, `area:core`, `area:infrastructure`)
- Сложность: L
- Дата: 2026-10-09
- PRD: §7.3, §6.1 (FR-1.2–FR-1.4, FR-1.7), §6.2 (FR-2.8), §6.3 (FR-3.6–FR-3.8); также §7.2 (`Backup:ConfigPath`)

## 1. Цель

Описать в коде модель `backup-config.json` (§7.3) и реализовать `IBackupConfigStore` (§8.2). Сервис читает файл по пути из `Backup:ConfigPath` (§7.2) или из `--config` (FR-9.2), проверяет его и записывает атомарно: через временный файл, предыдущая версия уходит в `backup-config.json.bak` (FR-2.8, NFR-2).

Проверки при загрузке:
- пути только абсолютные (FR-3.6);
- URL указывают на github.com (FR-1.1, FR-1.4);
- нет дублей по ID и по имени (FR-1.7, §7.3);
- нет двух записей с одной итоговой папкой архивов (FR-3.1–FR-3.3, FR-3.7);
- предупреждение, если `sourcesRoot` лежит внутри дерева архивов (FR-3.8).

Модель покрывает FR-1.2–FR-1.4, FR-1.7, FR-2.4 (`status`).

## 2. Критерии приёмки

Из issue, дословно:
1. Unit tests for every validation rule
2. Snapshot test (Verify) of the saved file
3. A failure during write does not corrupt the original file
4. Covered by tests, CI green

Добавлено по PRD и по сверке с кодом:
5. (добавлено, FR-2.8) После записи прежнее содержимое лежит в `backup-config.json.bak`. При первой записи, когда исходного файла нет, `.bak` не создаётся. Временный файл не остаётся ни после успеха, ни после сбоя.
6. (добавлено, §7.3) Сохранённый файл совпадает по форме с образцом §7.3: camelCase, `null` пишется явно, `status` строкой, отступ 2 пробела. Образец §7.3 проходит загрузку и сохранение без изменений.
7. (добавлено) `--config <path>` и `--config=<path>` переопределяют `Backup:ConfigPath`. `--config` без значения даёт ошибку конфигурации (код `2`).
8. (добавлено, FR-9.10) Отсутствующий файл, битый JSON, неподдерживаемая `schemaVersion` и ошибки валидации дают `BackupConfigException` со списком сообщений. `ConfigurationErrorMessages.TryGet` распознаёт это исключение, поэтому при подключении к старту получится код `2`.
9. (добавлено, security) Сообщения об ошибках называют место в файле (`accounts[1].url`) и не повторяют значение URL: в URL может оказаться вставленный токен.

## 3. Изменения PRD

Не требуются при вариантах по умолчанию в открытых вопросах.

## 4. Затрагиваемые проекты и типы

**Что уже есть в коде:**
- `BackupOptions.ConfigPath` (по умолчанию `backup-config.json`) и `BackupOptionsValidator` (не пустой путь).
- `ContentRootPathPostConfigure` делает путь абсолютным от папки exe.
- Опции `--config` нет. `System.CommandLine` не подключён; `GitHubBackupHostBuilder.IsConfigurationArgument` пропускает только `--Section:Key=value`.
- `System.IO.Abstractions` и `TestingHelpers` в `Directory.Packages.props` отсутствуют.
- В production-коде нет ни `IFileSystem`, ни `System.Text.Json`.

**`src/Directory.Packages.props`** — `System.IO.Abstractions`, `System.IO.Abstractions.TestingHelpers` (последние стабильные, проверка `dotnet list package --vulnerable`).

**GitHubBackup.Core**, namespace `GitHubBackup.Core.BackupConfiguration`, если не сказано иное:
- `BackupConfig` (`sealed class`, `get; set;`) — `SchemaVersion`, `BackupRoot`, `SourcesRoot?`, `Exclude`, `Accounts`, `Repositories`; списки по умолчанию `[]`.
- `AccountConfig` — `Url`, `Path?`, `Exclude`, `Repositories` (`List<AccountRepositoryConfig>`).
- `AccountRepositoryConfig` — `Name`, `Id` (`long`), `Branch?`, `Path?`, `Status`.
- `RepositoryConfig` — отдельный репозиторий: `Url`, `Id`, `Branch?`, `Path?`, `Status`.
- `RepositoryStatus` (enum) — `Active`, `Unavailable`.
- `GitHub/GitHubUrl` (`GitHubBackup.Core.GitHub`) — `TryParseAccount(string, out string login)`, `TryParseRepository(string, out string owner, out string name)`. Строгий разбор: `https`, хост `github.com` без учёта регистра, без userinfo, порта, query и fragment; один сегмент у аккаунта, два у репозитория; допускается один завершающий `/`; проверка символов login и имени.
- `ArchiveFolder` (`internal static`) — `Resolve(backupRoot, accountPath?, owner, repo, repoPath?)` по FR-3.1–FR-3.3.
- `IBackupConfigValidator`, `BackupConfigValidator` — `BackupConfigValidationResult Validate(BackupConfig config, string defaultSourcesRoot)`; чистая логика.
- `BackupConfigValidationResult` (record) — `Errors`, `Warnings`, `IsValid`.
- `IBackupConfigStore` — `Task<BackupConfigLoadResult> LoadAsync(CancellationToken)`, `Task SaveAsync(BackupConfig, CancellationToken)`.
- `BackupConfigLoadResult` (record) — `Config`, `Warnings`.
- `BackupConfigException` — `IReadOnlyList<string> Errors`; `Message` содержит путь файла и первую ошибку.
- `CoreServiceCollectionExtensions` — `TryAddSingleton<IBackupConfigValidator, BackupConfigValidator>()`.

**GitHubBackup.Infrastructure**:
- `PackageReference` на `System.IO.Abstractions`.
- `BackupConfiguration/JsonBackupConfigStore` (`internal sealed`) — зависимости: `IFileSystem`, `IOptions<BackupOptions>`, `IHostEnvironment`, `IBackupConfigValidator`, `ILogger<JsonBackupConfigStore>`.
- `BackupConfiguration/BackupConfigJsonContext` — source-generated `JsonSerializerContext`.
- `BackupConfiguration/BackupConfigLog` — `[LoggerMessage]` для предупреждений и сохранения.
- `InfrastructureServiceCollectionExtensions` — `TryAddSingleton<IFileSystem, FileSystem>()`, `TryAddSingleton<IBackupConfigStore, JsonBackupConfigStore>()`.
- `Hosting/ConfigurationErrorMessages` — ветка `BackupConfigException => [.. Errors]`.
- `Hosting/GitHubBackupHostBuilder` — `--config <path>` и `--config=<path>`; относительный путь — от текущего каталога; `AddInMemoryCollection` с `Backup:ConfigPath` после `AddCommandLine`; без значения — `InvalidDataException`.

**Корень репозитория**: без изменений (`.gitattributes`, `.gitignore`, `src/.editorconfig` не содержат правил для снимков).

**Тестовые проекты**: `Core.Tests`, `Infrastructure.Tests` (+ `TestingHelpers`), `Infrastructure.IntegrationTests`, `Cli.Tests`.

## 5. Подход

1. **Core: модель и `GitHubUrl`.** Модель без атрибутов JSON: имена задаёт политика camelCase в Infrastructure.
2. **Core: валидатор.** Собирает все ошибки. Формат: `"<location> <problem>."`, например `accounts[0].repositories[1].path must be an absolute path.` Правила:
   - `schemaVersion == 1`;
   - `backupRoot` задан и `Path.IsPathFullyQualified`;
   - `sourcesRoot`, все `path`: `null` или абсолютные; пустая строка, `C:foo`, `\foo`, недопустимые символы — ошибка;
   - URL аккаунтов — `TryParseAccount`, отдельных репозиториев — `TryParseRepository`; значение URL в сообщение не попадает;
   - `name` репозитория аккаунта — допустимое имя (не `.`/`..`, только `[A-Za-z0-9._-]`);
   - `id > 0`;
   - `branch`: `null` или непустая строка, не начинается с `-`;
   - дубли аккаунтов по login без учёта регистра;
   - дубли по ID во всём файле;
   - дубли по `owner/name` во всём файле без учёта регистра;
   - совпадение итоговых папок архивов: `ArchiveFolder.Resolve` → `Path.GetFullPath` → обрезка `\` → `OrdinalIgnoreCase`; записи с ошибкой пути или URL пропускаются;
   - предупреждение, если эффективный `sourcesRoot` равен `backupRoot` или папке архивов либо лежит внутри неё.
3. **`JsonBackupConfigStore.LoadAsync`.** Нет файла → `BackupConfigException`. Чтение потоком (BOM пропускается). `JsonException`, `null`, пустой файл → `BackupConfigException` со строкой и позицией, без содержимого. Ошибки валидации → исключение; предупреждения — в лог (`Warning`) и в `BackupConfigLoadResult.Warnings`.
4. **`BackupConfigJsonContext`:** camelCase; `JsonStringEnumConverter<RepositoryStatus>`; `WriteIndented`, `IndentSize = 2`, `NewLine = "\r\n"`; `DefaultIgnoreCondition = Never`; `UnsafeRelaxedJsonEscaping`; `RespectNullableAnnotations = true`; `ReadCommentHandling = Skip`, `AllowTrailingCommas = true`; `UnmappedMemberHandling = Disallow`.
5. **`SaveAsync`, атомарная запись.**
   1. Проверить модель валидатором; ошибки → `BackupConfigException`, на диск ничего не пишется.
   2. Сериализовать в память.
   3. Записать во временный `<config>.<random>.tmp` в той же папке (`FileMode.CreateNew`), `Flush(flushToDisk: true)`.
   4. Проверить `cancellationToken`.
   5. Файл есть → `File.Replace(temp, target, target + ".bak")`; нет → `File.Move(temp, target)`.
   6. При исключении удалить временный файл (best effort) и пробросить.
6. **Хост: `--config`** в общем `GitHubBackupHostBuilder`, до валидации опций.
7. **DI** через расширения слоёв. Загрузка при старте (§9, шаг 1) не подключается.

**Риск:** сохранённый файл сравнивается строкой побайтно (CRLF, финальный перевод строки); эталон лежит в тесте.

## 6. Тесты

**`GitHubBackup.Core.Tests`**
- `GitHub/GitHubUrlTests` — валидные и невалидные формы (схема, хост, userinfo, порт, query, fragment, число сегментов, `.git`, `.`/`..`, символы, пустая строка, относительный URL).
- `BackupConfiguration/ArchiveFolderTests` — по умолчанию; `path` аккаунта; `path` репозитория.
- `BackupConfiguration/BackupConfigValidatorTests` — по тесту или Theory на каждое правило из п. 5.2, образец §7.3 валиден, предупреждения FR-3.8 (внутри, равен, внутри папки архивов, `defaultSourcesRoot`), отсутствие предупреждения при `backupRoot` внутри `sourcesRoot`, сбор нескольких ошибок, сообщение без значения URL.
- `CoreServiceCollectionExtensionsTests` — `IBackupConfigValidator` разрешается.

**`GitHubBackup.Infrastructure.Tests`**
- `BackupConfiguration/JsonBackupConfigStoreTests` (`MockFileSystem`) — образец, BOM, отсутствующий список, регистр `status`, нет файла, пустой файл, битый JSON, `null`, неизвестный `status`, неверный тип, `null` в списке, неизвестное свойство, ошибки валидации, предупреждение.
- `BackupConfiguration/JsonBackupConfigStoreSaveTests` — сравнение сохранённого файла с эталоном в тесте (Shouldly), round-trip образца, CRLF, первая запись без `.bak`, вторая и третья запись `.bak`, невалидная модель, сбои записи temp и `Replace` (`FaultInjectingFileSystem`), отмена.
- `ConfigurationErrorMessagesTests`, `InfrastructureServiceCollectionExtensionsTests` — дополнения.

**`GitHubBackup.Infrastructure.IntegrationTests`** — `BackupConfiguration/JsonBackupConfigStoreFileTests`: настоящий `File.Replace` во временной папке.

**`GitHubBackup.Cli.Tests`** (`CliApplicationTests`) — `--config path`, `--config=path`, относительный путь, без значения (код `2`), приоритет над `--Backup:ConfigPath=`.

## 7. Проверка

| Критерий | Чем проверяется |
|---|---|
| 1 | `BackupConfigValidatorTests`, `GitHubUrlTests`, `ArchiveFolderTests` |
| 2 | `JsonBackupConfigStoreSaveTests.SaveAsync_FullConfig_WritesExpectedJson` (эталонный JSON в тесте) |
| 3 | `SaveAsync_*Fails*_KeepsOriginalFile`, `SaveAsync_Cancelled_*`, интеграционный тест |
| 4 | сборка, тесты, `dotnet format`, зелёный CI в PR |
| 5, 6 | тесты `.bak` и round-trip |
| 7 | тесты `--config` в `CliApplicationTests` |
| 8 | `JsonBackupConfigStoreTests`, `ConfigurationErrorMessagesTests` |
| 9 | assert «сообщение не содержит значения URL» |

## 8. Вне рамок

- Загрузка конфигурации при старте CLI и UI (§9, шаг 1) и вывод предупреждений в консоль.
- `IRepositoryPathResolver` целиком, включая папки клонов (FR-3.4, FR-3.5).
- Проверка масок `exclude` и предупреждение «есть в списке и исключён» (FR-1.6).
- Перенос `--config` в `System.CommandLine`.
- Межпроцессная блокировка записи.
- Вложенные папки архивов одной записи в другую.
- Отдельный репозиторий, чей owner — отслеживаемый аккаунт.
- Сохранение комментариев JSON при записи.
- Создание файла при первом запуске.

## 9. Открытые вопросы

1. Нет файла `backup-config.json` — ошибка (`BackupConfigException`), создание файла — позже, в UI.
2. Дубли по имени — полное `owner/name` во всём файле без учёта регистра; дубли аккаунтов по login — тоже ошибка.
3. `sourcesRoot: null` → `<ContentRootPath>\sources`, FR-3.8 проверяется по этому пути.
4. Неизвестные ключи в JSON — ошибка (`UnmappedMemberHandling.Disallow`).
5. URL: один завершающий `/` допустим; `.git`, `http`, `www.github.com`, userinfo — отклоняются. Отсутствующий `status` — `Active`.
6. Отдельный репозиторий с owner из аккаунтов — пока не проверяется.
7. Относительный `--config` — от текущего каталога; `--config` важнее `--Backup:ConfigPath=`.
8. `--config` делается сейчас в `GitHubBackupHostBuilder`.
9. Сохранение невалидной модели — отказ с исключением.
10. Есть ли отдельный issue на `IRepositoryPathResolver`?

## 10. Решения

Пользователь принял варианты по умолчанию по всем открытым вопросам (2026-10-09):

1. Отсутствующий `backup-config.json` — ошибка `BackupConfigException`; создание файла — позже, в UI.
2. Дубли по имени — полное `owner/name` во всём файле без учёта регистра; дубль аккаунта по login — ошибка.
3. `sourcesRoot: null` → `<ContentRootPath>\sources`; FR-3.8 проверяется по этому пути.
4. Неизвестный ключ в JSON — ошибка (`UnmappedMemberHandling.Disallow`).
5. URL только `https://github.com/...`, допустим один завершающий `/`; `.git`, `http`, `www.github.com`, userinfo — отклоняются. Отсутствующий `status` — `Active`.
6. Отдельный репозиторий с owner из аккаунтов не проверяется.
7. Относительный `--config` — от текущего каталога; `--config` важнее `--Backup:ConfigPath=`.
8. `--config` реализуется сейчас в `GitHubBackupHostBuilder`; перенос в `System.CommandLine` — в CLI-фазе.
9. `SaveAsync` отказывается сохранять невалидную модель.
10. Разрешение папок архивов и клонов — issue [#9](https://github.com/askrinnik/GitHubBackup/issues/9); здесь `ArchiveFolder` минимальный, #9 опирается на него.
11. 2026-10-09: Verify исключён из проекта (PRD 0.7); сохранённый файл проверяется сравнением с эталоном в тесте через Shouldly.

PRD: версия 0.7 исключает Verify (§8.3, §11); других изменений нет.

## 11. Задачи

- [x] `Directory.Packages.props`: `System.IO.Abstractions`, `System.IO.Abstractions.TestingHelpers`; проверка `--vulnerable`
- [x] Правила для снимков в `.gitattributes`, `.gitignore`, `.editorconfig` не нужны (Verify исключён)
- [x] Core: `RepositoryStatus`, `BackupConfig`, `AccountConfig`, `AccountRepositoryConfig`, `RepositoryConfig`
- [x] Core: `GitHub/GitHubUrl` и `GitHubUrlTests`
- [x] Core: `ArchiveFolder` и `ArchiveFolderTests`
- [x] Core: `IBackupConfigValidator`, `BackupConfigValidator`, `BackupConfigValidationResult` и `BackupConfigValidatorTests`
- [x] Core: `IBackupConfigStore`, `BackupConfigLoadResult`, `BackupConfigException`; регистрация и тест
- [x] Infrastructure: `BackupConfigJsonContext`, `BackupConfigLog`
- [x] Infrastructure: `JsonBackupConfigStore.LoadAsync` и `JsonBackupConfigStoreTests`
- [x] Infrastructure: `JsonBackupConfigStore.SaveAsync` и `JsonBackupConfigStoreSaveTests` с `FaultInjectingFileSystem` и эталонным JSON в тесте
- [x] Infrastructure: регистрация `IFileSystem` и `IBackupConfigStore`, тест
- [x] Infrastructure: `ConfigurationErrorMessages` распознаёт `BackupConfigException`, тест
- [x] Infrastructure: `--config` в `GitHubBackupHostBuilder` и тесты в `CliApplicationTests`
- [x] IntegrationTests: `JsonBackupConfigStoreFileTests`
- [x] Сборка, тесты и `dotnet format`, проверка новых `///`-комментариев
