# План: #29 F3.4 — проверочный прогон `--dry-run`

| | |
|---|---|
| Issue | [#29 F3.4: Dry run --dry-run](https://github.com/askrinnik/GitHubBackup/issues/29) |
| Полоса | Feature |
| Сложность | L |
| Дата | 2026-11-12 |

## 1. Цель

`backup --dry-run` выполняет обнаружение и быструю проверку (`git ls-remote`) и показывает, что сделал бы обычный запуск:
- клонирование (`WouldClone`);
- архив и его будущее имя (`WouldArchive`);
- `UpToDate`;
- новые, пропавшие и переименованные репозитории.

Прогон ничего не клонирует, не обновляет и не архивирует. Он не пишет `backup-config.json`, `.backup-state.json` и `.tmp` и не задаёт вопросов. Требования: FR-9.5, FR-9.8, §9 шаг 6, §8 (`IDiscoveryDecisionHandler` для dry-run), §11.

Что есть в коде сейчас:
- Опция `--dry-run` уже разбирается: `CliCommandLine` → `BackupCommandOptions.DryRun`.
- `BackupCommandHandler.RunAsync` останавливает такой запуск с `DryRunNotImplementedMessage` и кодом `2`. Это заглушка, её нужно убрать.

П-5 (запись в историю с флагом `DryRun`) в рамки не входит. SQLite-истории в коде ещё нет: есть только `HistoryOptions`, нет `DbContext` и нет записи запусков.

## 2. Критерии приёмки

Из issue:
- [ ] Тест: после dry-run файловая система не изменилась.
- [ ] Snapshot-тест вывода (по §11: эталон записан в тесте, Shouldly).
- [ ] Покрыто тестами, CI зелёный.

Добавлено из PRD и сверки с кодом:
- [ ] (добавлено) Вопросы не задаются. `IDiscoveryDecisionApplier.ApplyAsync` не вызывается, `ConsoleDiscoveryDecisionHandler` не используется.
- [ ] (добавлено) Новые, пропавшие, переименованные и вернувшиеся репозитории видны в выводе, но в `backup-config.json` не сохраняются. Папки архивов и клоны не перемещаются.
- [ ] (добавлено) У `WouldArchive` в колонке Archive стоит будущее имя архива (`ArchiveNameBuilder`).
- [ ] (добавлено) Нет клона → `WouldClone`, git не вызывается.
- [ ] (добавлено) Клон есть, refs и ветка совпадают с последним архивом и файл архива на месте → `UpToDate`. Иначе → `WouldArchive`. Пустой репозиторий на сервере → `UpToDate`.
- [ ] (добавлено) Сбой `ls-remote` → `Failed`, остальные репозитории обрабатываются, код `1`.
- [ ] (добавлено) Папка `.tmp` при dry-run не очищается (FR-7.4 удаляет файлы).
- [ ] (добавлено) `--dry-run` работает вместе с `--silent`, `--account`, `--repo`. С `--repo` обнаружения нет, как в обычном запуске.
- [ ] (добавлено) При перенаправленном вводе dry-run не останавливается с кодом `2`: вопросов нет, FR-9.6 не применяется.
- [ ] (добавлено) Wiki активного репозитория показывается так же: `WouldClone`, `WouldArchive` или `UpToDate`.
- [ ] (добавлено) Строка итогов dry-run включает `WouldClone` и `WouldArchive`.
- [ ] (добавлено) Команда `validate` не меняется.

## 3. Изменения PRD

Обязательных изменений нет. Предлагается одно уточнение (версия и строка истории — по `.claude/rules/docs.md`):
- §8, строка `IDiscoveryDecisionHandler`. Сейчас: «dry-run (ничего не решает)». Предлагается: «в dry-run решения не принимаются: найденное обнаружением только показывается, `IDiscoveryDecisionApplier` не вызывается».
- Причина: applier всегда пишет конфиг и переносит папки. Поэтому «пустой» обработчик невозможен, и dry-run обходит applier целиком.

## 4. Затрагиваемые проекты и типы

**GitHubBackup.Core**
- `Runs/BackupItemStatus` — новые значения `WouldArchive` и `WouldClone`.
- `Changes/ChangeDecision` — новое значение `CloneNeeded`. Его возвращает только предпросмотр.
- `Changes/IChangeDetector` и `ChangeDetector` — новый метод `PreviewAsync(ChangeDetectionRequest, CancellationToken)`. Он решает без clone и fetch, только по `GetRemoteRefsAsync` и `GetDefaultBranchAsync`.
- `Changes/ChangeDetectorLog` — запись о решении предпросмотра (можно переиспользовать `Decided`).
- `Runs/BackupRunRequest` — свойство `bool DryRun { get; init; }`. Позиционный конструктор не меняется.
- `Runs/BackupRunResult`:
  - `bool IsDryRun { get; init; }`;
  - `DiscoveryResult? PlannedDiscovery { get; init; }` — найденное обнаружением, но не применённое;
  - `ExitCode` учитывает `PlannedDiscovery`: New, Missing и Renamed дают `3`, Returned не даёт — так же, как сейчас с `Discovery`.
- `Runs/BackupOrchestrator` — ветка dry-run (раздел 5).
- `Runs/BackupOrchestratorLog` — записи «dry run started», «temporary folder left untouched», «repository missing, would be marked Unavailable».

**GitHubBackup.Cli**
- `BackupCommandHandler`:
  - убрать заглушку и `DryRunNotImplementedMessage`;
  - для dry-run передавать `new BackupRunRequest(filter, automaticHandler) { DryRun = true }`; обработчик не вызывается;
  - для dry-run пропускать проверку перенаправленного ввода.
- `BackupSummaryRenderer`:
  - перед таблицей строка `Dry run: nothing was cloned, updated, archived or saved.`;
  - блок обнаружения: `New: owner/name`, `Missing: owner/name`, `Renamed: old -> new`, `Returned: owner/name`;
  - итоги dry-run: `WouldClone, WouldArchive, UpToDate, Warning, Failed, Skipped`;
  - стили для `WouldArchive` и `WouldClone`.

**Документация**
- `README.md`: статус и таблица команд — описать работающий `--dry-run`.

**GitHubBackup.App** не затрагивается: он не использует оркестратор.

## 5. Подход

1. **Core, `ChangeDetector.PreviewAsync`.** Те же правила, что у `DetectAsync`, но без записи на диск:
   - нет `.git` в клоне → `CloneNeeded`, пустой snapshot, git не вызывается;
   - иначе `remote = GetRemoteRefsAsync`; пустой remote → `NothingToArchive`;
   - ветка: запрошенная, если она есть в remote; иначе `GetDefaultBranchAsync` с флагом `BranchFallback`; нет ни той, ни другой → `GitBranchNotFoundException`, как в обычном запуске;
   - затем порядок `Classify`: нет архива → нет файла → refs изменились → ветка изменилась → `UpToDate`;
   - `Refs` = remote, поэтому оркестратор берёт коммит HEAD ветки из remote.
   - Почему так: правила решения живут в `ChangeDetector`, оркестратор их не дублирует. `ls-remote origin` и `ls-remote --symref` не меняют клон.
2. **Core, `BackupOrchestrator.RunAsync` при `request.DryRun`:**
   - `EmptyTemporaryFolder` не вызывается, в лог идёт запись;
   - обнаружение выполняется, `decisionApplier.ApplyAsync` не вызывается, результат идёт в `PlannedDiscovery`;
   - элементы строятся из конфигурации как есть. Missing → `Skipped` с сообщением «missing on GitHub; a backup run marks it Unavailable». Returned обрабатываются как активные. Renamed обрабатываются под старым именем: GitHub перенаправляет `ls-remote` старого `origin`;
   - `BackupAsync` в dry-run зовёт `PreviewAsync` вместо `DetectAsync`. `CloneNeeded` → `WouldClone`. `ArchiveNeeded` → `WouldArchive` с именем из `nameBuilder.Build`; `archiver` и `stateStore.AppendAsync` не вызываются. Иначе → `UpToDate`;
   - `BranchFallback` → `Warning`, как в обычном запуске; будущее имя архива остаётся в колонке Archive;
   - wiki: `IsWikiEnabledAsync` (только чтение), потом тот же предпросмотр;
   - результат: `IsDryRun = true`, `Discovery = null`, `PlannedDiscovery = discovery`.
   - Почему так: отдельный флаг запроса, а не отдельный оркестратор. Загрузка конфига, инструменты, токен, фильтр и исключения общие.
3. **Cli** — обработчик и рендерер по разделу 4, затем README.

## 6. Тесты

**`GitHubBackup.Core.Tests/Changes/ChangeDetectorTests`** (NSubstitute `IGitClient`, `MockFileSystem`):
- `PreviewAsync_NoClone_ReturnsCloneNeededWithoutGit`;
- `PreviewAsync_RefsUnchanged_ReturnsUpToDate`;
- `PreviewAsync_RefsChanged_ReturnsArchiveNeeded`;
- `PreviewAsync_ArchiveFileMissing_ReturnsArchiveNeeded`;
- `PreviewAsync_NoLastArchive_ReturnsArchiveNeeded`;
- `PreviewAsync_BranchChanged_ReturnsArchiveNeeded`;
- `PreviewAsync_EmptyRemote_ReturnsNothingToArchive`;
- `PreviewAsync_BranchMissing_FallsBackToDefault`;
- `PreviewAsync_NoBranchAtAll_ThrowsGitBranchNotFound`;
- во всех тестах `CloneAsync` и `UpdateAsync` не вызывались.

**`GitHubBackup.Core.Tests/Runs/BackupOrchestratorTests`:**
- dry-run не вызывает `ApplyAsync`, `DetectAsync`, `IArchiver`, `AppendAsync`, `SaveAsync`;
- файл в `.tmp` остаётся на месте;
- Missing → `Skipped`, Returned обрабатывается;
- `PlannedDiscovery` заполнен;
- `ArchiveNeeded` → `WouldArchive` с именем из `FakeTimeProvider`;
- `CloneNeeded` → `WouldClone`;
- сбой предпросмотра → `Failed`, следующий репозиторий обрабатывается;
- фильтр `--repo` → обнаружение не вызывается.

**`GitHubBackup.Core.Tests/Runs/BackupRunResultTests`:**
- `ExitCode`: `PlannedDiscovery` с New, Missing или Renamed → `3`; только Returned → `0`; `Failed` важнее и даёт `1`.

**`GitHubBackup.Infrastructure.IntegrationTests/Runs/BackupOrchestratorTests`** (реальный git, `LocalGitSandbox`, `FakeGitHubApi`):
- `RunAsync_DryRun_ChangesNothingOnDisk` — главный приёмочный тест:
  - сценарий: репозиторий A уже заархивирован, затем в remote новый коммит; репозиторий B без клона; новый репозиторий аккаунта; пропавший репозиторий; файл в `.tmp`;
  - снимок дерева sandbox до и после (путь, размер, `LastWriteTimeUtc`, байты `backup-config.json` и `.backup-state.json`) — снимки равны;
  - статусы: `WouldArchive` с ожидаемым именем, `WouldClone`, `Skipped`; код `DiscoveryEvents`.
- `RunAsync_DryRunUnchanged_ReportsUpToDate`.
- `RunAsync_DryRunWiki_ReportsWikiStatus`.

**`GitHubBackup.Cli.Tests/BackupSummaryRendererTests`** (`TestConsole`, эталон — raw string в тесте):
- `RenderAsync_DryRun_WritesHeaderTableDiscoveryAndTotals` — snapshot всего вывода;
- `RenderAsync_DryRunNoRepositories_…`.

**`GitHubBackup.Cli.Tests/BackupCommandHandlerTests`:**
- заменить `RunAsync_DryRun_ReturnsCriticalWithoutRunningOrchestrator` на `RunAsync_DryRun_RunsOrchestratorWithDryRunRequest`;
- заменить `RunAsync_DryRunWithRedirectedInput_…` на `RunAsync_DryRunWithRedirectedInput_RunsOrchestrator`;
- `RunAsync_DryRunSilent_RunsDryRun`, `RunAsync_DryRunWithFilter_PassesFilter`, `RunAsync_DryRunDiscovery_ReturnsDiscoveryEvents`.

**`GitHubBackup.Cli.Tests/CliApplicationTests`:**
- заменить `RunAsync_BackupDryRun_ReturnsCriticalAndChangesNothing` на `RunAsync_BackupDryRunWithoutBackupConfig_ReturnsCriticalAndCreatesNoFiles`.

## 7. Проверка

| Критерий | Чем проверяется |
|---|---|
| ФС не изменилась | `BackupOrchestratorTests.RunAsync_DryRun_ChangesNothingOnDisk` (интеграционный), Core-тест про `.tmp` |
| Snapshot вывода | `BackupSummaryRendererTests.RenderAsync_DryRun_WritesHeaderTableDiscoveryAndTotals` |
| Нет вопросов, нет записи конфига | Core `BackupOrchestratorTests`, `BackupCommandHandlerTests` |
| Имя будущего архива | Core и интеграционный тест |
| CI зелёный | сборка, тесты без Smoke, `dotnet format --verify-no-changes`; CI pull request |

## 8. Вне рамок

- **П-5, история с флагом `DryRun`.** Истории в коде ещё нет; флаг появится в задаче про историю.
- **Блокировка запуска (NFR-SYS-1).** В коде её нет; решение для dry-run — в issue блокировки.
- **Имя архива для `WouldClone`.** Без клона `ls-remote origin` невозможен; нужен `ls-remote` по URL — отдельная работа.
- **Wiki без клона, которую никто не редактировал.** Dry-run покажет `WouldClone`, хотя обычный запуск её пропустит. Причина та же.
- **Имена переименованных репозиториев.** Dry-run показывает будущее имя по старому `owner/name`.
- **Блок обнаружения в сводке обычного запуска.**
- **UI-переключатель «Dry run»** (App).

## 9. Открытые вопросы

1. Уточнять ли §8 PRD (раздел 3). Рекомендация: да.
2. Код возврата при New, Missing или Renamed в dry-run. Рекомендация: `3`, как в обычном запуске.
3. `.tmp` в dry-run. Рекомендация: не очищать.
4. `BranchFallback` в dry-run. Рекомендация: `Warning` с будущим именем в колонке Archive.
5. Новые репозитории в dry-run. Рекомендация: только в блоке New, не строками таблицы.
6. `--silent` вместе с `--dry-run`. Рекомендация: вывод одинаковый.
7. Missing и Returned в таблице. Рекомендация: Missing → `Skipped` с пояснением; Returned — как активный.

## Решения

Ревью 2026-11-12: приняты все рекомендации раздела 9.

1. PRD 0.19: §8 уточнён (`IDiscoveryDecisionApplier` в dry-run не вызывается); FR-9.5 — `.tmp` не очищается, код возврата как у обычного запуска; FR-9.6 — dry-run не требует `--silent`.
2. Код `3` при New, Missing или Renamed в dry-run.
3. `.tmp` в dry-run не очищается.
4. `BranchFallback` → `Warning` с будущим именем архива.
5. Новые репозитории — только в блоке New.
6. `--silent` с `--dry-run` вывод не меняет.
7. Missing → `Skipped` с пояснением; Returned — как активный.

## Задачи

- [x] Уточнить §8, FR-9.5 и FR-9.6 PRD, поднять версию до 0.19, добавить строку в историю.
- [x] Core: `BackupItemStatus.WouldArchive` и `WouldClone`; `ChangeDecision.CloneNeeded`.
- [x] Core: `IChangeDetector.PreviewAsync` и реализация в `ChangeDetector`.
- [x] Core: `BackupRunRequest.DryRun`; `BackupRunResult.IsDryRun`, `PlannedDiscovery`; `ExitCode`.
- [x] Core: ветка dry-run в `BackupOrchestrator`; записи в `BackupOrchestratorLog`.
- [x] Core.Tests: `ChangeDetectorTests`, `BackupOrchestratorTests`, `BackupRunResultTests`.
- [x] IntegrationTests: `RunAsync_DryRun_ChangesNothingOnDisk` и сопутствующие.
- [x] Cli: `BackupCommandHandler` — убрать заглушку, передать `DryRun`, пропускать FR-9.6.
- [x] Cli: `BackupSummaryRenderer` — строка dry-run, блок обнаружения, итоги, стили.
- [x] Cli.Tests: `BackupSummaryRendererTests`, `BackupCommandHandlerTests`, `CliApplicationTests`.
- [x] README.
- [x] Сборка, тесты, формат; проверка комментариев.
- [x] По итогам security-ревью: `ChangeDetector.PreviewAsync` отклоняет запрошенную ветку и ветку по умолчанию, которые начинаются с `-`, как это делает `GitClient` в обычном запуске.
