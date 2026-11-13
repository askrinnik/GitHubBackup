**Итог.** В четырёх файлах есть 3 критичных проблемы, 4 важных и несколько мелких. Самая серьёзная: `GitClient.CloneAsync` кладёт токен в URL. Токен попадает в аргументы процесса и в `.git/config` клона. Потом `SevenZipArchiver` архивирует клон целиком, и токен оказывается в архиве.

Я читал рабочую копию файлов (в `git status` они изменены), а не diff. Я не открывал `GitHubUrl`, `ContainedFolder`, `ArchiveFolder`, `IProcessRunner` и `ISecretMasker`. Выводы о маскировании и о границах путей зависят от них, поэтому их стоит проверить.

Формат: `<ID правила> <severity> <файл> <член>`. ID берутся из `.claude/rules/security.md`.

## CRITICAL

**1. S1 CRITICAL, `GitClient.cs`, `CloneAsync` (строки 76–87)**
- **Риск:** `remoteUrl.Replace("https://", "https://x-access-token:" + token + "@")` собирает URL с токеном и передаёт его git как аргумент.
  - Токен виден в списке процессов.
  - `git clone` записывает этот URL в `remote.origin.url` внутри `.git/config`. Клон архивируется как есть, поэтому токен попадает в ZIP.
  - Строка 79 пишет `authenticatedUrl` в лог без вызова `secretMasker.Mask`, в отличие от `SetRemoteUrlAsync`.
  - `Replace` не проверяет хост. Для URL не с `github.com` токен уйдёт на чужой хост.
- **Исправление:** передавайте `remoteUrl` без изменений. `CreateEnvironment(request.Token, …)` уже добавляет заголовок `Authorization` через `GIT_CONFIG_*`, он привязан к `https://github.com/`. Удалите `authenticatedUrl` и логируйте `remoteUrl`. Заодно проверьте по I3, что URL имеет вид `https://github.com/<owner>/<repo>`. Если у кого-то уже есть клоны со старым URL, пройдите по ним и выполните `remote set-url` на чистый адрес.

**2. S1/S2 CRITICAL, `GitClient.cs`, `GetRemoteRefsAsync` (строки 121–125)**
- **Риск:** заголовок с токеном передаётся как `-c http.….extraHeader=Authorization: Basic …` в аргументах процесса, то есть виден в списке процессов. Окружение создаётся с `token: null`, поэтому `CreateEnvironment` не вызывает `secretMasker.Register`. Если токен не зарегистрирован раньше, `RunGitAsync` и `ParseOutput` не замаскируют его в тексте исключения (`command`). Он попадёт в сообщение и в логи.
- **Исправление:** используйте `CreateEnvironment(request.Token, request.ClonePath)`, как делает `GetDefaultBranchAsync`. Уберите `-c …` и всегда передавайте `_remoteRefsArguments`.

**3. I1 CRITICAL, `RepositoryPathResolver.cs`, `GetCloneFolder` (строки 44–45)**
- **Риск:** `Path.Combine(root, owner, repository)` не проверяет имена. Они приходят из GitHub API. Если сегмент абсолютный (`C:\x`), `Path.Combine` отбрасывает корень. Сегмент `..` выводит путь за корень. Для имён устройств и конечных точек защиты тоже нет. Затем `GitClient` выполняет в этой папке `reset --hard` и `clean -ffdx`, то есть удаляет файлы. `GetWikiCloneFolder` уже делает всё правильно: `EnsureValidNames` и `ContainedFolder.Combine`.
- **Исправление:** вызовите в `GetCloneFolder` те же `EnsureValidNames` и `ContainedFolder.Combine`. Проверьте, что `GetArchiveFolder` и `ArchiveFolder.Resolve` делают то же с `account.Path`, `repository.Name` и `repository.Path` из конфигурации.

## IMPORTANT

**4. I2 IMPORTANT, `GitClient.cs`, `ValidateBranch` и `UpdateAsync` (строки 110, 198–205)**
- **Риск:** `ValidateBranch` проверяет только пустую строку, хотя документация `SyncWorkingTreeAsync` обещает, что ветка не начинается с `-`. В `fetch origin <branch> …` перед веткой нет `--`. Имя вроде `--upload-pack=…` будет разобрано как опция. Имя из `backup-config.json` не проходит через `refs.ContainsBranch`, так что единственная защита здесь — `ValidateBranch`.
- **Исправление:** в `ValidateBranch` отклоняйте имена с ведущим `-`, управляющими символами, `..`, пробелами, `~^:?*[\` и так далее (правила `git check-ref-format`). Для fetch используйте полный refspec `refs/heads/<branch>`.

**5. P1 IMPORTANT, `GitEnvironment.cs`, `Create` (строка 65)**
- **Риск:** в документации сказано, что метод возвращает `GIT_TERMINAL_PROMPT`, но в коде есть только `GCM_INTERACTIVE`. Без `GIT_TERMINAL_PROMPT=0` git может запросить логин в консоли. В тихом режиме процесс зависнет. Правило из `external-processes.md` нарушено.
- **Исправление:** добавьте `new("GIT_TERMINAL_PROMPT", "0")`. Добавьте тест, который проверяет эту переменную.

**6. P1/I1 IMPORTANT, `GitEnvironment.cs`, `Create` (строка 69)**
- **Риск:** если путь содержит `;`, `GIT_CEILING_DIRECTORIES` молча не задаётся. Тогда в папке без `.git` git поднимается к родительскому репозиторию. `reset --hard` и `clean -ffdx` изменят не тот репозиторий.
- **Исправление:** не снимайте защиту молча. Отклоняйте такой путь с исключением. Либо перед разрушающими шагами проверяйте, что `git rev-parse --show-toplevel` равен `ClonePath`.

**7. I1 IMPORTANT, `SevenZipArchiver.cs`, `Validate` и `CreateAsync` (строки 40, 106)**
- **Риск:** XML-документация `Validate` обещает отклонять имя файла с папкой, но код проверяет только пустую строку. `FileName` вида `..\..\x.zip` или `C:\x.zip` даёт `finalPath` вне `DestinationFolder`. Тогда `File.Move` создаст архив за пределами папки назначения.
- **Исправление:** требуйте `FileName == Path.GetFileName(FileName)`, без недопустимых символов и с расширением `.zip`. После `Combine` проверьте через `Path.GetFullPath`, что `finalPath` лежит внутри `DestinationFolder`.

## SUGGESTION

**8. I3, `GitClient.SetRemoteUrlAsync` (строки 148–176):** метод проверяет ведущий `-`, но не формат URL. Добавьте проверку, что это `https://github.com/…` без `userinfo`. Тогда токен не попадёт в `.git/config` через этот путь. Поставьте `--` перед URL: `["remote", "set-url", "--", "origin", remoteUrl]` (по этому поводу см. также п. 4).

**9. I1, `GitClient.ValidateClonePath` (строки 183–190):** метод проверяет только `IsPathFullyQualified`, но не то, что путь внутри корня клонов. Это защита второго уровня, если п. 3 пропустит что-то.

**10. P2, `GitClient`, `request.GitPath`:** `SevenZipArchiver.Validate` проверяет `SevenZipPath` на `IsPathFullyQualified`, а `GitPath` нигде не проверяется. Относительный путь может найти `git.exe` в текущей папке. Добавьте ту же проверку.

**11. Защита от злого репозитория, `GitEnvironment.Create`:** `submodule update --recursive` берёт URL из `.gitmodules` чужого репозитория. Добавьте записи `protocol.allow=never` и `protocol.https.allow=always`. Тесты, которым нужен `file://`, продолжат работать через `AdditionalConfiguration`. Подумайте и о `GIT_CONFIG_NOSYSTEM=1`. Он отключит системный конфиг, который может сломать прокси или сертификаты, так что сначала проверьте это.

**12. Коллизия имён, `RepositoryPathResolver.GetWikiCloneFolder` (строка 51):** репозиторий `foo.wiki` и вики репозитория `foo` получают одну папку `foo.wiki`. Один клон затрёт другой через `reset --hard` и `clean`. GitHub допускает репозитории с таким именем. Отклоняйте имена, которые оканчиваются на `.wiki`, или вынесите вики в отдельную подпапку.

**13. `RepositoryPathResolver.GetSourcesRoot` (строка 41):** `config.SourcesRoot` возвращается без проверки. Сделайте так, чтобы метод выбрасывал исключение для пути не `IsPathFullyQualified`.

## Что в порядке
- `GitEnvironment`: пустой `credential.helper`, `core.symlinks=false`, заголовок привязан к `github.com`, токен помечен как secret.
- `SevenZipArchiver`: аргументы идут списком, после `--`. Временные файлы создаются с `Guid`. Перемещение не перезаписывает файл. Очистка при ошибке есть.
- `GetWikiCloneFolder`: проверка имён и границ — образец для п. 3.

Файлы я не менял.
