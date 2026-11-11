**Итог.** В четырёх файлах я нашёл 4 критичные проблемы, 6 важных и 3 пожелания. Главная проблема: токен попадает в аргументы процесса и в `.git/config`. Файлы не менял.

Я читал только эти четыре файла. `ArchiveFolder`, `ContainedFolder`, `GitClientLog`, `IProcessRunner` и вызывающий код не открывал. Где вывод зависит от них, это отмечено.

## CRITICAL

**1. S1 CRITICAL, `GitClient.CloneAsync`: токен вшивается в URL клона.**
- **Риск:**
  - `authenticatedUrl` содержит `x-access-token:<token>@`. Он идёт аргументом в `git clone`, поэтому токен виден в списке процессов.
  - `git clone` записывает этот URL в `.git/config` как `remote.origin.url`. Клон потом архивируется целиком, значит токен попадает в архив.
  - `GitClientLog.Cloning(logger, authenticatedUrl, …)` логирует URL с токеном. Я не проверял, маскирует ли логгер значения.
  - `Replace("https://", …)` заменяет подстроку в любом месте URL. URL на другой хост получит токен в явном виде.
- **Исправление:** передавать в `clone` и в лог только `remoteUrl`. Токен уже уходит через `GIT_CONFIG_*` в `CreateEnvironment(request.Token, …)`, поэтому подстановка в URL лишняя. Заодно проверять `remoteUrl` как `https://github.com/<owner>/<repo>` (см. п. 6).

**2. S1/S2 CRITICAL, `GitClient.GetRemoteRefsAsync`: токен идёт в аргументах и не регистрируется в маскере.**
- **Риск:**
  - Заголовок передаётся как `-c http…extraHeader=Authorization: Basic …` в командной строке. Это нарушает правило «токен только через env».
  - Окружение создаётся через `CreateEnvironment(token: null, …)`, поэтому `secretMasker.Register` не вызывается. Если этот метод вызван первым, `secretMasker.Mask(command)` в `RunGitAsync` не знает токен. Base64-заголовок попадёт в текст `GitCommandException` и в лог раннера.
- **Исправление:** вызывать `CreateEnvironment(request.Token, …)`, как делают остальные методы, и убрать `-c`. Если нужно, чтобы `ls-remote` не наследовал лишнего, регистрировать токен отдельно.

**3. I2 CRITICAL, `GitClient.UpdateAsync`: ветка попадает в `fetch` как позиционный аргумент без `--`.**
- **Риск:** аргументы идут как `["fetch", "origin", request.Branch ?? "HEAD", "--prune", …]`. Git разбирает опции в любой позиции. Ветка `--upload-pack=…` или `--recurse-submodules` станет опцией. `ValidateBranch` проверяет только пустоту, хотя XML-doc `SyncWorkingTreeAsync` утверждает, что ветка не начинается с `-`. Имя ветки приходит из `backup-config.json`.
- **Исправление:**
  - Отклонять ветку с ведущим `-`, управляющими символами, `..`, пробелами и всем, что не проходит `git check-ref-format --branch`.
  - Задавать refspec явно: `+refs/heads/<branch>:refs/remotes/origin/<branch>`.
  - Добавить `--` перед refspec.
  - То же `ValidateBranch` применять к `defaultBranch`, пришедшему с сервера.

**4. I1 CRITICAL, `RepositoryPathResolver.GetCloneFolder`: нет проверки имён и выхода за корень.**
- **Риск:** `Path.Combine(root, owner, repository)` без `EnsureValidNames` и `ContainedFolder`. Для wiki-клона эти проверки есть. Значение `..\..\x` или `C:\x` в `owner` или `repository` уведёт клон, а затем `reset --hard` и `clean -ffdx`, за пределы корня. Rooted-сегмент вообще отбрасывает корень. Вызывающий код мог проверять имена раньше, но это не видно из файла.
- **Исправление:** сделать то же, что в `GetWikiCloneFolder`: `EnsureValidNames(owner, repository)` и `ContainedFolder.Combine(...)`.

## IMPORTANT

**5. I1 IMPORTANT, `SevenZipArchiver.Validate` и `CreateAsync`: `FileName` может содержать путь.**
- **Риск:** XML-doc обещает отклонять имя файла с папкой, но код проверяет только пустоту. `FileName = "..\..\x.zip"` даёт `finalPath` вне `DestinationFolder`. Туда запишется архив (после `Move`).
- **Исправление:** требовать `FileName == Path.GetFileName(FileName)` и отсутствие недопустимых символов. После `Combine` проверять, что полный путь лежит внутри `DestinationFolder`.

**6. I3 IMPORTANT, `GitClient.CloneAsync` и `SetRemoteUrlAsync`: URL не проверяется как GitHub-адрес.**
- **Риск:** `CloneAsync` не проверяет `remoteUrl` совсем. `SetRemoteUrlAsync` отклоняет только ведущий `-` и не ставит `--`. Транспорты вроде `ext::` и `file://` не ограничены. Как следствие, URL на чужой хост получает токен (п. 1).
- **Исправление:** принимать только `https://github.com/<owner>/<repo>[.git]` (через `GitHubUrl`). Для `set-url` добавить `--`. Для надёжности задать `protocol.allow=never` и разрешить только `https`. Тестовые исключения оставить через `AdditionalConfiguration`.

**7. P1 IMPORTANT, `GitEnvironment.Create`: нет `GIT_TERMINAL_PROMPT=0`.**
- **Риск:** правило требует `GIT_TERMINAL_PROMPT=0`, а XML-doc говорит «prompts disabled». Код задаёт только `GCM_INTERACTIVE`. При отклонённом токене git может запросить логин в терминале и зависнуть в тихом режиме.
- **Исправление:** добавить `new("GIT_TERMINAL_PROMPT", "0")`. Рекомендую также задать `GIT_ASKPASS` пустым.

**8. P1 IMPORTANT, `GitEnvironment.Create`: окружение родителя наследуется.**
- **Риск:** если `IProcessRunner` не очищает окружение, git унаследует `GIT_DIR`, `GIT_SSH_COMMAND`, `GIT_CONFIG_GLOBAL` и подобное. Я `IProcessRunner` не проверял.
- **Исправление:** убедиться, что раннер очищает `GIT_*`. Либо явно задавать `GIT_CONFIG_NOSYSTEM=1` и `GIT_CONFIG_GLOBAL=NUL`, чтобы пользовательский конфиг не влиял на безопасность.

**9. IMPORTANT, `GitEnvironment.Create` и `GitClient.CreateEnvironment`: защита от «чужого репозитория» молча отключается.**
- **Риск:** если путь корня клонов содержит `;`, `GIT_CEILING_DIRECTORIES` не ставится. Папка клона без `.git` тогда резолвится в родительский репозиторий, а `reset --hard` и `clean -ffdx` портят его рабочее дерево. Это может сработать при `SourcesRoot` вроде `C:\a;b`.
- **Исправление:** бросать исключение вместо пропуска. Либо перед каждым шагом проверять, что `.git` существует в `ClonePath` (`rev-parse --show-toplevel` равен `ClonePath`).

**10. IMPORTANT, `RepositoryPathResolver.GetCloneFolder` и `GetWikiCloneFolder`: коллизия имён.**
- **Риск:** GitHub допускает репозиторий `foo.wiki`. Его клон `owner\foo.wiki` совпадает с wiki-клоном репозитория `foo`. Разные remote попадут в одну папку, и один клон перезапишет другой.
- **Исправление:** различать папки по схеме, например `owner\foo` и `owner\foo.wiki` → отдельный подкаталог или суффикс с недопустимым для имени символом. Либо явно отклонять репозитории с именем на `.wiki`.

## SUGGESTION

**11. F4/I1, `SevenZipArchiver.CreateAsync`: ссылки и спецсимволы.**
- Добавить `-snl`: 7-Zip сохранит junction и symlink как ссылки и не будет читать содержимое по ним. Это страховка к `core.symlinks=false`.
- Спецсимволы 7-Zip (`@listfile`, wildcard) в `sourceName` после `--` могут обрабатываться. Допустимые имена репозиториев GitHub этого не допускают. Можно подстраховаться: проверять `sourceName` или передавать `.\<name>`. Это моё предположение, я его не проверял.

**12. S1, `GitClient.SetRemoteUrlAsync`.** Маскируется только зарегистрированное значение. Если URL приходит с вшитым токеном, который ещё не зарегистрирован, он попадёт в лог. Лучше отклонять URL с `user:pass@`.

**13. `GitClient.SyncWorkingTreeAsync`: `lfs pull`.** `.lfsconfig` из недоверенного репозитория может сменить LFS-endpoint. Заголовок ограничен `https://github.com/`, и это хорошо. Но стоит принудительно задать `lfs.url` или отключить `.lfsconfig` (`lfs.fetchexclude`/`GIT_LFS_SKIP…`). Я оставил это как идею, не проверенное решение.

## Что сделано правильно
- Аргументы везде передаются списком, а в `clone`, `checkout` и `reset` стоит `--`.
- В `SevenZipArchiver` перемещение файла не перезаписывает существующий, а временные файлы удаляются при сбое.
- Имена `owner` и `repository` для wiki-клона проходят `EnsureValidNames` и `ContainedFolder.Combine`.
- `credential.helper` очищен, `core.symlinks=false` задан, заголовок ограничен `https://github.com/` и помечен как секрет.

Исправлять в таком порядке: п. 1, 2, 3, 4, потом остальные. Править файлы я не начинал. Скажите, по каким пунктам готовить план и правки. По правилам проекта сначала нужны issue и ваше «добро».
