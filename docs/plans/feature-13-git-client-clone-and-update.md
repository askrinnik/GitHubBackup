# План: #13 F1.6: Git client: clone and update

| | |
|---|---|
| Issue | [#13](https://github.com/askrinnik/GitHubBackup/issues/13) |
| Название | F1.6: Git client: clone and update |
| Lane | Feature |
| Сложность | L |
| Дата | 2026-11-10 |

## 1. Цель

Сделать первую часть `IGitClient` (абстракция в `Core`, реализация в `Infrastructure`). Клиент выполняет первичное клонирование (FR-4.2): все ветки и теги, checkout нужной ветки, рекурсивные сабмодули, LFS только для этой ветки. Он же обновляет клон строго по последовательности FR-4.3. Каждый вызов git получает `core.longpaths=true` (FR-4.6). Весь вывод логируется, ошибку определяет код возврата (FR-4.7). Токен передаётся только через `GIT_CONFIG_COUNT/KEY/VALUE` с ключом `http.https://github.com/.extraHeader`, интерактивные запросы отключены (FR-11.3, NFR-1).

## 2. Критерии приёмки

Из issue (дословно):
1. Интеграционные тесты: первичный клон, обновление после коммита, после force-push, с сабмодулем
2. Тест: после клона `.git/config` не содержит токена
3. Покрыто тестами, CI зелёный

Добавлено по сверке с PRD и кодом:
- (добавлено) Токена нет и в `.git/modules/**/config` сабмодулей. Токена нет в аргументах процессов и в логе (FR-11.3, NFR-1).
- (добавлено) Анонимный режим: без токена нет записи `extraHeader`. `core.longpaths`, `GIT_TERMINAL_PROMPT=0` и `GCM_INTERACTIVE=never` задаются всегда.
- (добавлено) Ненулевой код возврата любого шага даёт `GitCommandException`. Сообщение содержит команду, код и замаскированный stderr. Следующие шаги не запускаются.
- (добавлено) Отмена пробрасывает `OperationCanceledException`, следующие шаги не запускаются.
- (добавлено) Update удаляет локальные изменения, неотслеживаемые и игнорируемые файлы (`clean -ffdx`). Он подтягивает удалённые и перемещённые теги и удалённые ветки (`--prune --prune-tags --tags --force`).
- (добавлено) Ветка, которая начинается с `-`, или пустая ветка отклоняется до запуска git.

## 3. Изменения PRD

Не нужны.

## 4. Затрагиваемые проекты и типы

**GitHubBackup.Core** (`Git/`):
- `IGitClient`: `Task CloneAsync(GitRepositoryRequest request, string remoteUrl, CancellationToken cancellationToken)` и `Task UpdateAsync(GitRepositoryRequest request, CancellationToken cancellationToken)`.
- `GitRepositoryRequest` (`sealed record`): `required string GitPath`, `required string ClonePath`, `required string Branch`, `string? Token`. `PrintMembers` не печатает `Token`, как в `ProcessEnvironmentVariable`.
- `GitCommandException`: `ExitCode`, `Command` (замаскированная строка), сообщение с путём клона и последними строками stderr.

**GitHubBackup.Infrastructure** (`Git/`):
- `GitClient` (`internal sealed`): зависит от `IProcessRunner`, `IFileSystem`, `ISecretMasker` и `ILogger<GitClient>`. Свойство `internal IReadOnlyList<KeyValuePair<string,string>> AdditionalConfiguration { get; init; } = []` для тестов (передают `protocol.file.allow=always`).
- `GitEnvironment` (`internal static`): собирает `ProcessEnvironmentVariable` для вызова и формирует заголовок авторизации.
- `GitClientLog` (`[LoggerMessage]`).
- `InfrastructureServiceCollectionExtensions`: `TryAddSingleton<IGitClient, GitClient>()` и строка в XML-doc.

**Тесты:** `GitHubBackup.Infrastructure.Tests/Git/GitClientTests.cs`, `GitHubBackup.Core.Tests/Git/GitRepositoryRequestTests.cs`, `GitHubBackup.Infrastructure.IntegrationTests/Git/GitClientTests.cs`.

## 5. Подход

1. **Core.** `IGitClient`, `GitRepositoryRequest`, `GitCommandException` с XML-doc. Путь к git и токен приходят в запросе.
2. **Окружение git** (`GitEnvironment.Create(token, additional)`):
   - всегда `GIT_TERMINAL_PROMPT=0` и `GCM_INTERACTIVE=never`;
   - записи `GIT_CONFIG_KEY_n/VALUE_n`: `core.longpaths=true`, затем `AdditionalConfiguration`, затем (только с токеном) `http.https://github.com/.extraHeader` = `Authorization: Basic <base64("x-access-token:" + token)>` с `IsSecret: true`;
   - `GIT_CONFIG_COUNT` равен числу записей.
3. **Маскирование.** `ProcessRunner` регистрирует секретную переменную. `GitClient` дополнительно регистрирует в `ISecretMasker` токен и его base64-форму. Stderr в `GitCommandException` проходит через `Mask`.
4. **Clone** (FR-4.2): создать родительскую папку через `IFileSystem`; `git clone --no-checkout --origin origin -- <url> <ClonePath>`; затем шаги 2–7 FR-4.3 (`SyncWorkingTreeAsync`). `--no-checkout` не скачивает LFS при clone; `git lfs pull` берёт объекты только для текущего ref.
5. **Update** (FR-4.3): `fetch origin --prune --prune-tags --tags --force`, затем `SyncWorkingTreeAsync`.
6. **Шаг** (`RunGitAsync`): `ProcessRequest` с `GitPath`, рабочей папкой, аргументами списком и окружением; `!Succeeded` → `GitCommandException`; `OperationCanceledException` и `ProcessStartException` не перехватываются.
7. **Валидация.** `Branch` не пустая и не начинается с `-`. URL идёт после `--`.
8. **LFS.** `git lfs pull` выполняется без условий: `IToolLocator` уже требует git-lfs, в CI он есть.
9. **DI.** Регистрация сервиса.

## 6. Тесты

**Unit** (`Infrastructure.Tests/Git/GitClientTests`, фейковый `IProcessRunner`, `MockFileSystem`, настоящий `SecretMasker`):
- последовательность аргументов clone и update, рабочие папки;
- создание родительской папки;
- prompts и `core.longpaths` на каждом шаге;
- с токеном — Basic extraHeader как секрет и верный `GIT_CONFIG_COUNT`; без токена — нет extraHeader; токена нет в аргументах;
- сбой шага → `GitCommandException`, остановка, замаскированный stderr;
- отмена → `OperationCanceledException`, остановка;
- `ProcessStartException` пробрасывается;
- пустая ветка и ветка с `-` отклоняются до запуска;
- `GitRepositoryRequest.ToString` без токена (`Core.Tests`).

**Интеграционные** (`Infrastructure.IntegrationTests/Git/GitClientTests`, `LocalGitSandbox`, настоящий `ProcessRunner`):
- клон: ветка, все `origin/*` и теги; не-дефолтная ветка;
- update после нового коммита; после force-push;
- update сбрасывает локальные, неотслеживаемые и игнорируемые файлы;
- update зеркалит удалённую ветку и перемещённый или удалённый тег;
- вложенные сабмодули (два уровня); продвинутый сабмодуль при update;
- `.git/config` и `.git/modules/**/config` без токена, base64 и `extraheader`; лог без токена;
- путь длиннее 260 символов;
- отсутствующий remote и отсутствующая ветка → `GitCommandException`.

## 7. Проверка

| Критерий | Как проверяется |
|---|---|
| 1 | Интеграционные тесты клона, обновления после коммита, после force-push, с сабмодулями |
| 2 | Интеграционный тест конфигов без токена |
| 3 | Полный rebuild, тесты решения, `dotnet format --verify-no-changes`, CI на PR |
| Добавленные | Unit-тесты из п. 6 |

## 8. Вне рамок

- FR-4.4: ветка по умолчанию и fallback со статусом `Warning`. Сейчас отсутствующая ветка даёт `GitCommandException`.
- FR-4.5: пустой репозиторий.
- FR-5.x (wiki), FR-6.2 (`ls-remote`, снимки ссылок).
- Решение «clone или update» и удаление частичного клона — оркестратор.
- Цепочка источников токена (FR-11.1).
- LFS-файлы внутри сабмодулей; интеграционный тест с реальными LFS-объектами на `file://`.
- Изоляция от глобального `.gitconfig` машины.

## 9. Решения

1. Заголовок: `Authorization: Basic base64("x-access-token:<token>")`, как в `actions/checkout`.
2. Путь к git и токен передаются в `GitRepositoryRequest` при каждом вызове; `ToolSet` в DI не регистрируется.
3. Clone через `--no-checkout` плюс общие шаги 2–7 FR-4.3.
4. `git lfs pull` без условного пропуска; интеграционные тесты требуют git-lfs, как `ToolLocatorTests`.
5. Интеграционные тесты не изолируются от глобального `.gitconfig`.

## 10. Задачи

- [x] Core: `Git/IGitClient`, `Git/GitRepositoryRequest`, `Git/GitCommandException`
- [x] Infrastructure: `Git/GitEnvironment`
- [x] Infrastructure: `Git/GitClient`
- [x] Infrastructure: `Git/GitClientLog`
- [x] Infrastructure: регистрация `IGitClient` и XML-doc
- [x] Unit-тесты `Infrastructure.Tests/Git/GitClientTests` и `Core.Tests/Git/GitRepositoryRequestTests`
- [x] Интеграционные тесты `Infrastructure.IntegrationTests/Git/GitClientTests`
- [x] Build, тесты и format check зелёные; комментарии в diff проверены
