# План: Dry run reports WouldClone for an enabled wiki that has no git repository

| | |
|---|---|
| Issue | [#132](https://github.com/askrinnik/GitHubBackup/issues/132) |
| Заголовок | Dry run reports WouldClone for an enabled wiki that has no git repository |
| Lane | Bug |
| Сложность | M |
| Дата | 2026-11-15 |

Требования: FR-5.2, FR-9.5 (а также NFR-1, FR-11.3).

## Причина

`ChangeDetector.PreviewAsync` сначала вызывает `InspectCloneAsync`. Если клона нет, метод сразу возвращает `ChangeDecision.CloneNeeded` (`ChangeReason.NoClone`) и к серверу не обращается. `BackupOrchestrator.BackupAsync` превращает это решение в `BackupItemStatus.WouldClone`.

Обычный запуск в том же случае вызывает `IGitClient.CloneAsync`. Для wiki без страниц сервер отвечает `Repository not found`, `GitClient` бросает `GitRepositoryNotFoundException`, а `BackupOrchestrator.ProcessWikiAsync` ловит её, пишет `WikiNotFound` и возвращает `null` (строки в итогах нет), как требует FR-5.2. Клон не появляется, поэтому каждый dry run снова показывает `WouldClone`.

Читать refs сервера без клона нечем: `IGitClient.GetRemoteRefsAsync` выполняет `git ls-remote --refs origin` в папке клона, а её нет.

## Подход

```
BackupOrchestrator.ProcessWikiAsync (dry run)
  └─ BackupAsync → ChangeDetectionRequest { CheckRemoteWithoutClone = true }
      └─ ChangeDetector.PreviewAsync
          ├─ клон есть          → как сейчас
          └─ клона нет
              ├─ флаг выключен  → CloneNeeded без обращения к серверу (основной репозиторий, как сейчас)
              └─ флаг включён   → IGitClient.GetRemoteRefsAsync(request, remoteUrl)
                  ├─ refs получены           → CloneNeeded (WouldClone)
                  └─ GitRepositoryNotFound   → исключение уходит в ProcessWikiAsync
                                               → WikiNotFound в логе, строки нет
```

- `IGitClient` (Core) получает перегрузку `Task<RefSnapshot> GetRemoteRefsAsync(GitRepositoryRequest request, string remoteUrl, CancellationToken cancellationToken)`: читает ветки и теги сервера по URL, клон не нужен. Исключения: `ArgumentException` (путь не полный, URL пустой), `GitRepositoryNotFoundException`, `GitCommandException`, `ProcessStartException`, `OperationCanceledException`.
- `GitClient` реализует её командой `git ls-remote --refs -- <remoteUrl>`. URL — отдельный аргумент после `--` (I2). Токен — только через переменные окружения процесса (`CreateEnvironment(request.Token, request.ClonePath)`), как для остальных команд, обращающихся к серверу (FR-11.3). Рабочая папка — ближайшая существующая папка-предок `ClonePath` (проверка через `IFileSystem`); dry run ничего не создаёт. Вывод разбирает `GitRefParser.ParseRemoteRefs` через `ParseOutput`; «not found» распознаёт существующий `EnsureSucceeded`.
- `ChangeDetectionRequest` получает свойство `bool CheckRemoteWithoutClone { get; init; }`. Оно действует только в `PreviewAsync`.
- `ChangeDetector.PreviewAsync`: если клона нет и флаг включён — сообщает `BackupProgressStage.CheckingRemote`, вызывает новую перегрузку и возвращает `CloneNeeded` с полученным `RefSnapshot`. `GitRepositoryNotFoundException` не перехватывается.
- `BackupOrchestrator.BackupAsync` ставит `CheckRemoteWithoutClone = source.Target == BackupTarget.Wiki`. `ProcessWikiAsync` не меняется: он уже ловит `GitRepositoryNotFoundException`.
- Основной репозиторий в dry run без клона ведёт себя как раньше (`WouldClone` без обращения к серверу), как требует issue.
- PRD 0.35: FR-5.2 — правило действует и в dry run (без клона wiki проверяется сервер, `ls-remote`); FR-9.5 — пункт о wiki без git-репозитория; версия, дата, строка истории.

## Изменяемые файлы

- `docs/PRD.md` — FR-5.2, FR-9.5, версия 0.35, история.
- `src/GitHubBackup.Core/Git/IGitClient.cs` — перегрузка `GetRemoteRefsAsync` с URL.
- `src/GitHubBackup.Infrastructure/Git/GitClient.cs` — её реализация.
- `src/GitHubBackup.Core/Changes/ChangeDetectionRequest.cs` — `CheckRemoteWithoutClone`.
- `src/GitHubBackup.Core/Changes/ChangeDetector.cs` — `PreviewAsync`.
- `src/GitHubBackup.Core/Changes/IChangeDetector.cs` — документация `PreviewAsync` (исключение `GitRepositoryNotFoundException` при отсутствии клона).
- `src/GitHubBackup.Core/Runs/BackupOrchestrator.cs` — флаг для wiki.

## Тесты

- `ChangeDetectorTests`:
  - preview без клона, флаг включён, сервер отвечает refs → `CloneNeeded`, вызвана перегрузка с URL;
  - preview без клона, флаг включён, перегрузка бросает `GitRepositoryNotFoundException` → исключение пробрасывается;
  - preview без клона, флаг выключен → `CloneNeeded`, git не вызывается (регрессия основного репозитория).
- `BackupOrchestratorTests` (сначала падающий тест, воспроизводящий баг):
  - dry run, wiki включена, клона нет, сервер wiki не находит → результата wiki нет, у репозитория свой результат;
  - dry run, wiki есть на сервере, клона нет → `WouldClone`;
  - dry run, основной репозиторий без клона → `WouldClone`, к серверу не обращается.
- `GitClient` integration (локальный bare-репозиторий во временной папке, без сети):
  - перегрузка читает ветки и теги bare-репозитория, когда папки клона и её родителя нет, и ничего не создаёт;
  - несуществующий путь → `GitRepositoryNotFoundException`;
  - токен не попадает в аргументы процесса (если в тестах `GitClient` такая проверка уже есть для `ls-remote` — повторить её для новой перегрузки).

## Вне рамок

- Основной репозиторий, который API возвращает, а git-сервер не находит: в dry run остаётся `WouldClone`.
- Обычный запуск не меняется.

## Решения

- Пользователь разрешил пройти все шаги без остановки на ревью плана; ревью — по PR.
- Проверка сервера без клона включается только для wiki (критерий issue про основной репозиторий).
- По security review (P1 IMPORTANT): рабочая папка `ls-remote` — ближайшая существующая папка-предок без `.git`, а `GIT_CEILING_DIRECTORIES` — её родитель. Так git не читает `.git/config` объемлющего репозитория (`url.insteadOf`, `http.proxy`, `http.sslVerify`) в процессе с токеном. Тест: `GetRemoteRefsAsync_UrlWithoutCloneInsideEnclosingRepository_IgnoresItsConfiguration`.

## Задачи

- [x] PRD 0.35: FR-5.2, FR-9.5, история
- [x] Падающий тест оркестратора воспроизводит баг
- [x] `IGitClient` / `GitClient`: перегрузка `GetRemoteRefsAsync` с URL и integration-тесты
- [x] `ChangeDetectionRequest.CheckRemoteWithoutClone`, `ChangeDetector.PreviewAsync` и unit-тесты
- [x] `BackupOrchestrator.BackupAsync` ставит флаг для wiki
- [x] Сборка, тесты, формат зелёные
