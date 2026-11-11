# План: #14 F1.7: Git client: ref snapshot, default branch, empty repository

| | |
|---|---|
| Issue | [#14](https://github.com/askrinnik/GitHubBackup/issues/14) |
| Линия | Feature |
| Сложность | L |
| Дата | 2026-11-10 |

## 1. Цель

Дополнить `IGitClient` (часть 2) тремя возможностями:
- **Снимок ссылок.** Удалённый снимок через `git ls-remote` и локальный через `git for-each-ref`, в одном формате: `heads/<name>` / `tags/<name>` → SHA. Требования: FR-6.1, FR-6.2 п. 3–4, П-8.
- **Ветка по умолчанию на сервере.** Если ветка не указана, клон берёт ветку по умолчанию. Если указанной ветки на сервере нет, клон откатывается на ветку по умолчанию с предупреждением (FR-4.4).
- **Пустой репозиторий.** Clone и update на нём не падают. Снимок пустой. Результат сообщает «пусто», чтобы вызывающий код не архивировал. Событие пишется в лог (FR-4.5).

## 2. Критерии приёмки

1. Test: ls-remote and for-each-ref snapshots match for one state
2. Tests: remote branch missing (fallback), empty repository, annotated tag
3. Covered by tests, CI green
4. (добавлено, П-8) В удалённый снимок не попадают `refs/pull/*`, `HEAD`, peeled-строки `^{}` и прочие ссылки вне heads/tags. В локальном снимке нет `origin/HEAD`.
5. (добавлено, FR-4.4) Ветка не задана → checkout ветки по умолчанию на сервере. Если ветка по умолчанию на сервере сменилась, это видно и при update.
6. (добавлено, FR-4.4) При откате результат несёт признак предупреждения, а в лог пишется запись уровня `Warning`.
7. (добавлено, FR-4.5) Пустой репозиторий: клон создан, запись в логе, `IsEmpty = true`. Если потом на сервер пришёл первый коммит, update штатно делает checkout.
8. (добавлено, сверка с кодом) Удалённые на сервере ветки и теги, а также перенесённый тег: снимки расходятся до update и совпадают после него.
9. (добавлено, NFR-1) Новые команды (`ls-remote`) получают токен только через окружение. Токена нет в аргументах и в логе.

## 3. Изменения PRD

Не нужны.

## 4. Затрагиваемые проекты и типы

**GitHubBackup.Core** (`Core/Git/`)
- `RefSnapshot` (новый, sealed): неизменяемое отображение `heads/<name>`/`tags/<name>` → SHA с ordinal-сравнением, равенством по содержимому (`IEquatable`), `Empty`, `Refs` и `ContainsBranch(name)`.
- `GitSyncResult` (новый record): `string? Branch` (ветка checkout; `null`, если репозиторий пуст), `bool BranchFallback`, `RefSnapshot Refs` (локальный снимок после синхронизации), `bool IsEmpty => Branch is null`.
- `GitBranchNotFoundException` (новый): на сервере есть ветки, но ни запрошенную ветку, ни ветку по умолчанию определить нельзя.
- `GitRepositoryRequest`: `Branch` становится `string?` (`null` = ветка по умолчанию на сервере).
- `IGitClient`: `CloneAsync`/`UpdateAsync` возвращают `Task<GitSyncResult>`. Новые методы: `GetRemoteRefsAsync`, `GetLocalRefsAsync`, `GetDefaultBranchAsync` → `Task<string?>`.

**GitHubBackup.Infrastructure** (`Infrastructure/Git/`)
- `GitRefParser` (новый, internal static): разбор вывода `ls-remote`, `for-each-ref` и `ls-remote --symref`.
- `GitClient`: новые методы; общий шаг «снимок → выбор ветки → синхронизация» после clone/fetch; `Validate` делится на проверку пути и проверку ветки; вариант `RunGitAsync` с возвратом stdout.
- `GitClientLog`: события `BranchNotFound` (Warning), `EmptyRepository` (Information), `UsingDefaultBranch` (Information).

## 5. Подход

1. **Удалённый снимок:** `git ls-remote --refs origin` в клоне, токен через окружение. Парсер оставляет только `refs/heads/*` и `refs/tags/*` (П-8).
2. **Локальный снимок:** `git for-each-ref --format=%(objectname)%09%(refname) refs/remotes/origin refs/tags`; `refs/remotes/origin/<name>` → `heads/<name>`, `origin/HEAD` исключается. Для аннотированного тега обе команды дают SHA объекта тега.
3. **Ветка по умолчанию:** `git ls-remote --symref origin HEAD`, строка `ref: refs/heads/<name>\tHEAD`; пустой сервер → `null`. Вызов идёт только когда ветка не задана или отсутствует.
4. **Синхронизация** после `clone --no-checkout` / `fetch`: локальный снимок → веток нет: `EmptyRepository`, checkout не выполняется → ветка задана и есть: она → иначе ветка по умолчанию (`BranchFallback` + Warning, если ветка была задана) → `GitBranchNotFoundException`, если и её нет → проверка имени ветки → существующие шаги синхронизации.
5. Вызывающий код узнаёт о предупреждении через `GitSyncResult.BranchFallback`, о пустом репозитории — через `IsEmpty`.
6. Отсутствующая ветка больше не даёт `GitCommandException`: старый интеграционный тест заменяется тестом на fallback.

## 6. Тесты

- **Core.Tests:** `RefSnapshotTests`; `GitRepositoryRequestTests` с `Branch = null`.
- **Infrastructure.Tests:** `GitRefParserTests`; расширенный `GitClientTests` (новые операции в теориях про токен и аргументы, fallback, ветка по умолчанию, пустой снимок, `GitBranchNotFoundException`, ветка с ведущим `-`).
- **IntegrationTests:** равенство снимков (ветки, аннотированный и лёгкий теги, `refs/pull/1/head`); аннотированный тег; изменения на сервере до и после update; ветка по умолчанию; fallback при clone и update; смена ветки по умолчанию; пустой репозиторий при clone и update; первый коммит в пустой репозиторий; токен в новых командах.

## 7. Вне рамок

- Алгоритм FR-6.2 (`IChangeDetector`), файл состояния, перевод `BranchFallback` в статус `Warning` и пропуск архивации при `IsEmpty` — следующие issue фазы 1.
- Полный SHA `HEAD` для записи об архиве (FR-6.1).
- Wiki (FR-5.x).
- Сервер, на котором удалили все ветки после заполнения клона: результат `IsEmpty`, рабочее дерево не очищается.

## 8. Решения

1. «Пустой репозиторий» = на сервере нет ни одной ветки; теги не учитываются.
2. Ветки есть, но ни запрошенная ветка, ни `HEAD` сервера не указывают на существующую ветку → `GitBranchNotFoundException`.
3. `CloneAsync`/`UpdateAsync` возвращают `GitSyncResult`, `GitRepositoryRequest.Branch` nullable — потребителей кроме тестов нет.
4. `GitSyncResult` несёт локальный снимок после синхронизации.
5. По итогам security review в эту же задачу входят три доработки:
   - `GetDefaultBranchAsync` не возвращает недопустимое имя ветки от сервера: пустое, с ведущим `-` или с управляющими символами даёт `null`.
   - Плохой ответ сервера (дубликат ссылки, ссылка без имени) даёт `GitCommandException` с путём клона, а не `ArgumentException`.
   - Команды в существующем клоне получают `GIT_CEILING_DIRECTORIES` с родительской папкой клона: git не поднимается к чужому репозиторию выше клона.
6. По итогам повторного security review:
   - родительская папка берётся из нормализованного пути клона, поэтому `\` или `\.` в конце пути не отключают ограничение;
   - путь с `;` не попадает в `GIT_CEILING_DIRECTORIES`, потому что git делит переменную по этому символу;
   - имя ветки по умолчанию с Unicode-символами форматирования (`U+202E`, `U+200B`) считается недопустимым;
   - строки вывода git в сообщениях об ошибках обрезаются до 200 символов, управляющие и форматирующие символы заменяются на `?`.

## 9. Задачи

- [x] Core: `RefSnapshot`, `GitSyncResult`, `GitBranchNotFoundException`
- [x] Core: `GitRepositoryRequest.Branch` → `string?`; обновить `IGitClient`
- [x] Core.Tests: `RefSnapshotTests`, `Branch = null` в `GitRepositoryRequestTests`
- [x] Infrastructure: `GitRefParser`
- [x] Infrastructure: `GitClient` — новые методы, выбор ветки, пустой репозиторий
- [x] Infrastructure: новые события в `GitClientLog`
- [x] Infrastructure.Tests: `GitRefParserTests`, расширенный `GitClientTests`
- [x] IntegrationTests: снимки, ветка по умолчанию, fallback, пустой репозиторий, токен
- [x] Сборка, тесты, `dotnet format --verify-no-changes`
- [x] Проверка комментариев в diff
- [x] Security: проверка имени ветки по умолчанию от сервера
- [x] Security: плохой ответ сервера → `GitCommandException`
- [x] Security: `GIT_CEILING_DIRECTORIES` для команд в существующем клоне
