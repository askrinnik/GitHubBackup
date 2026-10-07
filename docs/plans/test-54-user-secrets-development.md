# План: #54 Test user-secrets in the Development environment

- Issue: [#54](https://github.com/askrinnik/GitHubBackup/issues/54)
- Лейн: Test-authoring (`type:test`)
- Сложность: S
- Дата: 2026-10-07
- PRD: 7.2 (порядок источников: `appsettings.json` < user-secrets только в `Development` < переменные `GitHubBackup__` < командная строка) — изменений PRD нет

## Цель

Покрыть тестами ветку `IsDevelopment()` → `AddUserSecrets(UserSecretsId, reloadOnChange: false)` в `GitHubBackupHostBuilder.Create`. Production-код не меняется.

## Критерии приёмки

- [x] Тест: в `Development` значение из user-secrets перекрывает `appsettings.json`
- [x] Тест: в `Production` user-secrets не читаются
- [x] Тест: переменная окружения перекрывает user-secrets
- [x] Тест не обращается к реальному профилю пользователя
- [ ] Покрыто тестами, CI зелёный
- [x] (добавлено) в `Development` без файла `secrets.json` хост собирается, значение берётся из `appsettings.json`
- [x] (добавлено) user-secrets не читаются и в `Staging`
- [x] (добавлено) аргумент `--Section:Key=value` перекрывает user-secrets

## Затрагиваемые проекты

`GitHubBackup.Cli.Tests`: новый `UserSecretsSourceTests` в коллекции `ProcessEnvironmentTestGroup`. Переиспользуются `TemporaryContentRoot`, `CliApplicationTests.CreateSettings` и `ValidAppSettings`, `EnvironmentVariableOverrideTests.PrefixedVariable`, `InMemoryFileProvider`.

## Подход

1. Тесты лежат в `Cli.Tests`: там уже есть тесты приоритета источников и непараллельная коллекция для тестов, меняющих переменные процесса.
2. Путь к хранилищу секретов вычисляется `PathHelper` в момент вызова `AddUserSecrets` из `%APPDATA%`. Конструктор теста сохраняет `APPDATA` и префиксную переменную, создаёт временный корень и выставляет `APPDATA` на него; `Dispose` всё восстанавливает и удаляет папки. Шов в production не нужен.
3. `WriteUserSecrets(json)` пишет `<fakeAppData>\Microsoft\UserSecrets\GitHubBackup\secrets.json`; id — литерал `"GitHubBackup"`, а не константа хоста, чтобы тест закреплял id из issue.
4. Окружение: `CreateSettings(...) with { EnvironmentName = Environments.Development }`.
5. Проверяемый ключ `Backup:ShortHashLength` (4–40): appsettings 10, secrets 14, переменная 12, аргумент 20; читается через `IOptions<BackupOptions>`.
6. Тест Development с тем же `APPDATA` доказывает, что файл читается, поэтому значение 10 в Production означает пропуск источника.

## Тесты

| Тест | Окружение | secrets.json | Переменная / аргумент | Ожидание |
|---|---|---|---|---|
| `BuildHost_DevelopmentWithUserSecret_OverridesAppSettings` | Development | 14 | — | 14 |
| `BuildHost_NotDevelopmentWithUserSecret_IgnoresUserSecrets` (теория: Production, Staging) | Production / Staging | 14 | — | 10 |
| `BuildHost_DevelopmentWithoutSecretsFile_UsesAppSettings` | Development | нет | — | 10 |
| `BuildHost_PrefixedVariable_OverridesUserSecret` | Development | 14 | `GitHubBackup__Backup__ShortHashLength=12` | 12 |
| `BuildHost_CommandLineArgument_OverridesUserSecret` | Development | 14 | `--Backup:ShortHashLength=20` | 20 |

## Вне рамок

- Получение токена `GitHubBackup:GitHub:Token` из user-secrets (FR-11.1) — отдельная функциональность.
- `UserSecretsId` в WPF-хосте.
- Регистр имени окружения — поведение фреймворка.

## Решения

- Подмена `APPDATA` в непараллельной коллекции допустима вместо шва в production.
- Отдельный класс `UserSecretsSourceTests`, а не дополнение `EnvironmentVariableOverrideTests`: у него своя подготовка.

## Задачи

- [x] Создать `UserSecretsSourceTests` с подготовкой `APPDATA` и `Dispose`.
- [x] Хелперы `WriteUserSecrets` и `ShortHashLength`.
- [x] Пять тестов из таблицы.
- [x] XML-doc класса и хелперов; комментарии без ссылок на issue.
- [x] Сборка, тесты, `dotnet format`.
