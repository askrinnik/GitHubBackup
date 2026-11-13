**Итог:** я нашёл 3 критичных проблемы и 5 важных. Самая серьёзная: токен GitHub попадает в аргументы процесса, в `.git/config` клона (а значит, в ZIP-архив) и, вероятно, в лог. Файлы я не менял.

Я прочитал файлы целиком, а не только изменённые строки. Типы `GitHubUrl`, `ContainedFolder`, `ArchiveFolder`, `IProcessRunner` и `GitClientLog` я не открывал. Выводы о них помечены как «проверить».

## CRITICAL

**1. S1 · `GitClient.CloneAsync` (GitClient.cs:76-79, 87): токен в URL**
- **Риск:**
  - `authenticatedUrl` содержит `x-access-token:<token>@`. Он идёт в аргументы `git clone`, видимые другим процессам.
  - `git clone` записывает этот URL в `.git/config` как `origin`. Клон архивируется как есть, поэтому токен попадает в ZIP.
  - `GitClientLog.Cloning(logger, authenticatedUrl, …)` получает URL без маскирования. Для сравнения, `SetRemoteUrlAsync` маскирует URL явно. Проверить, маскирует ли логгер сам.
  - `Replace("https://", …)` подставляет токен в любой хост, если `remoteUrl` ведёт не на github.com.
- **Исправление:**
  - Удалить `authenticatedUrl` и клонировать `remoteUrl` как есть. Заголовок авторизации уже добавляет `GitEnvironment.Create` через переменные окружения.
  - Перед клонированием проверять, что URL имеет вид `https://github.com/<owner>/<repo>` (I3).

**2. S1 · `GitClient.GetRemoteRefsAsync` (GitClient.cs:121-125): токен в аргументах**
- **Риск:** токен в виде base64-заголовка передаётся через `-c http.…extraHeader=Authorization: Basic …` в командной строке процесса. Любой локальный процесс увидит его, например через `Win32_Process`. Правило разрешает токен только в переменных окружения. Маскирование защищает лог, но не саму командную строку.
- **Исправление:** вызывать `CreateEnvironment(request.Token, request.ClonePath)` и всегда использовать `_remoteRefsArguments`. Ветка с `-c` не нужна.

**3. I1 · `RepositoryPathResolver.GetCloneFolder` (RepositoryPathResolver.cs:44-45): нет проверки имён**
- **Риск:**
  - `Path.Combine(root, owner, repository)` не проверяет имена.
  - Значение `..\..\x` выводит путь за пределы корня. Значение `C:\Windows` (rooted-сегмент) заменяет корень целиком.
  - Дальше `GitClient` выполняет в этой папке `reset --hard` и `clean -ffdx`, то есть удаляет файлы вне корня.
  - `GetWikiCloneFolder` уже делает всё правильно, а `GetCloneFolder` — нет.
- **Исправление:** вызывать `EnsureValidNames(owner, repository)` и `ContainedFolder.Combine(...)`, как в wiki-варианте. Добавить тест на `..`, `C:\x`, `CON`, `a/b`.

## IMPORTANT

**4. I2 · `GitClient.ValidateBranch` и `UpdateAsync` (GitClient.cs:110, 198-205): ветка как опция git**
- **Риск:**
  - `ValidateBranch` проверяет только пустоту. XML-doc `SyncWorkingTreeAsync` утверждает, что ветка не начинается с `-`, но код этого не обеспечивает.
  - `fetch origin <branch>` идёт без `--`. Ветка `--upload-pack=…` или `--exec=…` из `backup-config.json` станет опцией.
  - Ветки с `:` или `+` превращаются в произвольный refspec.
  - Имя ветки по умолчанию приходит от сервера (`ReadDefaultBranchAsync`) и проходит ту же слабую проверку.
- **Исправление:**
  - Отклонять ветки, которые начинаются с `-`, содержат `:`, пробелы, управляющие символы, `..`, `\`, `~^?*[`, или заканчиваются на `.lock`.
  - Использовать явный refspec: `+refs/heads/<b>:refs/remotes/origin/<b>`.

**5. I1 · `SevenZipArchiver.Validate` (SevenZipArchiver.cs:90-120): `FileName` может выйти из папки**
- **Риск:**
  - XML-doc обещает отклонять имя файла, содержащее папку, но код проверяет только пустоту.
  - `FileName = "..\..\x.zip"` уводит `finalPath` из `DestinationFolder`.
  - Rooted-имя заменяет папку назначения целиком.
  - `File.Move` и `File.Copy` затем пишут в произвольное место.
- **Исправление:**
  - Требовать `Path.GetFileName(FileName) == FileName` и отсутствие недопустимых символов.
  - После `Combine` проверять через `GetFullPath`, что `finalPath` лежит внутри `DestinationFolder`.

**6. P1 / S · `GitEnvironment.Create` (GitEnvironment.cs:65): нет `GIT_TERMINAL_PROMPT=0`**
- **Риск:**
  - Summary и `<returns>` обещают `GIT_TERMINAL_PROMPT`, и правила требуют его, но метод его не добавляет. Есть только `GCM_INTERACTIVE`.
  - При отказе в токене git может запросить логин в консоли и зависнуть в тихом режиме.
  - Проверить, не добавляет ли его `ProcessRunner`.
- **Исправление:** добавить `new("GIT_TERMINAL_PROMPT", "0")`.

**7. I3 / S1 · `GitClient.SetRemoteUrlAsync` (GitClient.cs:148-175): слабая проверка URL**
- **Риск:**
  - Проверяются только пустота и ведущий `-`. Допускаются `file://`, `ext::…` и URL с `user:pass@`.
  - URL с учётными данными сохраняется в `.git/config` и попадает в архив.
  - Аргумент идёт без `--`.
- **Исправление:**
  - Принимать только `https://github.com/<owner>/<repo>` без userinfo (общий валидатор `GitHubUrl`).
  - Добавить `--` перед позиционными аргументами.

**8. Отказ «в открытую» · `GitEnvironment.Create` (GitEnvironment.cs:69)**
- **Риск:** если путь содержит `;`, `GIT_CEILING_DIRECTORIES` просто не задаётся. Тогда в папке без `.git` команды `reset --hard` и `clean -ffdx` могут сработать на объемлющем репозитории и стереть его файлы. Защита отключается именно там, где она нужна.
- **Исправление:** бросать исключение, а не пропускать защиту. Либо отклонять `;` в путях на этапе валидации (`GetCloneFolder`, `GetSourcesRoot`).

## SUGGESTION

**9. `RepositoryPathResolver.GetWikiCloneFolder`: возможная коллизия папок.** Wiki репозитория `foo` лежит в `owner\foo.wiki`. Обычный репозиторий с именем `foo.wiki` клонируется в ту же папку, и `clean -ffdx` затрёт один клон другим. Проверить, запрещает ли `IsValidRepositoryName` такие имена. Если нет, отклонять имена с суффиксом `.wiki` или хранить wiki в отдельной подпапке.

**10. P1 · `GitEnvironment.Create`: окружение родителя.** Если `ProcessRunner` наследует переменные родителя, то `GIT_DIR`, `GIT_WORK_TREE`, `GIT_INDEX_FILE`, `GIT_ASKPASS`, `GIT_SSH_COMMAND`, `GIT_EXEC_PATH` способны перенаправить git. Явно очищать их, например пустым значением (зависит от поведения `ProcessRunner`).

**11. Submodules · `GitClient.SyncWorkingTreeAsync`.** `submodule update --init --recursive` берёт URL из `.gitmodules` чужого репозитория. Ограничить протоколы: `GIT_ALLOW_PROTOCOL=https`. Для тестов с `file://` оставить исключение через `AdditionalConfiguration`.

**12. F4 · `SevenZipArchiver.CreateAsync` (SevenZipArchiver.cs:59): символические ссылки и junction.** Защита сейчас держится только на `core.symlinks=false`. Junction и reparse-point 7-Zip может обойти по содержимому. Рассмотреть `-snl` (проверить поведение для ZIP на Windows) или проверку reparse-point в клоне.

**13. P2 · `SevenZipArchiver.Validate`.** Проверяется только полная квалифицированность `SevenZipPath`, но не существование. Это ограничено тем, что `IToolLocator` проверяет путь при старте. Убедиться, что проверка есть.

Могу проверить пункты, помеченные «проверить» (`GitClientLog.Cloning`, `ProcessRunner`, `GitHubUrl.IsValidRepositoryName`, `ContainedFolder`), если скажете.
