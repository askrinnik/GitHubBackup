# Ревью безопасности: 4 файла

**Итог:** в `GitClient.cs` я нашёл три критичные проблемы, и все три связаны с токеном. Токен попадает в URL клона, в логи и в аргументы процесса. Ещё две критичные проблемы — внедрение аргументов через имя ветки и выход за пределы корня через `GetCloneFolder`. В `SevenZipArchiver.cs` `FileName` не проверяется. В `GitEnvironment.cs` нет `GIT_TERMINAL_PROMPT`. Файлы я не менял. Оценки даны по `.claude/rules/security.md`.

## CRITICAL

**1. S1 · `GitClient.CloneAsync` — токен в URL, в `.git/config`, в аргументах и в логе**
- **Риск:**
  - `authenticatedUrl` подставляет `x-access-token:<token>@` в URL.
  - Этот URL уходит в `git clone` аргументом процесса. Его видят другие процессы через WMI или диспетчер задач.
  - Git записывает его в `.git/config` как `origin`, а клон потом архивируется вместе с токеном.
  - `GitClientLog.Cloning(logger, authenticatedUrl, …)` пишет URL в лог без `secretMasker.Mask`.
  - `Replace("https://", …)` заменяет все вхождения, а не только схему.
  - Хост не проверяется, поэтому токен уйдёт на любой `https://`-адрес из `remoteUrl`. Это нарушает I3.
- **Исправление:**
  - Удалить `authenticatedUrl` и передавать `remoteUrl` как есть. Заголовок авторизации уже приходит через `CreateEnvironment(request.Token, …)`.
  - Перед клоном проверять `remoteUrl` через `GitHubUrl`: схема `https`, хост `github.com`, без `userinfo`.
  - Если тестам нужен `file://`, разрешать его только через `AdditionalConfiguration`, а не в продакшен-пути.

**2. S1/S2 · `GitClient.GetRemoteRefsAsync` — токен в аргументах и возможная утечка в исключении**
- **Риск:**
  - Токен (Base64) передаётся как `-c http…extraHeader=Authorization: Basic …`, то есть аргументом процесса. Это прямо запрещено в `external-processes.md`.
  - `CreateEnvironment(token: null, …)` не вызывает `Register(EncodeCredentials(token))`. Сырой токен регистрируется в `TokenProvider`, а Base64-форма — только в `CreateEnvironment`.
  - Если этот метод вызван раньше любого клона или обновления, сбой пройдёт через `RunGitAsync`. Тогда `secretMasker.Mask(command)` не замаскирует заголовок, и токен попадёт в текст `GitCommandException` и в лог команды. Это зависит от порядка вызовов, я его не проверял.
- **Исправление:** убрать ветку с `-c`, использовать `CreateEnvironment(request.Token, …)` и `_remoteRefsArguments`. Это тот же путь, что у остальных методов, и регистрация в маскере сработает.

**3. I2 · `GitClient.UpdateAsync` — имя ветки как опция git**
- **Риск:**
  - Ветка идёт в `["fetch", "origin", request.Branch ?? "HEAD", …]` без `--`.
  - `ValidateBranch` проверяет только пустоту и пробелы. Ветка вида `--upload-pack=…` из `backup-config.json` разбирается как опция.
  - Комментарий к `SyncWorkingTreeAsync` обещает «does not start with `-`», но этого нигде не проверяется.
- **Исправление:**
  - В `ValidateBranch` отклонять ветку, которая начинается с `-`, содержит управляющие символы, `..`, `@{`, пробел, `~ ^ : ? * [ \`, заканчивается на `.lock` или `/`. То есть реализовать правила `git check-ref-format`.
  - В `fetch` дополнительно ставить `--` перед `origin`.
  - `ValidateBranch(defaultBranch, …)` вызывается и для ветки с сервера, так что исправление закроет и её.

**4. I1 · `RepositoryPathResolver.GetCloneFolder` — нет проверки имён и границ корня**
- **Риск:**
  - `Path.Combine(GetSourcesRoot(config), owner, repository)` не вызывает ни `EnsureValidNames`, ни `ContainedFolder.Combine`.
  - Сегмент `..` выводит путь из корня. Сегмент с диском или абсолютным путём заменяет корень целиком. Имена устройств (`NUL`) и хвостовые точки тоже не отсекаются.
  - `GetWikiCloneFolder` рядом защищён, а этот метод нет.
  - На путь клона потом опираются `clean -ffdx` и `reset --hard`, то есть он определяет, что будет удалено.
  - Возможно, имена проверяются выше по стеку. Вызывающий код я не прочитал, но на него полагаться нельзя.
- **Исправление:** сделать как в `GetWikiCloneFolder`: `EnsureValidNames(owner, repository)`, затем `ContainedFolder.Combine(GetSourcesRoot(config), owner, repository)`.

## IMPORTANT

**5. I1/F2 · `SevenZipArchiver.Validate` и `CreateAsync` — `FileName` не проверяется на каталог**
- **Риск:**
  - Документация метода обещает отклонять «file name carries a folder», но код проверяет только пустоту.
  - Значение вроде `..\..\x.zip`, `C:\x.zip` или `a:stream` попадает в `Path.Combine(request.DestinationFolder, request.FileName)`. Итоговый архив оказывается вне папки назначения.
- **Исправление:** требовать `fileSystem.Path.GetFileName(request.FileName) == request.FileName`. Дополнительно отклонять `:` и недопустимые символы (`Path.GetInvalidFileNameChars`) и `GitHubUrl.IsWindowsDeviceName`.

**6. P1 · `GitEnvironment.Create` — нет `GIT_TERMINAL_PROMPT=0`**
- **Риск:**
  - Документация метода и `external-processes.md` обещают эту переменную, но `Create` её не добавляет. Остался только `GCM_INTERACTIVE`.
  - При отклонённом токене git может запросить пароль на консоли и зависнуть в тихом режиме.
  - Тест `GitClientTests.cs:407` ожидает `variables["GIT_TERMINAL_PROMPT"] == "0"`, значит переменную, скорее всего, убрали по ошибке. Я не смотрел `git diff` этого файла.
- **Исправление:** добавить `new("GIT_TERMINAL_PROMPT", "0")`. По желанию задать пустые `GIT_ASKPASS` и `SSH_ASKPASS`, чтобы не наследовать их от родителя.

**7. I1 · `GitEnvironment.Create` и `GitClient.CreateEnvironment` — защита от выхода за клон отключается молча**
- **Риск:**
  - Если путь содержит `;` (например, `SourcesRoot = C:\my;backups`), `GIT_CEILING_DIRECTORIES` просто не задаётся.
  - Если в папке клона нет `.git`, а выше лежит другой репозиторий, `reset --hard` и `clean -ffdx` сработают в нём и удалят чужие файлы.
  - Защита тихо пропадает именно там, где она нужнее всего.
- **Исправление:**
  - Не работать молча: бросать исключение, если `ceilingDirectory` содержит `;`.
  - Лучше проверять перед разрушающими шагами, что `git rev-parse --show-toplevel` равен `ClonePath`, или что `.git` есть в самой папке клона.

**8. I3 · `GitClient.SetRemoteUrlAsync` — URL без проверки схемы, хоста и учётных данных**
- **Риск:**
  - Проверяются только пустота и ведущий `-`.
  - Если вызывающий код передаст URL с токеном, он запишется в `.git/config`.
  - Если передать URL с другим хостом или схемой, клон начнёт ходить туда.
- **Исправление:** проверять `https://github.com/<owner>/<repo>` без `userinfo`, как в пункте 1.

## SUGGESTION

**9. Подмодули. `GitClient.SyncWorkingTreeAsync`.**
- `submodule update --init --recursive` берёт URL из `.gitmodules` чужого репозитория, то есть из недоверенных данных. Токен уйдёт только на `github.com`, но клонирование произвольных хостов, включая локальную сеть, возможно.
- **Исправление:** задавать `GIT_ALLOW_PROTOCOL=https` через `GitEnvironment`. Тестам с `file://` нужно будет разрешение через `AdditionalConfiguration`.

**10. F2. `SevenZipArchiver.Move`, межтомная ветка.**
- Если `File.Delete(temporaryPath)` не удался, файл остаётся в `.tmp` и только логируется. Правило F2 требует удалять временные файлы.
- **Исправление:** добавить очистку остатков в начале следующего запуска.

**11. `RepositoryPathResolver.GetSourcesRoot`.**
- `config.SourcesRoot` не проверяется на полную квалифицированность, и `ContainedFolder` использует `Path.GetFullPath` от текущего каталога процесса. `ValidateClonePath` в `GitClient` это ловит позже.
- **Исправление:** проверять `Path.IsPathFullyQualified` сразу в `GetSourcesRoot`.

## Что проверено и в порядке
- `SevenZipArchiver`:
  - `--` стоит перед путями, поэтому имена не читаются как опции.
  - Перезапись архива исключена: `File.Move` и `File.Copy` вызываются без `overwrite`.
  - Временный файл получает уникальное имя и удаляется при ошибке и отмене.
- `GitEnvironment`:
  - Токен идёт только в переменную, помеченную как секретная.
  - `core.symlinks=false` и пустой `credential.helper` заданы.
- `GitClient`:
  - Сообщения об ошибках проходят через `secretMasker.Mask`.
  - `IsWindowsDeviceName` и `.wiki`-коллизия имён уже учтены в `GitHubUrl`.

Не прочитал `ArchiveFolder.Resolve`, где обрабатываются `account.Path` и `repository.Path` из конфигурации. Эти пути тоже недоверенные, их стоит проверить отдельно.
