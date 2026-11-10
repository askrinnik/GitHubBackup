# План: #9 — F1.2: Resolve archive and clone folders

- Issue: [#9](https://github.com/askrinnik/GitHubBackup/issues/9)
- Линия: Feature (`type:feature`, `area:core`)
- Сложность: S
- Дата: 2026-11-10
- PRD: §6.3 (FR-3.1–FR-3.5, FR-3.9), §13 (П-10), §8.2 (`IRepositoryPathResolver`)

## 1. Цель

Ввести сервис `IRepositoryPathResolver` (§8.2). Он вычисляет для репозитория из `backup-config.json`:

- папку архивов: `<backupRoot>\<owner>\<repo>`; `path` аккаунта заменяет `<backupRoot>\<owner>`; `path` репозитория заменяет путь целиком (FR-3.1–FR-3.3);
- корень клонов: `sourcesRoot` из конфигурации или `<папка exe>\sources` (FR-3.4);
- папку клона `<sourcesRoot>\<owner>\<repo>` и клона wiki `<sourcesRoot>\<owner>\<repo>.wiki`; `path` на клоны не влияет (FR-3.5, П-10);
- папку временных архивов `<sourcesRoot>\.tmp` (FR-3.9).

## 2. Критерии приёмки

Из issue, дословно:
1. Unit-тесты на все комбинации переопределений
2. Чужие и свои репозитории обрабатываются одинаково
3. Покрыто тестами, CI зелёный

Добавлено по PRD и по сверке с кодом:
4. Имена владельца и репозитория, которые выводят папку клона или архивов за пределы её корня (`..`, абсолютный путь), отклоняются с `ArgumentException` (правило безопасности: path escape).
5. Сервис зарегистрирован в DI; папка exe по умолчанию берётся из `IHostEnvironment.ContentRootPath`.

## 3. Затронутые проекты и типы

| Проект | Тип | Изменение |
|---|---|---|
| `GitHubBackup.Core` | `IRepositoryPathResolver` | новый интерфейс в `BackupConfiguration` |
| `GitHubBackup.Core` | `RepositoryPathResolver` | новая реализация; конструктор принимает папку клонов по умолчанию |
| `GitHubBackup.Core` | `ArchiveFolder` | проверка «папка внутри корня» выносится в общий `ContainedFolder` и используется и для клонов |
| `GitHubBackup.Infrastructure` | `InfrastructureServiceCollectionExtensions` | регистрация `IRepositoryPathResolver` с папкой `<ContentRootPath>\sources` |
| `GitHubBackup.Core.Tests` | `RepositoryPathResolverTests`, `ContainedFolderTests` | новые тесты |
| `GitHubBackup.Infrastructure.Tests` | `InfrastructureServiceCollectionExtensionsTests` | тест регистрации |

## 4. Подход

- Методы интерфейса:
  - `GetArchiveFolder(BackupConfig, AccountConfig, AccountRepositoryConfig)` — репозиторий аккаунта; владелец — логин из URL аккаунта.
  - `GetArchiveFolder(BackupConfig, RepositoryConfig)` — отдельный репозиторий; владелец и имя из URL.
  - `GetSourcesRoot(BackupConfig)`, `GetCloneFolder(BackupConfig, owner, repository)`, `GetWikiCloneFolder(BackupConfig, owner, repository)`, `GetTemporaryArchiveFolder(BackupConfig)`.
- Архивы вычисляет существующий `ArchiveFolder.Resolve` — то же правило, что у валидатора (FR-3.7), без второй копии.
- Свои и чужие репозитории идут через одно правило: владелец берётся из URL, отличий по владельцу нет.
- Некорректный URL аккаунта или репозитория — `ArgumentException`. Конфигурация к этому моменту уже прошла валидацию (FR-1.4), так что это ошибка вызывающего кода.
- Папка `.tmp` не пересекается с папками владельцев: логин GitHub не содержит точку.
- Реализация в `Core` не знает папку exe; её передаёт регистрация в `Infrastructure` (как `JsonBackupConfigStore`).

## 5. Тесты

- `RepositoryPathResolverTests`: все комбинации `path` аккаунта и репозитория (нет / только аккаунт / только репозиторий / оба) для репозитория аккаунта; отдельный репозиторий без `path` и с `path`; свой и чужой владелец дают одинаковое правило; репозиторий аккаунта и отдельный репозиторий того же владельца дают одну папку; клоны и wiki не зависят от `path`; `sourcesRoot` по умолчанию и заданный; `.tmp`; некорректные URL и имена с `..` отклоняются.
- `ContainedFolderTests`: перенос проверок выхода за корень.
- Тест DI-регистрации в `Infrastructure.Tests`.

## 6. Вне рамок

- Создание папок на диске — задача потребителей (клонирование, архивация).
- Проверки FR-3.6–FR-3.8 уже сделаны в #8.

## 7. Открытые вопросы

Нет.

## 8. Решения

- Открытых вопросов не было, поэтому реализация началась сразу после плана (по указанию пользователя).
- `RepositoryPathResolver` публичный: его создаёт регистрация в `Infrastructure`, которая знает папку exe.
- По итогам проверки безопасности (I1) `GetCloneFolder` и `GetWikiCloneFolder` отклоняют имена, которые не проходят `GitHubUrl.IsValidLogin` и `GitHubUrl.IsValidRepositoryName`: разделители, имена устройств, завершающие точка и пробел.

## 9. Задачи

- [x] `ContainedFolder` и перевод `ArchiveFolder` на него
- [x] `IRepositoryPathResolver` и `RepositoryPathResolver`
- [x] Регистрация в DI
- [x] Тесты
- [x] Сборка, тесты, формат
