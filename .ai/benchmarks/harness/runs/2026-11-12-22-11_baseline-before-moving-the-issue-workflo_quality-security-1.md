Я прочитал все четыре файла целиком, не только изменённые строки. Есть 4 критичные проблемы, 4 важные и несколько предложений по укреплению. Ничего не правил.

`ProcessRunner`, `ArchiveFolder`, `ContainedFolder` и `GitHubUrl` я не открывал. Выводы про них ниже помечены как «проверить».

## CRITICAL

**1. S1: `GitClient.CloneAsync`, токен в URL клона**
- **Риск:** `authenticatedUrl` содержит `x-access-token:<token>@`.
  - Токен попадает в аргументы процесса `git clone`, и его видят другие процессы пользователя.
  - Токен попадает в `.git/config` как `remote.origin.url`, а клон потом архивируется целиком.
  - `GitClientLog.Cloning(logger, authenticatedUrl, …)` пишет токен в лог без маски.
  - `Replace("https://", …)` заменяет подстроку в любом месте URL, а не только схему.
- **Исправление:** убрать подстановку токена. `CreateEnvironment(request.Token, …)` уже добавляет заголовок `Authorization` через `GIT_CONFIG_*`, поэтому в команду нужно передавать чистый `remoteUrl`. Логировать только чистый URL.

**2. S1/S2: `GitClient.GetRemoteRefsAsync`, токен в `-c` и не зарегистрирован в masker**
- **Риск:** заголовок (Base64 токена) передаётся аргументом `-c http…extraHeader=Authorization: Basic …`, то есть виден в командной строке процесса. Метод вызывает `CreateEnvironment(token: null, …)`, и `secretMasker.Register` для токена не срабатывает.
  - Тогда `RunGitAsync` и `ParseOutput` маскируют команду и stderr «пустым» masker. Заголовок может уйти в `GitCommandException.Message` и в логи.
  - Маскировка работает только если токен зарегистрировал другой вызов раньше.
- **Исправление:** вызывать `CreateEnvironment(request.Token, …)`, как это делает `GetDefaultBranchAsync`, и убрать ветку с `-c`.

**3. I1: `RepositoryPathResolver.GetCloneFolder`, нет проверки имён и выхода из корня**
- **Риск:** `Path.Combine(root, owner, repository)` не проверяет имена. Значение `..` выводит путь из корня клонов. Rooted-сегмент вроде `C:\x` вообще отбрасывает корень. Имена вроде `CON` или с точкой на конце тоже проходят.
  - Дальше на этом пути выполняются `fetch`, `reset --hard` и `clean -ffdx`, то есть разрушительные операции вне корня.
  - `GetWikiCloneFolder` в этом же файле уже делает всё правильно, поэтому защита асимметрична.
- **Исправление:** вызывать `EnsureValidNames(owner, repository)` и `ContainedFolder.Combine(GetSourcesRoot(config), owner, repository)`.
- **Проверить:** `ArchiveFolder.Resolve` в обоих `GetArchiveFolder` получает `repository.Name` и `repository.Path` из конфигурации. Нужно убедиться, что он проверяет их так же строго.

**4. I2: `GitClient.UpdateAsync` и `ValidateBranch`, имя ветки может стать опцией git**
- **Риск:** `ValidateBranch` проверяет только пустоту. В `UpdateAsync` ветка идёт позиционным аргументом: `fetch origin <branch> …` без `--`. Имя `--upload-pack=<cmd>` разберётся как опция, и это возможное выполнение команды.
  - Комментарий к `SyncWorkingTreeAsync` обещает, что ветка не начинается с `-`, но код этого не проверяет.
  - Имя ветки с `:` или `+` также меняет смысл refspec.
  - Ветка по умолчанию, пришедшая от сервера, проходит ту же слабую проверку.
- **Исправление:**
  - Отклонять ветки, начинающиеся с `-`, и ветки, не проходящие правила `git check-ref-format --branch`.
  - Для `fetch` использовать явный refspec `+refs/heads/<branch>:refs/remotes/origin/<branch>`.

## IMPORTANT

**5. I1/F2: `SevenZipArchiver.Validate` и `CreateAsync`, `FileName` не проверяется на папки**
- **Риск:** документация метода обещает отклонять имя файла с папкой, но код проверяет только пустоту.
  - `FileName` вида `..\..\x.zip` или rooted-путь через `Path.Combine` даёт `finalPath` вне `DestinationFolder`, и архив пишется туда.
  - `File.Move` без перезаписи не затирает существующий файл, но запись вне папки остаётся.
- **Исправление:** требовать `FileName == Path.GetFileName(FileName)`, отсутствие недопустимых символов, зарезервированных имён и завершающих точек и пробелов. После `Combine` проверять, что полный путь лежит внутри `DestinationFolder`.

**6. `GitEnvironment.Create`, при `;` в пути потолок поиска репозитория пропадает молча**
- **Риск:** путь с `;` (на Windows это допустимо) не получает `GIT_CEILING_DIRECTORIES`.
  - Если в такой папке нет `.git`, а выше лежит другой репозиторий, `reset --hard` и `clean -ffdx` в `SyncWorkingTreeAsync` сработают по чужому репозиторию.
  - То же возможно, когда `ceilingDirectory` вычислить не удалось.
- **Исправление:** не пропускать ограничение молча. Либо отклонять такой путь, либо перед разрушительными шагами проверять через `git rev-parse --show-toplevel`, что корень репозитория равен `ClonePath`.

**7. I3: `GitClient.CloneAsync` и `SetRemoteUrlAsync`, нет проверки URL**
- **Риск:** метод принимает любой `remoteUrl` (`file://`, `ssh://`, `ext::`), а требование «только `https://github.com/…`» нигде не проверяется. `SetRemoteUrlAsync` отсекает только префикс `-`.
  - `SetRemoteUrlAsync` не отклоняет URL с учётными данными (`user:pass@`), и они лягут в `.git/config`.
  - `SetRemoteUrlAsync` вызывает `git remote set-url` без `--`.
- **Исправление:**
  - Проверять URL через `GitHubUrl` (https, хост github.com, без userinfo).
  - Добавить `GIT_ALLOW_PROTOCOL=https` в `GitEnvironment`. Он же ограничит протоколы в `.gitmodules` чужих репозиториев.
  - Для тестов с `file://` расширять список через `AdditionalConfiguration` или отдельным параметром.

**8. P1: `GitEnvironment.Create`, нет `GIT_TERMINAL_PROMPT=0`**
- **Риск:** документация метода и `external-processes.md` требуют эту переменную. В коде есть только `GCM_INTERACTIVE`. При отклонённом токене git может запросить пароль в консоли и зависнуть.
- **Исправление:** добавить `new("GIT_TERMINAL_PROMPT", "0")`. Сначала проверить, не добавляет ли её `ProcessRunner`.

## SUGGESTION

- **`RepositoryPathResolver.GetWikiCloneFolder` и `GetCloneFolder`:** репозиторий `foo.wiki` и вики репозитория `foo` получают одну папку. Один клон перезапишет другой, а `clean -ffdx` его удалит. Исправление: обнаруживать коллизию или кодировать имя вики иначе.
- **`SevenZipArchiver.CreateAsync`:** добавить `-snl` к команде `a`, чтобы 7-Zip хранил ссылки как ссылки. Сейчас защита от архивации чужих файлов держится только на `core.symlinks=false`.
- **`SevenZipArchiver.CreateAsync`:** пока токен попадает в `.git/config` (находка 1), он окажется внутри архива. После исправления находки 1 этот риск исчезнет.
- **`GitClient.RunGitAsync`:** `request.GitPath` нигде не проверяется на абсолютность (P2). Добавить проверку `Path.IsPathFullyQualified`, как для `SevenZipPath`.
- **`GitEnvironment.Create`:** user- и system-конфигурация git по-прежнему читаются. Для воспроизводимости и изоляции можно задать `GIT_CONFIG_NOSYSTEM=1` и `GIT_CONFIG_GLOBAL=NUL`.

Хорошо сделано: `--` в `clone`, `checkout`, `reset`, список аргументов вместо строки, `core.symlinks=false`, пустой `credential.helper`, никогда не перезаписывающий `Move` и уникальные временные имена в `SevenZipArchiver`.
