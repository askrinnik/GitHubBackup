# План: #12 F1.5: Test infrastructure: local bare repositories

| | |
|---|---|
| Issue | [#12](https://github.com/askrinnik/GitHubBackup/issues/12) |
| Название | F1.5: Test infrastructure: local bare repositories |
| Лейн | Test-authoring |
| Сложность | M |
| Дата | 2026-11-10 |

## 1. Цель

Нужна общая тестовая инфраструктура для интеграционных git-сценариев без сети (PRD [§11](../PRD.md#11-тестирование), строка «Интеграционные (git)»; CI на `windows-latest`). Песочница создаёт во временной папке bare-репозиторий («сервер») и рабочий клон для коммитов. Через неё тесты собирают нужное состояние: коммиты, ветки, лёгкие и аннотированные теги, force-push, удаление ветки, сабмодули, ветка по умолчанию, пустой репозиторий. После теста песочница сама удаляет временные папки. Код продукта не меняется. Сама инфраструктура покрыта самопроверочными тестами.

## 2. Критерии приёмки

Из issue, дословно:
- [ ] Пример теста использует фикстуру и проходит в CI
- [ ] Временные папки удаляются после тестов
- [ ] Покрыто тестами, CI зелёный

Добавлено по PRD §11 и сверке покрытия:
- (добавлено) Git в песочнице изолирован от машинной конфигурации. Глобальный и системный конфиг не читаются. `user.name`, `user.email`, `init.defaultBranch=main`, `core.autocrlf=false` и отключённая подпись задаются явно. Интерактивные запросы отключены.
- (добавлено) Remote адресуется через `file://` URL. Сабмодули работают при `protocol.file.allow=always`.
- (добавлено) Есть помощник «сменить ветку по умолчанию на сервере» (`HEAD` bare-репозитория) для сценария «откат на ветку по умолчанию».
- (добавлено) Если git-команда помощника падает, тест получает исключение с командой, кодом выхода и stderr.
- (добавлено) Удаление работает и для read-only объектов `.git`. Повторный `Dispose` не падает.

## 3. Затрагиваемые проекты и типы

**`GitHubBackup.TestUtilities`.** Песочница живёт здесь: позже её переиспользуют CLI end-to-end тесты в `GitHubBackup.Cli.Tests`.
- `TemporaryFolder` (новый, `static`). Создаёт уникальную папку `%TEMP%\GitHubBackup.Tests\<guid>`. Удаляет дерево: снимает `ReadOnly` со всех файлов, затем делает ограниченный повтор при `IOException`/`UnauthorizedAccessException`. Отсутствующая папка не вызывает ошибку.
- `TemporaryContentRoot` (изменён). Создаёт и удаляет папку через `TemporaryFolder`. Публичное поведение не меняется.
- `Git/GitCommandRunner` (новый, `internal`). Запускает `git.exe` через `System.Diagnostics.Process`: `ArgumentList`, явная рабочая папка, асинхронное чтение stdout и stderr, kill дерева при отмене, исключение с командой, кодом и stderr при ненулевом коде выхода, детерминированное окружение.
- `Git/LocalGitSandbox` (новый, `public sealed`, `IDisposable`). Владеет корневой временной папкой, пустым глобальным конфигом и путём к `git.exe` (параметр или первый подходящий элемент `PATH`).
  - `CreateRemoteAsync(name, ct)` — bare-репозиторий с первым коммитом на `main`;
  - `CreateEmptyRemoteAsync(name, ct)` — bare-репозиторий без коммитов;
  - `CreateFolder(name)` — папка для кода под тестом;
  - `RunGitAsync(workingDirectory, ct, params string[] args)` — произвольная git-команда, возвращает строки stdout.
- `Git/LocalGitRemote` (новый, `public sealed`). Пара «bare + рабочий клон». Свойства `BarePath`, `Url` (`file:///…`), `WorkingPath`. Помощники публикуют результат на сервер сразу:
  - `CommitAsync`, `CreateBranchAsync`, `CheckoutAsync`, `DeleteBranchAsync`;
  - `CreateTagAsync` (лёгкий или аннотированный, `force`), `DeleteTagAsync`;
  - `ForcePushAsync`, `SetDefaultBranchAsync`, `AddSubmoduleAsync`, `GetRemoteRefsAsync`.

**`GitHubBackup.Infrastructure.IntegrationTests`.** Самопроверочные тесты в папке `TestInfrastructure/`.

**`CLAUDE.md`.** В строке `GitHubBackup.TestUtilities` раздела *Repository layout* упомянуть `LocalGitSandbox`; `docs/ai/README.md` — проверить.

Новых пакетов нет.

## 4. Подход

```
TemporaryFolder (+ TemporaryContentRoot)
        |
GitCommandRunner (окружение, запуск)
        |
LocalGitSandbox -> LocalGitRemote
        |
самопроверочные тесты в IntegrationTests
```

1. **Жизненный цикл.** В `TestUtilities` нет xUnit, поэтому песочница — обычный `IDisposable`. Тестовый класс создаёт её в конструкторе и освобождает в `Dispose`. Каждый тест получает свою папку.
2. **Запуск git.** Собственный `GitCommandRunner`, а не `ProcessRunner`: `ProcessRunner` — `internal`, и подготовка сценария не должна зависеть от кода под тестом.
3. **Окружение каждого git-процесса.**
   - удалить унаследованные `GIT_DIR`, `GIT_WORK_TREE`, `GIT_INDEX_FILE`, `GIT_CONFIG_PARAMETERS`, `GIT_CONFIG_COUNT*`;
   - `GIT_CONFIG_NOSYSTEM=1`, `GIT_CONFIG_GLOBAL=<root>\.gitconfig` (пустой файл);
   - `GIT_TERMINAL_PROMPT=0`, `GCM_INTERACTIVE=never`;
   - через `GIT_CONFIG_COUNT/KEY/VALUE`: `user.name`, `user.email`, `init.defaultBranch=main`, `core.autocrlf=false`, `commit.gpgsign=false`, `tag.gpgsign=false`, `protocol.file.allow=always`, `core.longpaths=true`, `credential.helper=`.
4. **Создание remote.** `git init --bare`; рабочий клон — `git init -b main` и `remote add origin <file-URL>`.
5. **Аргументы.** Имена веток и тегов — отдельные аргументы; где git допускает, перед ними `--`.
6. **Удаление.** `LocalGitSandbox.Dispose` вызывает `TemporaryFolder.Delete`.

## 5. Тесты

`GitHubBackup.Infrastructure.IntegrationTests/TestInfrastructure/`. Результат проверяется независимым путём через `RunGitAsync` (`ls-remote`, `cat-file`, `merge-base`, `clone`).

**`LocalGitSandboxTests`**
- `CreateRemoteAsync_NewRemote_CreatesBareRepositoryWithMainBranch`
- `Clone_RemoteUrl_ChecksOutLatestCommit` — пример теста из критериев
- `CreateEmptyRemoteAsync_NewRemote_HasNoRefs`
- `RunGitAsync_CommandFails_ThrowsWithCommandAndError`
- `RunGitAsync_GlobalConfig_IsIsolated`
- `Dispose_ReadOnlyObjectFiles_DeletesRootFolder`
- `Dispose_CalledTwice_DoesNotThrow`

**`LocalGitRemoteTests`**
- `CommitAsync_TwoCommits_RemoteBranchPointsToSecondWithFirstAsParent`
- `CreateBranchAsync_FromStartPoint_PublishesBranchAtThatCommit`
- `DeleteBranchAsync_ExistingBranch_RemovesRefFromRemote`
- `DeleteBranchAsync_MissingBranch_Throws`
- `CreateTagAsync_Lightweight_PublishesTagPointingToCommit`
- `CreateTagAsync_Annotated_PublishesTagObject`
- `CreateTagAsync_ForceExistingTag_MovesTag`
- `DeleteTagAsync_ExistingTag_RemovesRefFromRemote`
- `ForcePushAsync_ToEarlierCommitThenCommit_RewritesHistory`
- `SetDefaultBranchAsync_OtherBranch_ChangesRemoteHead`
- `AddSubmoduleAsync_OtherRemote_CloneWithSubmodulesContainsItsFiles`

## 6. Проверка

| Критерий | Как проверить |
|---|---|
| Пример теста с фикстурой проходит в CI | `Clone_RemoteUrl_ChecksOutLatestCommit`; job CI зелёный на PR |
| Временные папки удаляются | `Dispose_*`; после прогона `%TEMP%\GitHubBackup.Tests` пуст |
| Покрыто тестами, CI зелёный | полная сборка, тесты решения, `dotnet format --verify-no-changes`; CI на PR |

## 7. Вне рамок

- Тесты будущего `GitClient` (clone, fetch, обновление, ls-remote) — в issues, которые реализуют эти операции.
- Git LFS в песочнице — решат LFS-сценарии FR-4.2.
- Сценарии 7-Zip.
- Подмена адреса GitHub на `file://` в CLI e2e.
- Фиксированные даты коммитов.

## 8. Решения

- Песочница живёт в `GitHubBackup.TestUtilities`, самотесты — в `Infrastructure.IntegrationTests/TestInfrastructure/`.
- Git запускается собственным `GitCommandRunner` (`Process`); `InternalsVisibleTo` в продуктовый проект не добавляется.
- `CreateRemoteAsync` сразу делает первый коммит на `main`; помощники сразу публикуют изменения на сервер.
- Правка `TemporaryContentRoot` (общий `TemporaryFolder`) входит в этот issue.

Пользователь разрешил перейти к реализации без отдельного ревью, если в плане нет вопросов; открытые вопросы планировщика были подтверждениями вариантов по умолчанию и приняты как решения выше.

## 9. Задачи

- [x] `TestUtilities/TemporaryFolder.cs`
- [x] `TemporaryContentRoot` на `TemporaryFolder`
- [x] `TestUtilities/Git/GitCommandRunner.cs`
- [x] `TestUtilities/Git/LocalGitSandbox.cs`
- [x] `TestUtilities/Git/LocalGitRemote.cs`
- [x] XML doc для всех типов и членов
- [x] `IntegrationTests/TestInfrastructure/LocalGitSandboxTests.cs` (7 тестов)
- [x] `IntegrationTests/TestInfrastructure/LocalGitRemoteTests.cs` (11 тестов)
- [x] `CLAUDE.md`, *Repository layout*; проверить `docs/ai/README.md`
- [x] Сборка, тесты решения, format; `%TEMP%\GitHubBackup.Tests` пуст после прогона
