**Итог:** в изменениях есть 5 критичных и 4 важных проблемы. Скорее всего, незакоммиченные правки убрали защиту, которую требуют `.claude/rules/security.md` и существующие тесты. `git diff` я не смотрел и тесты не запускал. Вывод основан на чтении кода и тестов. Файлы не менял.

## CRITICAL

**1. `GitClient.CloneAsync`: токен в URL (S1, FR-11.3).**
- **Риск:** `authenticatedUrl` подставляет `x-access-token:<token>@` в URL. Токен попадает в аргументы процесса `git clone`, где его видят другие процессы. Git записывает URL в `.git/config` клона, и токен уходит в архив. `GitClientLog.Cloning` пишет этот URL в лог без маскирования.
- **Исправление:** клонировать по исходному `remoteUrl`. Токен уже передаётся через `GitEnvironment.Create` в заголовке `extraHeader`. Тест `GitClientTests.cs:92` ждёт именно `_remoteUrl` в аргументах.

**2. `GitClient.CloneAsync`: токен уходит на любой хост (I3).**
- **Риск:** `remoteUrl` не проверяется. `Replace("https://", …)` отправит токен на `https://evil.example/...`. Заменяется также каждое вхождение `https://`.
- **Исправление:** после пункта 1 подстановки не будет. Всё равно проверять `remoteUrl` через `GitHubUrl` (только `https://github.com/<owner>/<repo>`). То же нужно для `SetRemoteUrlAsync`: сейчас там проверяется только ведущий `-`.

**3. `GitClient.GetRemoteRefsAsync`: токен в аргументах и не зарегистрирован в маскере (S1, S2).**
- **Риск:** заголовок Basic (base64 токена) передаётся как `-c http...extraHeader=...` в командной строке. Одновременно вызывается `CreateEnvironment(token: null, …)`, поэтому токен не попадает в `secretMasker.Register`. Сообщения `GitCommandException` и `ParseOutput` могут выдать токен без маскирования.
- **Исправление:** использовать `CreateEnvironment(request.Token, …)` с аргументами `_remoteRefsArguments`, как делает `GetDefaultBranchAsync`.

**4. `GitClient.UpdateAsync` и `ValidateBranch`: инъекция аргумента (I2).**
- **Риск:** `ValidateBranch` проверяет только пустоту. В `fetch origin <branch> …` ветка стоит после опций и без `--`. Ветка вида `--upload-pack=…` станет опцией. Ветка с `:`, `+` или `*` станет refspec. Комментарий в `SyncWorkingTreeAsync` обещает, что ветка не начинается с `-`, но ничто этого не проверяет.
- **Исправление:** в `ValidateBranch` применить правила `git check-ref-format`: без ведущего `-`, без `:`, `*`, `?`, `[`, `\`, пробелов, `..`, `@{`. Для fetch лучше явный refspec `+refs/heads/<b>:refs/remotes/origin/<b>`.

**5. `RepositoryPathResolver.GetCloneFolder`: нет проверки имён (I1).**
- **Риск:** используется `Path.Combine(root, owner, repository)` без проверки. Владелец `C:\Windows` отбрасывает корень, `..` выходит из него. Дальше на этом пути выполняются `reset --hard` и `clean -ffdx`, то есть можно уничтожить чужие данные. `GetWikiCloneFolder` защищён `EnsureValidNames` и `ContainedFolder.Combine`, а `GetCloneFolder` нет. Тест `GetCloneFolder_InvalidNames_Throws` ждёт `ArgumentException`.
- **Исправление:** сделать как в `GetWikiCloneFolder`: `EnsureValidNames` плюс `ContainedFolder.Combine(GetSourcesRoot(config), owner, repository)`. Без `ContainedFolder` не обойтись: имена из `IsValidLogin` и `IsValidRepositoryName` проверены, но корень надо проверять отдельно.

## IMPORTANT

**6. `SevenZipArchiver.Validate`: `FileName` не проверяется на путь (I1, F2).**
- **Риск:** документация обещает отклонять имя файла с папкой, но код проверяет только пустоту. `FileName = @"..\x.zip"` или `@"C:\Users\...\Startup\a.zip"` выводит `finalPath` из `DestinationFolder`, и `File.Move` запишет файл туда. Тест `CreateAsync_UnsafeFileName_ThrowsArgumentException...` ждёт отказ для `sub\a.zip`, `C:\a.zip`, `..`, `.`, `name.`, `CON.zip`, `a*.zip` и других.
- **Исправление:** требовать `Path.GetFileName(name) == name`, отклонять недопустимые символы, ведущие и конечные точки и пробелы, зарезервированные имена устройств. Затем убедиться, что `GetDirectoryName(finalPath)` равен `DestinationFolder`.

**7. `GitEnvironment.Create`: нет `GIT_TERMINAL_PROMPT=0` (P1).**
- **Риск:** документация и тест `GitClientTests.cs:407` требуют эту переменную, но в коде её нет, есть только `GCM_INTERACTIVE`. Git может запросить пароль в консоли и зависнуть в тихом режиме.
- **Исправление:** добавить `new("GIT_TERMINAL_PROMPT", "0")`.

**8. `GitEnvironment.Create`: защита по `GIT_CEILING_DIRECTORIES` молча отключается.**
- **Риск:** если путь содержит `;`, потолок не задаётся без ошибки. Тогда в папке без `.git` git поднимется в родительский репозиторий и выполнит там `reset --hard` и `clean -ffdx`.
- **Исправление:** при `;` в пути бросать исключение, то есть закрывать доступ по умолчанию.

**9. `GitClient.RunGitAsync`, `GitEnvironment`: git работает внутри недоверенного клона.**
- **Риск:** `git lfs pull` и другие шаги идут с `WorkingDirectory = ClonePath`, где лежит чужой код. Старые git-lfs для Windows искали `git.exe` в текущей папке (CVE-2020-27955).
- **Исправление:** добавить в окружение `NoDefaultCurrentDirectoryInExePath=1` и проверять версии git и git-lfs.

## SUGGESTION

- **`GitEnvironment.Create`, submodules:** `submodule update --init --recursive` берёт URL из `.gitmodules` недоверенного репозитория. Добавить `protocol.allow=never` и `protocol.https.allow=always`. `AdditionalConfiguration` идёт после и позволит тестам открыть `file://`.
- **`GitEnvironment.Create`, унаследованное окружение:** переменные `GIT_DIR`, `GIT_WORK_TREE`, `GIT_INDEX_FILE`, `GIT_ASKPASS` из родительского процесса могут направить `reset --hard` и `clean` в другой репозиторий. Если `ProcessEnvironmentVariable` умеет сбрасывать значения, очищать их.
- **`SevenZipArchiver.RunAsync` и `GitClient.RunGitAsync`:** пути `SevenZipPath` и `GitPath` проверяются только на полный путь, а `\\server\share\7z.exe` тоже полный. Принимать только пути, проверенные `IToolLocator`.
- **`SevenZipArchiver.CreateAsync`:** защита от симлинков держится на `core.symlinks=false`. Дополнительно можно проверить, поддерживает ли ваша версия 7-Zip `-snl` для ZIP.

## Следующий шаг
Предлагаю сначала исправить пункты 1, 3 и 5. Они затрагивают токен и данные на диске. Дальше поддерживать правки по `.claude/rules/security.md`. Подтвердите, и я подготовлю правки. Могу сначала показать, что убрали в этих файлах, но для этого мне нужен `git diff`.
