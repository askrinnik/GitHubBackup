# План: #63 — Architecture cleanup: configuration binding, I/O rule, start-up errors, shared test helpers

- Issue: [#63](https://github.com/askrinnik/GitHubBackup/issues/63)
- Линия: Feature (`type:chore`, `area:core`, `area:infrastructure`, `area:build`)
- Сложность: M
- Дата: 2026-11-09
- PRD: §8.1 «Решение и проекты»; правила слоёв — `CLAUDE.md`, *Repository layout*

## 1. Цель

Убрать расхождения между кодом и правилами архитектуры. Поведение приложения не меняется, кроме пункта 4. Каждый пункт задачи — отдельный коммит на ветке `63-architecture-cleanup`; каждый коммит собирается и проходит тесты сам по себе.

## 2. Критерии приёмки

Из issue, дословно:
1. Core не ссылается на `Microsoft.Extensions.Options.ConfigurationExtensions` и не использует `IConfiguration`
2. Сообщения об ошибках конфигурации при старте не изменились (существующие тесты `Cli.Tests` и `Infrastructure.Tests` проходят)
3. Архитектурный тест падает на тестовой сборке с вызовом `File.Exists` и проходит на решении
4. Тесты на `ConfigurationErrorMessages`: файл конфигурации не найден → ошибка конфигурации; посторонний `FileNotFoundException` → не ошибка конфигурации
5. `TemporaryContentRoot` существует в одном экземпляре
6. Пять коммитов, по одному на пункт; CI зелёный

Добавлено по сверке с кодом:
7. (добавлено) Тест падает, если Microsoft изменит текст исключения привязчика, который разбирает `DescribeBindingFailure` (пункт 1 задачи).
8. (добавлено) Архитектурное правило не срабатывает на разрешённые типы с похожими именами: `Path`, `FileMode`, `FileAccess`, `FileShare`, `System.IO.Abstractions.*`.
9. (добавлено) Повреждённый `appsettings.json` и `--config` без значения по-прежнему дают ошибку конфигурации с тем же текстом; посторонний `InvalidDataException` — не ошибка конфигурации.

## 3. Коммиты

### Коммит 1 — привязка конфигурации в Infrastructure

- `Core/Configuration/SectionBindingExtensions.cs` → `Infrastructure/Configuration/SectionBindingExtensions.cs` (namespace `GitHubBackup.Infrastructure.Configuration`). Тесты `Core.Tests/Configuration/SectionBindingExtensionsTests.cs` → `Infrastructure.Tests/Configuration/`.
- `AddGitHubBackupCore()` без параметров: регистрирует `IRunContext`, `IBackupConfigValidator`, `BackupOptionsValidator` и `AddOptions<BackupOptions>().ValidateOnStart()`.
- `AddGitHubBackupInfrastructure(configuration)` добавляет `services.AddOptions<BackupOptions>().BindSection(configuration, BackupOptions.SectionName)`.
- `GitHubBackupHostBuilder` и тесты вызывают `AddGitHubBackupCore()`.
- `Core.csproj`: `Microsoft.Extensions.Options.ConfigurationExtensions` → `Microsoft.Extensions.Options` (нужны `IValidateOptions`, `OptionsBuilder`) и `Microsoft.Extensions.DependencyInjection.Abstractions`. Версии — в `Directory.Packages.props`. Infrastructure получает `ConfigurationExtensions` транзитивно через `Microsoft.Extensions.Hosting`.
- Тесты `CoreServiceCollectionExtensionsTests`: регистрация без конфигурации; правило валидатора проверяется через `Configure`. Тест привязки секции `Backup` переезжает в `InfrastructureServiceCollectionExtensionsTests`.
- Новый тест (п. 7): реальный `ConfigurationBinder` на неконвертируемом значении бросает `InvalidOperationException` с текстом ровно `Failed to convert configuration value 'abc' at 'Backup:ShortHashLength' to type 'System.Int32'.`
- Архитектурный тест: `Microsoft.Extensions.Configuration` добавляется в запрещённые для Core пространства имён и сборки (`LayerRules`), чтобы правило не вернулось.

### Коммит 2 — формат путей Core описан явно

- `Directory.Build.props`: комментарий у `IsOsIndependentProject` — Core не зависит от API ОС, но пути в домене — пути Windows (буква диска, `\`, сравнение без учёта регистра).
- XML-комментарии `<remarks>` у `BackupConfigValidator` и `ArchiveFolder` — то же. Кода не меняет.

### Коммит 3 — архитектурный тест на прямой доступ к файлам

- `LayerRules.DirectFileSystemTypes`: `System.IO.File`, `System.IO.Directory`, `System.IO.FileInfo`, `System.IO.DirectoryInfo`, `System.IO.FileStream`.
- `FileSystemAccessTests`: Core и Infrastructure не зависят ни от одного из этих типов (NetArchTest `HaveDependencyOnAny`).
- Фикстуры: `DirectFileAccessFixture` (вызывает `File.Exists`) и `AllowedFileTypesFixture` (`Path`, `FileMode`, `FileAccess`, `FileShare`, `IFileSystem`).
- `RuleDetectionTests`: правило падает на первой фикстуре и проходит на второй. Если NetArchTest сравнивает имена по префиксу (`System.IO.File` совпадёт с `System.IO.FileMode`), правило проверяет точные имена типов через Mono.Cecil, который уже приходит с NetArchTest.

### Коммит 4 — `ConfigurationErrorMessages` сужен

- Новый `Infrastructure/Hosting/ConfigurationFileException` (sealed, `Exception`): ошибка файла конфигурации или `--config`. Сообщение — сообщение исходного исключения вместе с внутренними (как `JoinMessages` сейчас), поэтому текст на экране не меняется.
- `GitHubBackupHostBuilder.Create`: `FileNotFoundException` и `InvalidDataException` из `AddJsonFile(appsettings.json)` оборачиваются в `ConfigurationFileException`; `FindConfigPath` бросает `ConfigurationFileException`. XML `<exception>` обновлены.
- `ConfigurationErrorMessages.TryGet`: ветка `FileNotFoundException or InvalidDataException` заменяется на `ConfigurationFileException`. `backup-config.json` уже приходит как `BackupConfigException`.
- Тесты: `ConfigurationFileException` (не найден / повреждён) → ошибка конфигурации; голые `FileNotFoundException` и `InvalidDataException` → `false`. Существующие тесты Cli (нет `appsettings.json`, битый JSON, `--config` без значения) проходят без изменения ожидаемого текста.

### Коммит 5 — общий проект тестовых помощников

- Новый `src/GitHubBackup.TestUtilities` (`net10.0-windows`, ссылка на Infrastructure, `IsPackable=false`, без тестового раннера; имя не кончается на `Tests`, поэтому тестовые настройки `Directory.Build.props` к нему не применяются). Добавлен в `GitHubBackup.slnx`.
- Один `public sealed class TemporaryContentRoot` (namespace `GitHubBackup.TestUtilities`): конструктор `TemporaryContentRoot(string? appSettings = null)` пишет `appsettings.json`, если текст задан; `FolderPath`, `LogsPath`, `BuildHost(params string[] args)`, `Dispose`.
- Удаляются обе копии; `Cli.Tests` и `Infrastructure.IntegrationTests` ссылаются на `TestUtilities`.
- Помощник использует `System.IO` напрямую: правило коммита 3 касается только Core и Infrastructure.
- PRD §8.1: строка `GitHubBackup.TestUtilities\ ← общие помощники тестов` в дереве и фраза, что это не продукт и у него нет пары. Версия 0.7 → 0.8, дата, строка истории.
- `CLAUDE.md`, *Repository layout*: проект в дереве и исключение из правила «у каждого проекта есть тестовый проект».

## 4. Проверка

- После каждого коммита: полная пересборка и тесты решения (`build-runner`, scope `full`), формат.
- В конце — security review (изменения в Infrastructure и в пути старта).

## Решения

- План утверждён без правок.
- По итогам security review повреждённый файл user secrets тоже даёт `ConfigurationFileException`: раньше он считался ошибкой конфигурации, и поведение сохранено.

## 5. Чек-лист

- [x] Коммит 1: привязка конфигурации в Infrastructure
- [x] Коммит 2: формат путей Core описан
- [x] Коммит 3: архитектурный тест на прямой доступ к файлам
- [x] Коммит 4: `ConfigurationErrorMessages` сужен
- [x] Коммит 5: `GitHubBackup.TestUtilities`, PRD §8.1, `CLAUDE.md`
- [x] Security review, проверка критериев

## Вне рамок

- Разбор `--config` в `Cli` — #26.
- Обработка неожиданных исключений при старте — #58.
- Переход Core на `net10.0-windows` — отклонено, Core остаётся на `net10.0`.
