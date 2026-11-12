# Обзор безопасности: 4 файла

Правки в файлах я не вносил. Коротко: нашёл 3 критичные проблемы и 5 важных. Критичные: токен попадает в `.git/config`, в лог и в аргументы процесса, а ветка может быть разобрана как опция git. Всё проверено чтением кода. Тесты и `git diff` я не запускал.

## CRITICAL

### 1. S1 `GitClient.CloneAsync`: токен в URL клона
- **Риск.** Код подставляет токен в URL: `x-access-token:<token>@`, и этот URL идёт аргументом в `git clone`. Это нарушает S1, FR-11.3 и `external-processes.md`. Последствия:
  - токен виден в командной строке процесса;
  - git записывает URL в `remote.origin.url` внутри `.git/config`;
  - клон архивируется как есть, поэтому токен попадает в ZIP;
  - `GitClientLog.Cloning(logger, authenticatedUrl, …)` пишет URL с токеном в лог без маскировки.
- **Исправление.** Передавайте в `clone` чистый `remoteUrl`. Заголовок авторизации уже добавляет `CreateEnvironment(request.Token, …)` через `GIT_CONFIG_*`, поэтому подстановка в URL лишняя. Удалите `authenticatedUrl` и логируйте `remoteUrl`. Добавьте тест: после клона в `.git/config` нет токена.

### 2. S1/S2 `GitClient.GetRemoteRefsAsync`: токен в аргументах и не зарегистрирован в маскировщике
- **Риск.** Метод кладёт `-c http.…extraHeader=Authorization: Basic …` в аргументы git, а не в окружение. Токен виден в списке процессов и в залогированной командной строке. Метод вызывает `CreateEnvironment(token: null, …)`, поэтому `secretMasker.Register` не вызывается. Если метод запущен первым, при ошибке `secretMasker.Mask(ProcessCommandLine.Format("git", arguments))` не знает токена. Заголовок попадёт в текст `GitCommandException`.
- **Исправление.** Уберите `-c`-вариант. Вызывайте `CreateEnvironment(request.Token, request.ClonePath)`, как `GetDefaultBranchAsync`, и передавайте `_remoteRefsArguments` без изменений.

### 3. I2 `GitClient.UpdateAsync` / `ValidateBranch`: инъекция опции через имя ветки
- **Риск.** `ValidateBranch` проверяет только пустоту. В `UpdateAsync` ветка идёт позиционным аргументом: `["fetch", "origin", request.Branch ?? "HEAD", …]`, без `--`. Ветка `--upload-pack=<cmd>` из конфигурации станет опцией git. Это путь к выполнению команды.
- **Тот же дефект для ветки от сервера.** `SynchronizeAsync` вызывает `ValidateBranch(defaultBranch, …)`, а XML-документация `SyncWorkingTreeAsync` обещает «does not start with `-`». Код этого не проверяет.
- **Исправление.**
  1. В `ValidateBranch` отклоняйте имена с ведущим `-`, пробелами, управляющими символами и `..`. Можно дополнительно проверять через `git check-ref-format --branch`.
  2. Вставьте `--` перед `origin` в `fetch`.
  3. Проверяйте и `request.Branch`, и `defaultBranch`.

## IMPORTANT

### 4. I1 `RepositoryPathResolver.GetCloneFolder`: нет проверки имён и границы корня
- **Риск.** Метод делает простой `Path.Combine(GetSourcesRoot(config), owner, repository)`. Имя `..` или абсолютный сегмент (`C:\Windows`) выводит клон за пределы корня. Затем на нём выполняются `reset --hard` и `clean -ffdx`. Имена устройств и хвостовые точки тоже проходят. `GetWikiCloneFolder` соседний метод уже защищён: он вызывает `EnsureValidNames` и `ContainedFolder.Combine`. Для основного клона эта защита отсутствует.
- **Исправление.** Сделайте так же: `EnsureValidNames(owner, repository); return ContainedFolder.Combine(GetSourcesRoot(config), owner, repository);`.
- **Что не проверял.** Для `GetArchiveFolder` защиту должен давать `ArchiveFolder.Resolve`. Я его не читал, потому что он в другом файле.

### 5. P1 `GitEnvironment.Create`: нет `GIT_TERMINAL_PROMPT=0`
- **Риск.** Метод ставит только `GCM_INTERACTIVE=never`. Документация метода и `external-processes.md` требуют и `GIT_TERMINAL_PROMPT=0`. Тест `GitClientTests` (около строки 407) ожидает `variables["GIT_TERMINAL_PROMPT"] == "0"`. Без переменной git может запросить логин в консоли. В тихом режиме процесс зависнет.
- **Исправление.** Добавьте `new("GIT_TERMINAL_PROMPT", "0")` в `variables`. Похоже, переменную удалили в текущей незакоммиченной правке.

### 6. P1/I1 `GitEnvironment.Create`: отказ в защите при `;` в пути
- **Риск.** Если путь содержит `;`, `GIT_CEILING_DIRECTORIES` молча не задаётся. Тогда git в папке без `.git` ищет репозиторий выше и может выполнить `reset --hard` и `clean -ffdx` в чужом, объемлющем репозитории. Это разрушительно.
- **Исправление.** Закрывайте защиту наглухо:
  - бросайте исключение, если ceiling задать нельзя;
  - либо перед разрушительными шагами проверяйте, что `git rev-parse --show-toplevel` равен `ClonePath`.

### 7. I1 `SevenZipArchiver.Validate` / `CreateAsync`: имя файла не проверяется
- **Риск.** Документация `Validate` обещает отклонять `FileName` с папкой, но код проверяет только пустоту. `finalPath = Path.Combine(DestinationFolder, FileName)`:
  - значение `..\..\x.zip` выводит архив за пределы папки назначения;
  - абсолютный путь полностью заменяет `DestinationFolder`;
  - символ `:` даёт запись в альтернативный поток NTFS.
- **Исправление.** В `Validate` требуйте `FileName == Path.GetFileName(FileName)`. Отклоняйте `Path.GetInvalidFileNameChars()` и `:`. Затем проверяйте через `Path.GetFullPath`, что `finalPath` лежит внутри `DestinationFolder`.

### 8. I3 `GitClient.CloneAsync` и `SetRemoteUrlAsync`: URL не валидируется
- **Риск.** `CloneAsync` проверяет только непустоту. `SetRemoteUrlAsync` проверяет пустоту и ведущий `-`, но не схему. Схемы `ssh://`, `file://`, `ext::` и URL с логином и паролем не отклоняются. `SetRemoteUrlAsync` записывает URL в `.git/config`. Если в нём есть учётные данные, они сохранятся в клоне и попадут в архив. В `SetRemoteUrlAsync` нет `--` перед `remoteUrl`. Защищает только проверка `-`.
- **Исправление.**
  - Принимайте только `https://github.com/<owner>/<repo>[.git]` без userinfo. Подойдёт `GitHubUrl.TryParseRepository`.
  - Добавьте `--` в `["remote", "set-url", "origin", "--", …]`, если эта форма поддерживается вашей версией git. Иначе оставьте проверку на `-`.

### 9. P2 `GitClient`: путь к git не проверяется
- **Риск.** `request.GitPath` идёт в `ProcessRequest.FileName` без проверки. `ProcessRunner` создаёт `ProcessStartInfo(request.FileName)`. `SevenZipArchiver.Validate` требует полный путь для `SevenZipPath`, а у git такой проверки нет. Относительное имя вроде `git` приводит к поиску исполняемого файла по PATH и рабочим папкам.
- **Исправление.** В `ValidateClonePath` или рядом проверяйте `Path.IsPathFullyQualified(request.GitPath)`. Либо гарантируйте это в `IToolLocator`.

## SUGGESTION

### 10. `GitEnvironment.Create`: ограничение протоколов для подмодулей
`submodule update --recursive` выполняется над недоверенным `.gitmodules`. Добавьте переменную `GIT_ALLOW_PROTOCOL=https`. Тогда подмодуль не сможет использовать `ssh`, `file` или `ext`. Тестам `file://`-подмодулей понадобится исключение, например через `AdditionalConfiguration`.

### 11. `SevenZipArchiver.CreateAsync`: символические ссылки и точки повторной обработки
`core.symlinks=false` защищает от ссылок из git. Добавьте ещё `-snl` в `7z a`, чтобы 7-Zip не следовал за ссылками и junction. Это защита в глубину.

### 12. `GitClient.ValidateClonePath` и `CreateAsync`: клон и источник вне корня
Оба метода требуют только полный путь. Принадлежность корню гарантирует вызывающий код (`RepositoryPathResolver`). Если исправить пункт 4, этого достаточно.

## Что в порядке
- `SevenZipArchiver.CreateAsync`: аргументы идут списком, перед путями стоит `--`, и это отключает разбор `@listfile`. Перенос в итоговое имя никогда не перезаписывает существующий файл. Временные файлы удаляются и при ошибке, и при отмене.
- `GitClient.SyncWorkingTreeAsync`: ветка имеет вид `origin/<branch>`, после `reset` и `checkout` стоит `--`. Параметр `-B` забирает имя как значение, а не как опцию.
- `GitClient.RunGitAsync`: командная строка и сообщения об ошибках проходят через `secretMasker`.
- `GitEnvironment.Create`: токен передаётся только через `GIT_CONFIG_VALUE_n`, значение помечено секретным. Ключ `http.https://github.com/.extraHeader` ограничен хостом GitHub. `credential.helper` очищается.
- `ProcessRunner.CreateStartInfo`: переменные `GitHubBackup__*` вычищаются из окружения дочерних процессов.
- `RepositoryPathResolver.GetWikiCloneFolder`: проверки `EnsureValidNames` и `ContainedFolder.Combine` корректны.
