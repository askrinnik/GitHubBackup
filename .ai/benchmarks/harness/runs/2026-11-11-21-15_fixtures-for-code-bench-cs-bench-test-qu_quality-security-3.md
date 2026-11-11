# Ревью безопасности: 4 файла

**Итог:** 3 критичных находки, 5 важных, 3 рекомендации. Файлы я не менял. Тела `GitHubUrl`, `ContainedFolder`, `ArchiveFolder.Resolve` и `IProcessRunner` я не читал. Где вывод зависит от них, это отмечено.

## CRITICAL

**1. I1 CRITICAL — `RepositoryPathResolver.GetCloneFolder`**
- **Риск:** метод строит путь через `Path.Combine(root, owner, repository)` без проверки имён. `GetWikiCloneFolder` рядом проверяет их через `EnsureValidNames` и `ContainedFolder.Combine`.
  - Имя `..` выводит путь за корень клонов.
  - Корневой сегмент (`C:\x`) заменяет корень целиком.
  - Служебные имена (`CON`, `NUL`) и хвостовые точки тоже проходят.
  - После этого `GitClient` запускает в этой папке `reset --hard` и `clean -ffdx`. Это может уничтожить чужие файлы.
- **Исправление:** вызывать `EnsureValidNames(owner, repository)` и `ContainedFolder.Combine(...)`, как в `GetWikiCloneFolder`.
- **Проверить:** допускает ли `IsValidRepositoryName` имя `x.wiki`. Если да, клон репозитория `x.wiki` совпадёт с папкой wiki-клона репозитория `x`.

**2. S1 CRITICAL — `GitClient.CloneAsync`**
- **Риск:** токен встраивается в URL: `https://x-access-token:<token>@...`.
  - URL передаётся в аргументах процесса `git clone`, и его видят другие процессы.
  - Git записывает URL в `.git/config` как `remote.origin.url`. Клон потом попадает в ZIP вместе с токеном.
  - `GitClientLog.Cloning(logger, authenticatedUrl, ...)` пишет URL с токеном в лог.
  - Это нарушает NFR-1 и FR-11.3.
  - `Replace("https://", ...)` заменяет все вхождения, а не только префикс.
- **Исправление:** передавать в `git clone` исходный `remoteUrl`. Заголовок авторизации уже приходит через `GitEnvironment.Create`, поэтому токен в URL не нужен. Логировать только `remoteUrl`.

**3. S1/S2 CRITICAL — `GitClient.GetRemoteRefsAsync`**
- **Риск:** заголовок `Authorization: Basic <base64(token)>` передаётся аргументом `-c`, то есть виден в списке процессов и в логе команды.
  - Окружение создаётся с `CreateEnvironment(token: null, ...)`. Токен не регистрируется в `secretMasker`.
  - Если этот вызов идёт первым (`ls-remote` для проверки изменений), маска ещё не знает токен. Заголовок попадёт в лог и в сообщение `GitCommandException`.
- **Исправление:** передавать `request.Token` в `CreateEnvironment`, а `-c` убрать. Заголовок пойдёт через `GIT_CONFIG_*`, как в остальных методах.

## IMPORTANT

**4. I2 IMPORTANT — `GitClient.UpdateAsync` (и `ValidateBranch`)**
- **Риск:** `ValidateBranch` проверяет только пустоту, хотя в документации `SyncWorkingTreeAsync` сказано, что ветка не начинается с `-`. В `fetch origin <branch> ...` значение стоит без `--`, поэтому ветка `--upload-pack=...` читается как опция.
  - Ветка из `backup-config.json` может также содержать `:` или `+`. Тогда это refspec, а не имя ветки.
  - Правило `external-processes.md` нарушено.
- **Исправление:** валидировать как `git check-ref-format --branch`. Отклонять префикс `-`, а также `:`, `+`, `..`, `~^?*[\`, пробелы и управляющие символы. Лучше передавать явный refspec `+refs/heads/<b>:refs/remotes/origin/<b>`.

**5. IMPORTANT — `GitEnvironment.Create` (параметр `ceilingDirectory`)**
- **Риск:** если путь клона содержит `;`, потолок поиска репозитория молча не задаётся. Если в клоне нет `.git`, git поднимается к родительскому репозиторию, например когда корень клонов лежит внутри чужого репозитория. Тогда `reset --hard` и `clean -ffdx` выполнятся над ним. Потолок сравнивается как строка, поэтому junction в пути тоже может его обойти.
- **Исправление:** при `;` бросать исключение, а не отключать защиту. Надёжнее задавать `GIT_DIR=<clone>\.git` и `GIT_WORK_TREE=<clone>`. Альтернатива: перед разрушительными шагами сверять `git rev-parse --show-toplevel` с путём клона.

**6. P1 IMPORTANT — `GitEnvironment.Create`**
- **Риск:** `GIT_TERMINAL_PROMPT=0` не добавляется, хотя документация метода и `external-processes.md` его требуют. Для приватного репозитория без токена git может запросить пароль в терминале и зависнуть в тихом режиме.
- **Оговорка:** возможно, `IProcessRunner` добавляет переменную сам. Я это не проверял.
- **Исправление:** добавить `new("GIT_TERMINAL_PROMPT", "0")`.

**7. I1/F2 IMPORTANT — `SevenZipArchiver.Validate` и `CreateAsync`**
- **Риск:** документация `Validate` обещает отклонять `FileName` с папкой, но код проверяет только пустую строку. `Path.Combine(DestinationFolder, FileName)` с `..\x.zip` или `C:\x.zip` создаёт архив вне папки назначения.
- **Исправление:** требовать `FileName == Path.GetFileName(FileName)` и отсутствия недопустимых символов. После `Combine` проверять, что `finalPath` лежит внутри `DestinationFolder`.

**8. I3/S1 IMPORTANT — `GitClient.SetRemoteUrlAsync` и `CloneAsync` (`remoteUrl`)**
- **Риск:** метод проверяет пустоту и префикс `-`, но не схему и не наличие `user:pass@`. Через него в `.git/config` можно записать URL с токеном.
  - `CloneAsync` вообще не проверяет `remoteUrl`.
  - Токен маскируется только если он уже зарегистрирован в `secretMasker`. Здесь вызывается `CreateEnvironment(token: null, ...)`.
- **Исправление:** принимать только `https://github.com/<owner>/<repo>` без userinfo. Использовать общую проверку из `GitHubUrl`. В `CloneAsync` проверять так же.

**9. IMPORTANT — `GitClient.SyncWorkingTreeAsync` (подмодули)**
- **Риск:** `submodule update --init --recursive` выполняет URL из `.gitmodules` недоверенного репозитория. Версия git по умолчанию может разрешать лишние протоколы (`ssh`, `git://`), а `ext::` зависит от конфигурации пользователя.
- **Исправление:** задать `GIT_ALLOW_PROTOCOL=https` для всех процессов git. Тестам с `file://` нужен отдельный путь через `AdditionalConfiguration`.

## SUGGESTION

**10. F4/I1 SUGGESTION — `SevenZipArchiver.CreateAsync`**
- 7-Zip на Windows без `-snl` следует по символьным ссылкам и junction. `core.symlinks=false` защищает только ссылки, созданные git, но не junction внутри клона.
- **Исправление:** добавить `-snl` в аргументы `a`.

**11. P1 SUGGESTION — `GitEnvironment.Create`**
- Процесс наследует окружение родителя: `GIT_DIR`, `GIT_WORK_TREE`, `GIT_ASKPASS`, `GIT_SSH_COMMAND` и другие.
- **Исправление:** явно очищать эти переменные. Какое окружение собирает `IProcessRunner`, я не проверял.

**12. SUGGESTION — `GitClient.SyncWorkingTreeAsync`**
- Подмодули и LFS обрабатывают недоверенный ввод, поэтому нужна проверка минимальной версии git с исправленными CVE по подмодулям. Её лучше делать при старте, вместе с проверкой пути к git.

## Проверено, замечаний нет
- Аргументы `7z` ставят `--` перед путями. `--` также останавливает разбор `@listfile`.
- Переименование архива не перезаписывает существующий файл.
- Временные файлы удаляются при сбое и отмене.
- `credential.helper` очищается, `core.symlinks=false` задаётся.
- Остальные аргументы git передаются списком, а имя ветки при `checkout`, `reset` и `-B` защищено префиксом `origin/` и `--`.

Если хотите, могу прочитать `GitHubUrl`, `ContainedFolder` и `IProcessRunner`, чтобы закрыть оговорки в пунктах 1, 6 и 11.
