**Итог.** Нашёл одну проблему уровня CRITICAL и три IMPORTANT. Ещё есть три замечания уровня SUGGESTION. Файлы я не менял. Формат записей: `<ID> <severity> <файл> <член>`.

## CRITICAL

**S1 CRITICAL `GitClient.cs` `CloneAsync`: токен попадает в лог и в аргументы процесса**
- **Риск.** Строка 78 вставляет токен в URL: `https://x-access-token:<token>@…`.
  - Эту строку получает `GitClientLog.Cloning(logger, authenticatedUrl, …)` (строка 79, шаблон `Cloning {Url} …`). Токен уходит в лог открытым текстом, потому что маскирование к `{Url}` здесь не применяется.
  - Тот же URL идёт аргументом в `git clone`. Токен виден в списке процессов и записывается в `.git/config` как `remote.origin.url`, а клон потом попадает в архив.
  - Это прямое нарушение S1, NFR-1 и FR-11.3.
  - `RunGitAsync` маскирует сообщения об ошибках, но маскирование не заменяет запрет на токен в аргументах.
- **Исправление.** Не подставлять токен в URL. Клонировать с чистым `remoteUrl` и передавать токен в `GIT_CONFIG_*` через `GitEnvironment.Create(token, …)`, как это уже делают `GetRemoteRefsAsync` и `UpdateAsync`. В `CreateEnvironment` для клона передавать `request.Token`, в лог — `remoteUrl`. Если URL всё же нужно логировать, прогонять его через `secretMasker.Mask`.
- **Дополнительно.** `remoteUrl.Replace("https://", …)` заменяет все вхождения, а не только схему. Если токен не задан, URL уходит без проверки на `https://github.com/` (I3). Нужна проверка, что `remoteUrl` — это `https://github.com/<owner>/<repo>`.

## IMPORTANT

**I1 IMPORTANT `RepositoryPathResolver.cs` `GetCloneFolder`: нет защиты от выхода из корня**
- **Риск.** Метод делает `Path.Combine(GetSourcesRoot(config), owner, repository)` без проверки.
  - `GetWikiCloneFolder` проверяет имена через `EnsureValidNames` и `ContainedFolder.Combine`. `GetCloneFolder` не делает ни того ни другого.
  - Он вызывается из `BackupWorkItem` и `DiscoveryDecisionApplier`, а `owner` и `repository` приходят из GitHub API и из конфигурации.
  - Имя вроде `..` или `C:\x` выводит клон за пределы корня. `Path.Combine` с rooted-сегментом молча отбрасывает корень.
  - Дальше `git clean -ffdx` и `reset --hard` работают в чужой папке.
- **Исправление.** Привести `GetCloneFolder` к виду `GetWikiCloneFolder`: вызвать `EnsureValidNames(owner, repository)`, затем `ContainedFolder.Combine(...)`.
- **Дополнительно.** `GetTemporaryArchiveFolder` берёт только константы, там всё безопасно.

**P1 IMPORTANT `GitEnvironment.cs` `Create`: не задан `GIT_TERMINAL_PROMPT=0`**
- **Риск.**
  - XML-doc возвращаемого значения обещает `GIT_TERMINAL_PROMPT`, `external-processes.md` требует его, а `GitClientTests.cs:407` проверяет его значение `"0"`. В коде переменной нет, в production-коде она нигде не задаётся.
  - Без неё git может запросить логин в консоли и зависнуть в тихом режиме. `GCM_INTERACTIVE=never` закрывает только Credential Manager.
  - Это может быть следствием правки, которая сейчас не закоммичена. Я не запускал тесты и не смотрел diff, потому что shell в этой сессии отключён.
- **Исправление.** Добавить `new("GIT_TERMINAL_PROMPT", "0")` в начало списка `variables`.

**P1/I2 IMPORTANT `GitClient.cs` `SetRemoteUrlAsync`, `UpdateAsync`: ввод попадает в позиции, где его можно принять за опцию**
- **Риск.**
  - `["remote", "set-url", "origin", remoteUrl]` идёт без `--`. Запрет на ведущий `-` есть, но нет запрета на пробелы, управляющие символы и схемы вроде `ext::`, которые git трактует особым образом. Я не проверял, ограничивает ли `GitUrl` схемы.
  - `["fetch", "origin", request.Branch ?? "HEAD", …]` тоже без `--`. Для ветки есть только проверка на пустоту (`ValidateBranch`). Ветка `--upload-pack=cmd` станет опцией и приведёт к выполнению команды. Комментарий к `SyncWorkingTreeAsync` говорит, что ветка «не начинается с `-`», но `ValidateBranch` этого не проверяет.
  - Цепочка `checkout -B branch origin/branch --` защищена через `--`. Но если `fetch` пропустил такую ветку, дальше идти не должно.
- **Исправление.**
  - В `ValidateBranch` отклонять ветку с ведущим `-`, пробелами и управляющими символами. Можно дополнительно проверить имя через `git check-ref-format --branch`.
  - Для `fetch` отдавать ветку как refspec с префиксом, например `refs/heads/<branch>`, или ставить `--` перед аргументами.
  - В `SetRemoteUrlAsync` проверять URL по тому же шаблону `https://github.com/<owner>/<repo>`, что и в `CloneAsync`.

## SUGGESTION

**S5 SUGGESTION `GitClient.cs` `GetRemoteRefsAsync`**
- Токен кладётся в аргументы процесса: `-c http.https://github.com/.extraHeader=Authorization: Basic …` (строка 123). Он виден в списке процессов, и это противоречит FR-11.3.
- В этом же методе `CreateEnvironment(token: null, …)` намеренно не передаёт токен, поэтому дублируется логика `GitEnvironment`.
- Исправление: передавать `request.Token` в `CreateEnvironment`, чтобы заголовок шёл через `GIT_CONFIG_*`, а `-c` убрать.

**F2 SUGGESTION `SevenZipArchiver.cs` `CreateAsync`**
- `request.FileName` проверяется только на пустоту. Doc к `Validate` обещает отклонять имя с папкой, но кода для этого нет. Имя `..\x.zip` или с rooted-сегментом изменит `finalPath` через `Path.Combine`.
- `copyPath` строится строковой интерполяцией от `finalPath`.
- Исправление: проверять `Path.GetFileName(request.FileName) == request.FileName`. Дополнительно сверять `Path.GetFullPath(finalPath)` с `DestinationFolder`, как в `ContainedFolder`.

**F4 SUGGESTION `SevenZipArchiver.cs` `CreateAsync`, `Move`**
- Между `File.Exists(finalPath)` и `File.Move` есть гонка (TOCTOU). `File.Move` без `overwrite` всё равно бросит `IOException`, поэтому перезаписи нет. Это замечание на уровне надёжности, не уязвимость.
- Исправление: ничего менять не нужно. Можно убрать предварительную проверку или оставить её как ранний отказ.

## Проверено и замечаний нет

- **`SevenZipArchiver.cs`.**
  - Аргументы идут списком, перед путями стоит `--`, пути проверяются на полную квалификацию.
  - Временный архив создаётся с уникальным именем в `.tmp` и удаляется при сбое.
  - Для symlink-ов есть защита через `core.symlinks=false`.
- **`GitEnvironment.cs`.**
  - Токен идёт только через `GIT_CONFIG_VALUE_n` с флагом `IsSecret`.
  - Пустой `credential.helper` и `GIT_CEILING_DIRECTORIES` реализованы разумно, `;` в пути обработан.
- **`RepositoryPathResolver.cs`.** `GetWikiCloneFolder` и `GetArchiveFolder` защищены через `EnsureValidNames`, `ContainedFolder` и разбор URL.

Какие правки делать первыми, решаете вы. Я бы начал с S1 и I1: они дают утечку токена и выход за пределы корня.
