**Итог:** в четырёх файлах есть 4 критичных проблемы, 4 важных и 3 пожелания. Самое опасное: токен GitHub попадает в аргументы процесса и в `.git/config` (а значит, в архив), а имена владельца и репозитория не проверяются перед построением пути клона. Файлы я не менял.

Формат: `ID серьёзность — файл, член`. Идентификаторы и серьёзность взяты из `.claude/rules/security.md`.

## CRITICAL

**1. S1/I3 — `GitClient.CloneAsync` (`GitClient.cs:76-79`, `:87`)**
- **Риск:** метод вставляет токен в URL (`https://x-access-token:<token>@…`) и передаёт его в `git clone` аргументом.
  - Другие локальные процессы видят токен в командной строке.
  - Git записывает URL в `.git/config` как `origin`. Клон архивируется как есть, поэтому токен попадает в ZIP.
  - `GitClientLog.Cloning` получает `authenticatedUrl` без маскирования. Внутрь `GitClientLog` я не заглядывал, но в `SetRemoteUrlAsync` URL маскируется явно, а здесь нет.
  - `Replace("https://", …)` заменяет все вхождения и не проверяет хост. Для URL не на `github.com` токен уйдёт стороннему серверу.
- **Исправление:**
  - Передавать в `clone` исходный `remoteUrl` без учётных данных. Заголовок авторизации уже приходит из `CreateEnvironment(request.Token, …)` и ограничен `https://github.com/`, так что подстановка в URL лишняя.
  - Проверять `remoteUrl` по шаблону `https://github.com/<owner>/<repo>` (I3).

**2. S1/S2 — `GitClient.GetRemoteRefsAsync` (`GitClient.cs:121-126`)**
- **Риск:** заголовок `Authorization: Basic <base64 токена>` передаётся аргументом `-c` и виден в списке процессов. Кроме того, `CreateEnvironment(token: null, …)` не регистрирует токен в `secretMasker`.
  - `RunGitAsync` и `ParseOutput` маскируют команду через `secretMasker`. Если токен не зарегистрирован, заголовок попадёт в текст исключения, лог и историю запусков.
  - Это зависит от того, вызывал ли раньше `CreateEnvironment` с токеном другой метод. На это полагаться нельзя.
- **Исправление:** убрать `-c` и вызывать `CreateEnvironment(request.Token, request.ClonePath)`, как это делает `GetDefaultBranchAsync`. Тогда заголовок идёт только через переменные окружения, а токен регистрируется в маскере.

**3. I1 — `RepositoryPathResolver.GetCloneFolder` (`RepositoryPathResolver.cs:44-45`)**
- **Риск:** метод делает `Path.Combine(root, owner, repository)` без проверок.
  - Если `owner` или `repository` содержит `..`, корневой путь вроде `C:\x` или имя устройства (`CON`, `NUL`), то клон окажется вне корня клонов. `Path.Combine` отбрасывает всё, что стоит слева от корневого сегмента.
  - Над этой папкой затем выполняются `reset --hard` и `clean -ffdx`, то есть возможно удаление чужих файлов.
  - `GetWikiCloneFolder` защищён через `EnsureValidNames` и `ContainedFolder.Combine`, а обычный клон нет.
- **Исправление:** сделать как в `GetWikiCloneFolder`: вызвать `EnsureValidNames(owner, repository)` и вернуть `ContainedFolder.Combine(GetSourcesRoot(config), owner, repository)`.

**4. I2 — `GitClient.UpdateAsync` и `ValidateBranch` (`GitClient.cs:110`, `:198-205`)**
- **Риск:** `fetch origin <branch> …` получает ветку позиционным аргументом без `--`.
  - `ValidateBranch` проверяет только пустую строку, а не ведущий `-`.
  - Ветка `--upload-pack=…` или `--server-option=…` из конфига превратится в опцию git.
  - Комментарий к `SyncWorkingTreeAsync` утверждает, что ветка не начинается с `-`, но код этого не проверяет.
- **Исправление:** в `ValidateBranch` отклонять ветку, которая начинается с `-`, содержит управляющие символы или не проходит правила `git check-ref-format`. Для `fetch` использовать форму с явным refspec и `--`.

## IMPORTANT

**5. I1 — `SevenZipArchiver.Validate` и `CreateAsync` (`SevenZipArchiver.cs:40`, `:106-109`)**
- **Риск:** документация метода говорит, что `FileName` не должен содержать папку, но код проверяет только пустоту. `Path.Combine(DestinationFolder, FileName)` при значении `..\..\x.zip` или `C:\x.zip` запишет архив вне папки назначения.
- **Исправление:** требовать `FileName == Path.GetFileName(FileName)`, отсутствие недопустимых символов, и проверять, что `GetFullPath(finalPath)` лежит внутри `DestinationFolder`.

**6. P1 — `GitEnvironment.Create` (`GitEnvironment.cs:69`) и `GitClient.CreateEnvironment` (`GitClient.cs:224-227`)**
- **Риск:** если путь клона содержит `;`, потолок поиска репозитория (`GIT_CEILING_DIRECTORIES`) молча не задаётся.
  - Тогда в папке без `.git` git найдёт внешний репозиторий выше по дереву и выполнит над ним `reset --hard` и `clean -ffdx`.
  - Путь с `;` на Windows допустим.
- **Исправление:** не пропускать потолок молча. Либо бросать исключение для такого пути, либо перед разрушительными шагами проверять `git rev-parse --show-toplevel` на совпадение с `ClonePath`.

**7. P1 — `GitEnvironment.Create` (`GitEnvironment.cs:63-67`)**
- **Риск:** документация метода и `external-processes.md` обещают `GIT_TERMINAL_PROMPT=0`, но в списке переменных есть только `GCM_INTERACTIVE`. Я не проверил, добавляет ли `IProcessRunner` эту переменную сам. Если нет, git при отказе авторизации может запросить пароль в консоли и зависнуть в тихом режиме.
- **Исправление:** добавить `new("GIT_TERMINAL_PROMPT", "0")`. Если `IProcessRunner` уже добавляет её, привести документацию в соответствие с кодом.

**8. I3/I2 — `GitClient.SetRemoteUrlAsync` (`GitClient.cs:156-174`)**
- **Риск:** URL проверяется только на ведущий `-`. Схема и хост не проверяются, поэтому подойдут `file://`, `ssh://` и чужой хост. Команда `remote set-url origin <url>` идёт без `--`. Если URL содержит учётные данные, маскер замаскирует их только при совпадении с зарегистрированным токеном.
- **Исправление:** применять ту же проверку `https://github.com/<owner>/<repo>`, что и в пункте 1, отклонять URL с `user:pass@` и поставить `--` перед позиционными аргументами.

## SUGGESTION

**9. P1 — `GitEnvironment.Create`:** глобальный и системный конфиг git пользователя продолжают действовать (`url.*.insteadOf`, `core.sshCommand`, `core.fsmonitor`, `protocol.*`). Задать `GIT_CONFIG_NOSYSTEM=1` и `GIT_CONFIG_GLOBAL=NUL`. Добавить `GIT_ALLOW_PROTOCOL=https`, чтобы `submodule update --init --recursive` и `lfs pull` не ходили по другим протоколам.

**10. P2/F4 — `SevenZipArchiver.Validate`:**
- `SevenZipPath` проверяется только на «полный путь». UNC-путь `\\host\share\7z.exe` пройдёт, а существование файла не проверяется.
- Без `-snl` 7-Zip следует за junction внутри клона. Git junction не создаёт, но другой процесс мог создать.

**11. I1 — `RepositoryPathResolver.GetArchiveFolder`:** оба перегруженных метода передают `account.Path` и `repository.Path` из конфига в `ArchiveFolder.Resolve`. Этот класс не входил в запрошенные файлы, и я его не читал. Нужно проверить, что он удерживает результат внутри `BackupRoot`.

## Что в порядке
- 7-Zip: `--` стоит перед `temporaryPath` и `sourceName`, аргументы идут списком, архив не перезаписывается (`File.Move` без overwrite).
- Заголовок авторизации ограничен ключом `http.https://github.com/.extraHeader`.
- `core.symlinks=false` и пустой `credential.helper` заданы для всех команд git.
- `GetWikiCloneFolder` проверяет имена и вложенность пути.
