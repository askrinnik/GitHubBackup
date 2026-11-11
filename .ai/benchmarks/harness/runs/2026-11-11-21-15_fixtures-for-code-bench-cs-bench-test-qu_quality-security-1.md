# Ревью безопасности: 4 файла

Файлы я не менял. Тесты и сборку не запускал. Выводы сделаны только чтением кода.

Сначала главное. Критичных находок пять: токен в URL клона, токен в аргументах `ls-remote`, выход клона за корень, инъекция опции через ветку и выход архива за папку назначения. Все пять нарушают правила `.claude/rules/security.md`. Формат: `<ID> <severity> <файл> <член>`.

## CRITICAL

### 1. S1 CRITICAL — `GitClient.CloneAsync`: токен в URL клона
- **Что в коде.** `authenticatedUrl` собирается как `https://x-access-token:<token>@…`.
- **Риск.**
  - Токен попадает в аргументы процесса `git clone`, и его видят другие процессы.
  - Он пишется в лог через `GitClientLog.Cloning(logger, authenticatedUrl, …)`. Шаблон `{Url}` не маскируется, и `secretMasker` здесь не вызывается.
  - `git clone` сохраняет URL как `origin` в `.git/config`. Клон архивируется целиком, значит токен окажется в ZIP.
- **Лишний код.** Токен уже передаётся заголовком в `GitEnvironment.Create`, так что подстановка в URL не нужна.
- **Исправление.** Удалить `authenticatedUrl`. Передавать `remoteUrl` без учётных данных в `clone` и в лог. Добавить проверку, что `remoteUrl` не содержит `@` в части authority.

### 2. S1/S2 CRITICAL — `GitClient.GetRemoteRefsAsync`: токен в аргументах и без маскирования
- **Что в коде.** Заголовок передаётся как `-c http…extraHeader=Authorization: Basic …` в аргументах. `CreateEnvironment(token: null, …)` не вызывает `secretMasker.Register`.
- **Риск.**
  - Заголовок виден в списке процессов.
  - Если токен ещё не зарегистрирован в `secretMasker`, строка команды и stderr в `GitCommandException` и в `ParseOutput` уходят в лог и в статус `Failed` без маскирования. Это бывает, когда `ls-remote` — первая операция запуска.
- **Исправление.** Вызывать `CreateEnvironment(request.Token, request.ClonePath)`, как в `GetDefaultBranchAsync`, и убрать `-c`. Тогда заголовок идёт через окружение, а токен регистрируется в маскере.

### 3. I1 CRITICAL — `RepositoryPathResolver.GetCloneFolder`: нет проверки имён и границы корня
- **Что в коде.** Метод делает `Path.Combine(root, owner, repository)` без `EnsureValidNames` и без `ContainedFolder`. `GetWikiCloneFolder` их использует, и этот метод от них отличается.
- **Риск.** Значение `owner` вида `C:\x` отбрасывает корень. Значения `..`, `con` и подобные выводят путь наружу. Затем в этой папке выполняются `reset --hard` и `clean -ffdx`, то есть можно уничтожить чужие данные.
- **Подтверждение.** `RepositoryPathResolverTests.GetCloneFolder_InvalidNames_Throws` ожидает исключение, а код его не бросает. Похоже, защита потеряна в текущих незакоммиченных изменениях.
- **Исправление.** Скопировать из `GetWikiCloneFolder` вызовы `EnsureValidNames(owner, repository)` и `ContainedFolder.Combine(...)`. Проверить, что тест проходит.

### 4. I2 CRITICAL — `GitClient.UpdateAsync` и `ValidateBranch`: ветка может стать опцией git
- **Что в коде.** `["fetch", "origin", request.Branch ?? "HEAD", …]` идёт без `--`. `ValidateBranch` проверяет только пустоту. Документация `SyncWorkingTreeAsync` обещает «does not start with `-`», но это нигде не проверяется.
- **Риск.** Ветка `--upload-pack=<cmd>` из конфига или командной строки превращается в опцию и приводит к выполнению команды. Строка вида `a:refs/heads/x` или `+x` работает как refspec и меняет локальные ссылки.
- **Исправление.**
  - В `ValidateBranch` отклонять ведущий `-`, пробелы, управляющие символы, `:`, `?`, `*`, `[`, `\`, `..`, `@{`, `//` и суффиксы `.lock` и `/`. Либо вызывать `git check-ref-format --branch`.
  - Перенести опции вперёд и поставить `--` перед позиционными аргументами: `fetch --prune --prune-tags --tags --force -- origin <branch>`.

### 5. I1/F CRITICAL — `SevenZipArchiver.Validate` и `CreateAsync`: `FileName` может выйти за папку назначения
- **Что в коде.** Документация `Validate` говорит, что имя файла с папкой отклоняется. Код проверяет только `IsNullOrWhiteSpace`. Затем `finalPath = Path.Combine(DestinationFolder, FileName)`.
- **Риск.** Имя `..\..\x.zip` или абсолютный путь записывает архив (`File.Move`/`File.Copy`) в произвольное место.
- **Исправление.**
  - Требовать `Path.GetFileName(FileName) == FileName`, отсутствие недопустимых символов и расширение `.zip`.
  - После `Combine` проверить через `GetFullPath`, что результат лежит внутри `DestinationFolder`.

## IMPORTANT

### 6. P1 IMPORTANT — `GitEnvironment.Create`: нет `GIT_TERMINAL_PROMPT=0`
- **Что в коде.** Документация метода и правило `external-processes.md` говорят, что переменная выставляется. Код её не добавляет.
- **Подтверждение.** `GitClientTests.cs:407` ожидает значение `"0"`, поэтому этот тест, скорее всего, падает.
- **Риск.** При отказе в авторизации (токен истёк, приватный репозиторий без токена) git может запросить пароль в консоли. В тихом режиме процесс зависнет.
- **Исправление.** Добавить `new("GIT_TERMINAL_PROMPT", "0")` в `variables`.

### 7. I1/P1 IMPORTANT — `GitEnvironment.Create`: при `;` в пути потолок отключается
- **Что в коде.** Если `ceilingDirectory` содержит `;`, `GIT_CEILING_DIRECTORIES` не ставится. `;` допустим в имени папки Windows.
- **Риск.** Если корень клонов лежит внутри другого git-репозитория, а `.git` у клона нет, git поднимается к родителю. Тогда `reset --hard` и `clean -ffdx` затронут чужой репозиторий.
- **Исправление.**
  - Закрывать при ошибке: бросать исключение, если путь содержит `;`.
  - Или перед шагами синхронизации проверять, что `.git` есть в самой папке клона (через `IFileSystem`) либо что `git rev-parse --show-toplevel` совпадает с `ClonePath`.

### 8. I3 IMPORTANT — `GitClient.CloneAsync` и `SetRemoteUrlAsync`: URL не проверяется
- **Что в коде.** Проверяется только «не пусто» и (в `SetRemoteUrlAsync`) «не начинается с `-`».
- **Риск.** Можно клонировать `file://`, локальный путь или экзотический транспорт, а не только `https://github.com/<owner>/<repo>`. Токен тоже зависит от подстановки `https://` в п. 1.
- **Исправление.** Проверять через `GitHubUrl.TryParseRepository` либо точное соответствие шаблону. В `SetRemoteUrlAsync` добавить `--` перед URL. Для тестов с `file://` оставить путь через `AdditionalConfiguration`.

### 9. P1 IMPORTANT — `GitEnvironment.Create`: окружение родителя не очищается
- **Что в коде.** `ProcessRunner` только добавляет переменные в унаследованное окружение (`startInfo.Environment[name] = value`).
- **Риск.** Унаследованные `GIT_DIR`, `GIT_WORK_TREE`, `GIT_INDEX_FILE`, `GIT_ASKPASS`, `GIT_SSH_COMMAND`, `GIT_EXEC_PATH` перенаправят операции или выполнят чужой код. `GIT_DIR` вместе с `reset --hard` особенно опасен.
- **Исправление.** Удалять эти переменные в `ProcessRunner` (`startInfo.Environment.Remove`) для git-процессов. Для этого нужно расширить `ProcessRequest`.

## SUGGESTION (защита в глубину)

- **`GitEnvironment.Create`, содержимое клона не доверенное.** Добавить в конфигурацию `protocol.ext.allow=never`, `protocol.file.allow=never` (для тестов — через `AdditionalConfiguration`), `core.hooksPath` на пустую папку и `core.fsmonitor=false`. Это ограничивает вредоносные `.gitmodules` и хуки из шаблонов пользователя.
- **`SevenZipArchiver.CreateAsync`, имя источника в аргументах 7-Zip.** `--` останавливает разбор опций, но `@имя` по-прежнему читается как список файлов, а `*` и `?` — как шаблоны. Имена репозиториев GitHub таких символов не содержат. Но `SourceFolder` проверяется только на «абсолютный путь», поэтому стоит отклонять `@`, `*`, `?` в `sourceName`.
- **`SevenZipArchiver.CreateAsync`, ссылки и junction.** Правило `core.symlinks=false` защищает только от ссылок, созданных git. Если в клоне окажется junction или symlink другого происхождения, 7-Zip может заархивировать содержимое цели. Стоит проверить поведение по умолчанию на Windows и рассмотреть `-snl`.
- **`RepositoryPathResolver.GetSourcesRoot`.** Значение `config.SourcesRoot` не проверяется на абсолютный путь. Сейчас ошибку поймают `ValidateClonePath` и `Validate`, но лучше отклонять относительный путь здесь, на входе.

## Что в порядке

- **`GitEnvironment`:**
  - токен идёт через `GIT_CONFIG_*` и не пишется в `.git/config`;
  - значение заголовка помечено как секрет;
  - `credential.helper` очищен;
  - `core.symlinks=false`.
- **`GitClient`:**
  - `clone … -- url path` защищён от опций;
  - вывод ошибок и команд маскируется (кроме п. 2);
  - потолок для поиска репозитория ставится через нормализованный родитель.
- **`SevenZipArchiver`:**
  - архив не перезаписывается;
  - временные файлы создаются с GUID и удаляются при сбое и отмене;
  - перенос между томами идёт через копию и переименование.
- **`RepositoryPathResolver.GetWikiCloneFolder`:** валидация имён и проверка границы корня сделаны правильно.

Начать стоит с п. 1–3. Они закрывают утечку токена и выход за корень, и п. 3 уже подтверждён упавшим тестом.
