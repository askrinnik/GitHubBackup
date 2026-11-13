**Итог.** Я нашёл 5 критичных проблем и 4 важных, плюс замечания по укреплению защиты. Самые опасные: токен попадает в URL и в аргументы процесса, ветка с `-` в начале становится опцией git, а `GetCloneFolder` не проверяет имена. Файлы я не менял.

Формат записей: `<ID правила> <серьёзность> <файл> <член>`. ID берутся из `.claude/rules/security.md`.

## CRITICAL

**1. S1: `GitClient.CloneAsync`, токен в URL**
- **Риск.** Код подставляет токен в `https://x-access-token:<token>@…` и передаёт этот URL аргументом `git clone`. Из-за этого:
  - токен виден в списке процессов;
  - git записывает URL с токеном в `.git/config` как `origin`, а клон потом попадает в архив;
  - `GitClientLog.Cloning` получает этот URL без маскирования;
  - `Replace("https://", …)` меняет все вхождения, и если `remoteUrl` указывает не на GitHub, токен уходит на чужой хост.
- **Исправление.**
  - Передавать `remoteUrl` как есть. Заголовок авторизации уже добавляет `CreateEnvironment(request.Token, …)` через `GitEnvironment`.
  - Удалить `authenticatedUrl` и логировать `remoteUrl`.
  - Проверять, что URL имеет вид `https://github.com/<owner>/<repo>` (правило I3).
  - Клоны, созданные этим кодом, уже могут хранить токен в `.git/config`. Для них нужна разовая очистка через `git remote set-url origin <чистый URL>`.

**2. S1/S2: `GitClient.GetRemoteRefsAsync`, заголовок в `-c` и токен не зарегистрирован в маскировщике**
- **Риск.** Заголовок `Authorization: Basic <base64>` идёт в командной строке как `-c …extraHeader=…`. Base64 от `x-access-token:<token>` декодируется тривиально. Кроме того, метод вызывает `CreateEnvironment(token: null, …)`, поэтому `secretMasker.Register` не вызывается. Если это первый вызов с этим токеном, `RunGitAsync` и `ParseOutput` не замаскируют заголовок в тексте исключения.
- **Исправление.** Использовать `CreateEnvironment(request.Token, request.ClonePath)` и убрать `-c` из аргументов. Тогда токен передаётся только через окружение и регистрируется в маскировщике, как в `UpdateAsync` и `GetDefaultBranchAsync`.

**3. I2: `GitClient.ValidateBranch`, `UpdateAsync` и `CloneAsync`, внедрение опции через имя ветки**
- **Риск.** `ValidateBranch` проверяет только пустую строку. `UpdateAsync` ставит `request.Branch` в `["fetch", "origin", branch, …]`, без `--` и без проверки начала. Ветка вида `--upload-pack=<cmd>` из конфигурации станет опцией git. XML-doc к `SyncWorkingTreeAsync` утверждает, что ветка «does not start with `-`», но код этого не проверяет.
- **Исправление.**
  - В `ValidateBranch` отклонять имена с `-` в начале, с управляющими символами, с `..` и с пробелами. Допустимо ещё вызывать `git check-ref-format --branch`.
  - В `fetch` лучше передавать явный refspec: `+refs/heads/<branch>:refs/remotes/origin/<branch>`.
  - Применять `ValidateBranch` и к ветке, пришедшей с сервера (`defaultBranch`). Сейчас там тот же слабый вариант.

**4. I1: `RepositoryPathResolver.GetCloneFolder`, нет проверки имён**
- **Риск.** Метод делает `Path.Combine(root, owner, repository)` без `EnsureValidNames` и без `ContainedFolder`. Если `owner` или `repository` равны `..`, `C:\x` или `\\host\share`, `Path.Combine` отбросит корень и клон окажется вне корня. На этот путь потом запускаются `reset --hard` и `clean -ffdx`. `GetWikiCloneFolder` рядом защищён, а этот метод нет.
- **Исправление.** Вызывать `EnsureValidNames(owner, repository)` и `ContainedFolder.Combine(...)`, как в `GetWikiCloneFolder`.
- **Не проверено.** Я не смотрел вызывающий код. Возможно, имена проверяются раньше, но на уровне этого класса защиты нет.

**5. I1: `SevenZipArchiver.Validate` и `CreateAsync`, `FileName` может выйти за `DestinationFolder`**
- **Риск.** XML-doc `Validate` обещает отклонять имя файла с папкой, но код проверяет только пустую строку. `finalPath = Path.Combine(DestinationFolder, FileName)` и `copyPath` при `..\..\x.zip` или абсолютном пути выходят за папку назначения. Архив запишется туда, куда указало имя. Это критично, если `FileName` строится из имени репозитория.
- **Исправление.** Требовать `FileName == Path.GetFileName(FileName)` и отсутствие недопустимых символов. Затем проверять `Path.GetFullPath(finalPath)` на принадлежность `DestinationFolder`.

## IMPORTANT

**6. `GitEnvironment.Create`, защита от чужого репозитория молча отключается**
- **Риск.** Если путь содержит `;`, `GIT_CEILING_DIRECTORIES` не ставится. Тогда в папке клона без `.git` git найдёт репозиторий выше (например, dotfiles в профиле) и выполнит там `reset --hard` и `clean -ffdx`. Это потеря данных. Ceiling тоже пропадает, если `ceilingDirectory` равен `null` (клон в корне диска).
- **Исправление.** Не пропускать защиту, а выбрасывать исключение. Лучше отклонять `ClonePath` с `;` в `ValidateClonePath`.

**7. P1: `GitEnvironment.Create`, нет `GIT_TERMINAL_PROMPT=0`**
- **Риск.** Doc-комментарий и `external-processes.md` требуют эту переменную, но код добавляет только `GCM_INTERACTIVE`. При отклонённом токене git может запросить логин в терминале и повиснуть в тихом режиме.
- **Исправление.** Добавить `new("GIT_TERMINAL_PROMPT", "0")`. Сначала проверьте, не ставит ли её `IProcessRunner`. Я его не читал.

**8. I3: `GitClient.SetRemoteUrlAsync` и `CloneAsync`, URL не проверяется**
- **Риск.** `SetRemoteUrlAsync` отклоняет только пустую строку и `-` в начале. Нет проверки схемы и хоста. URL с `user:token@` запишется в `.git/config`, а маскируется только в логе. `file://` и локальные пути тоже проходят. Команда идёт без `--`.
- **Исправление.**
  - Общая проверка `https://github.com/<owner>/<repo>` без userinfo.
  - В `remote set-url` добавить `--`: `["remote", "set-url", "--", "origin", remoteUrl]`.
  - Проверку нужно сделать и в `CloneAsync` (см. п. 1).

**9. `RepositoryPathResolver.GetWikiCloneFolder`, коллизия имён**
- **Риск.** Вики репозитория `x` лежит в папке `x.wiki`. Но на GitHub есть и обычный репозиторий с именем `x.wiki`, и его клон попадёт в ту же папку. Тогда `reset --hard` и `clean -ffdx` одного клона уничтожат другой.
- **Исправление.** Развести каталоги, например отдельная подпапка для вики. Это меняет раскладку папок, поэтому по правилам репозитория сначала нужна правка PRD.
- **Не проверено.** Реализации `ContainedFolder.Combine`, `GitHubUrl.IsValidLogin` и `IsValidRepositoryName` я не читал. Что `GetWikiCloneFolder` защищён, я принял по тому, как он их вызывает.

## SUGGESTION

- **P1: `GitEnvironment.Create`, изоляция окружения.**
  - Окружение родителя наследуется целиком. Глобальная и системная конфигурация git (`insteadOf`, `http.proxy`, `sslVerify`, `include`) и `GIT_ASKPASS`/`SSH_ASKPASS` влияют на процесс.
  - Стоит добавить `GIT_CONFIG_NOSYSTEM=1` и `GIT_CONFIG_GLOBAL=NUL`, убрать `*_ASKPASS`, и ограничить транспорты через `GIT_ALLOW_PROTOCOL=https`. Это защищает от вредоносного `.gitmodules` с `ssh://` и `file://`.
  - Тестам нужен `file`. Их можно обслужить через `AdditionalConfiguration`.
- **P2: `SevenZipArchiver.Validate`.** `SevenZipPath` проверяется только на «полный путь». Существование файла и то, что это `7z.exe`, должен гарантировать `IToolLocator`. Убедитесь, что вызывающий код его использует.

## Что в порядке

- `SevenZipArchiver` передаёт аргументы списком, после `--`. Временные файлы уникальны и удаляются при ошибке и отмене. Архив не перезаписывает существующий файл.
- Заголовок авторизации привязан к `https://github.com/`, а в `GitEnvironment.Create` помечен как секрет. `core.symlinks=false` и пустой `credential.helper` на месте.
- Сообщения об ошибках в `RunGitAsync` проходят через `secretMasker`.

Если хотите, могу проверить вызывающий код и `GitClientLog`, чтобы подтвердить п. 1, 4 и 5. Для этого нужно прочитать ещё несколько файлов.
