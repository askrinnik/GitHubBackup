# План: #42 «F4.7: FlaUI UI tests»

- Issue: https://github.com/askrinnik/GitHubBackup/issues/42
- Лейн: Feature (метка `type:test`, но в объём входят правки CI workflow)
- Сложность: L
- Дата: 2026-11-14

## 1. Цель
Проект `GitHubBackup.App.UITests` получает сквозные тесты на FlaUI для трёх сценариев PRD §11 (строка «UI end-to-end»): запуск приложения, диалог обнаружения и редактор настроек. Тесты запускаются только в отдельном workflow, который стартуют вручную (PRD §11, CI). В том же issue workflow `ui-smoke-tests.yml` и `ci.yml` приводятся к правилам `.claude/rules/github-actions.md`: actions закреплены по SHA, у job есть таймаут, явно задан shell `pwsh`, входной параметр передаётся через `env:`. Тесты не ходят в сеть, не трогают Credential Manager и реальный GitHub. API GitHub заменяет WireMock на loopback-адресе (PRD §7.2 разрешает `http` для loopback), токен берётся из `GitHub:Token` в тестовом `appsettings.json`.

## 2. Критерии приёмки
Из issue, дословно:
1. Tests pass locally and in the manual workflow
2. Covered by tests, CI green

Из комментария владельца issue:
3. Pin `actions/checkout`, `actions/setup-dotnet`, `actions/upload-artifact` in `.github/workflows/ui-smoke-tests.yml` to full commit SHAs with the version in a trailing comment (reuse the pins of ci.yml where they exist).
4. Add `timeout-minutes` to the job.
5. Set `defaults: run: shell: pwsh` explicitly.
6. Pass `inputs.category` to the test step through `env:` and quote it, instead of interpolating `${{ inputs.category }}` into `run:`.
7. Check `ci.yml` for the same gaps (and fix them).

Добавлено по проверке покрытия:
- (добавлено) Запуск с некорректным `appsettings.json` показывает диалог «Configuration error:», и процесс завершается с кодом `ExitCode.Critical` (2).
- (добавлено) Запуск без `backup-config.json` показывает `TreeLoadError`. Пустая конфигурация показывает `EmptyTreeMessage`.
- (добавлено) Если в диалоге обнаружения у добавляемого репозитория неверная папка архива, кнопка Apply неактивна.
- (добавлено) Редактор настроек: Cancel при несохранённых изменениях спрашивает подтверждение («Unsaved changes»). Ответ Yes возвращает к дереву и не меняет файл, ответ No оставляет редактор открытым.
- (добавлено) «Save tool paths» пишет `user-settings.json` в content root копии приложения. Так тест заодно проверяет, что тесты изолированы от папки сборки.
- (добавлено) `ci.yml`: `concurrency.group` приводится к виду из правила, `${{ github.workflow }}-${{ github.ref }}`.

## 3. Изменения требований
Не нужны. PRD §11 и §7.2 уже описывают всё, что нужно.

## 4. Затронутые проекты и типы

**`GitHubBackup.App` (production).** Изменения не ожидаются: нужные AutomationId уже есть (`MainWindow`, `RepositoryTree`, `EmptyTreeMessage`, `TreeLoadError`, `BackupAllButton`, `SettingsButton`, `RunSummary*`, `DiscoveryReviewWindow`, `DiscoveryTable`, `AddAllButton`/`SkipAllButton`/`ExcludeAllButton`, `ApplyDiscoveryButton`, `CancelDiscoveryButton`, `NewRepositoryPath`, поля и кнопки `SettingsView`). Ошибка валидации поля доступна через UIA `HelpText` (`FormTextBoxStyle` кладёт текст ошибки в `ToolTip`). Новый `AutomationProperties.AutomationId` добавляется, только если FlaUI не находит элемент.

**`GitHubBackup.TestUtilities`** (помощники переносятся из `Cli.Tests`, чтобы не копировать их):
- `GitHubApiStub`: переносится из `GitHubBackup.Cli.Tests`, становится `public`.
- `BackupConfigJson`: переносится так же, становится `public`.
- `BuildOutput` (новый, static): `CopyTo(sourceFolder, targetFolder, executableName)` — копия папки сборки без `logs`, `data`, `sources` и `backup-config.json` (логика из `CliInstallation.CopyBuildOutput`).
- `GitRemoteRedirect` (новый): пишет `redirect.gitconfig` с `insteadOf = https://github.com/` → локальная папка remotes, отдаёт `GIT_CONFIG_GLOBAL` и `GIT_CONFIG_NOSYSTEM`.
- csproj: `WireMock.Net` и `Microsoft.AspNetCore.WebUtilities`.

**`GitHubBackup.Cli.Tests`:** `CliInstallation` использует `BuildOutput` и `GitRemoteRedirect`; `GitHubApiStub.cs` и `BackupConfigJson.cs` удаляются, `using` обновляются.

**`GitHubBackup.App.UITests`:**
- csproj: ссылка на `GitHubBackup.TestUtilities`, `FlaUI.UIA3`, `AssemblyMetadata` `GitHubBackupAppOutputPath`. Условие `RunUITests` остаётся.
- Параллельный запуск отключён (один рабочий стол).
- `AppInstallation`: копия папки App во временной папке с тестовым `appsettings.json` (токен-заглушка, `ApiBaseUrl` = адрес WireMock), `backup-config.json`, `user-settings.json`, архивами и окружением (`DOTNET_ENVIRONMENT=Production`, редирект git).
- `AppSession`: запуск exe из копии через FlaUI, без унаследованных `GitHubBackup__*` и `GIT_*`; ожидания с таймаутом, поиск модальных окон, закрытие и код выхода; `Dispose` гарантирует завершение дерева процессов; скриншот при падении.
- `UiTimeouts`: именованные таймауты.
- `AppLaunchTests`, `DiscoveryDialogTests`, `SettingsEditorTests`.

**`src/Directory.Packages.props`:** `FlaUI.UIA3`.

**Workflows** и **`README.md`** (предусловия UI-тестов: git, 7-Zip, интерактивный рабочий стол; артефакт workflow).

## 5. Подход
1. Добавить `FlaUI.UIA3` в `Directory.Packages.props`, проверить уязвимости.
2. Перенести помощники в TestUtilities без изменения поведения, `Cli.Tests` остаются зелёными.
3. Изоляция без production-изменений: content root приложения — папка exe, поэтому тест копирует папку сборки App во временную папку и запускает exe оттуда (тот же приём, что у `CliInstallation`). Токен из `GitHub:Token`, поэтому Credential Manager и `gh` не читаются.
4. Диалог обнаружения: `GitHubApiStub` отдаёт новый доступный репозиторий, `discoverAccessibleRepositories: true`; нажатие `BackupAllButton` открывает `DiscoveryReviewWindow`. Нужны git в `PATH` и 7-Zip по умолчанию (на `windows-latest` есть). Для Add clone идёт в локальный bare-репозиторий через `LocalGitSandbox` и `GitRemoteRedirect`.
5. Взаимодействие через UIA patterns (`Invoke`, `ValuePattern`), без фокуса и мыши.
6. Ожидания только через `Retry` с таймаутами, без `Thread.Sleep`.
7. Скриншоты окон приложения при падении теста (не всего рабочего стола: на CI они попадают в артефакт).
8. Workflows:
   - `ui-smoke-tests.yml`: `defaults: run: shell: pwsh`, `timeout-minutes: 30`, SHA-пины трёх actions с `# vX.Y.Z`, `env: TEST_CATEGORY` и `--filter-trait "Category=$env:TEST_CATEGORY"`, скриншоты в артефакте.
   - `ci.yml`: те же SHA-пины, `defaults: run: shell: pwsh` (лишний `shell:` у шага хуков убирается), `timeout-minutes: 30`, `concurrency.group: ${{ github.workflow }}-${{ github.ref }}`.

## 6. Тесты
**`AppLaunchTests`:**
- `Launch_ValidConfiguration_ShowsMainWindowWithRepositoryTree`
- `Launch_ConfigurationWithoutRepositories_ShowsEmptyTreeMessage`
- `Launch_BackupConfigMissing_ShowsTreeLoadError`
- `Launch_InvalidAppSettings_ShowsConfigurationErrorAndExitsCritical`
- `Close_MainWindow_ExitsWithSuccess`

**`DiscoveryDialogTests`:**
- `BackupAll_NewAccessibleRepository_ShowsDiscoveryDialogWithRow`
- `Apply_ExcludeAll_WritesExclusionAndShowsSummary`
- `Apply_SkipAll_LeavesConfigUnchangedAndReportsSkipped`
- `Apply_AddAll_AddsRepositoryAndCreatesArchive`
- `Cancel_DiscoveryDialog_CancelsRunAndLeavesConfigUnchanged`
- `Apply_InvalidArchiveFolder_ApplyButtonDisabled`

**`SettingsEditorTests`:**
- `Save_EditedFields_WritesBackupConfigAndReturnsToTree`
- `EditField_EmptyBackupRoot_ShowsErrorAtField`
- `Save_InvalidAccountUrl_ShowsErrorAndWritesNothing`
- `Cancel_UnsavedChangesConfirmed_ReturnsToTreeWithoutWriting`
- `Cancel_UnsavedChangesDeclined_StaysInEditor`
- `SaveTools_AbsoluteGitPath_WritesUserSettingsInContentRoot`

Сохранённый `backup-config.json` сравнивается с ожидаемым JSON в raw string literal.

## 7. Проверка
| Пункт | Как проверить |
|---|---|
| 1 (локально) | `dotnet build src/GitHubBackup.slnx -p:RunUITests=true`, затем `dotnet test --solution src/GitHubBackup.slnx --no-build -p:RunUITests=true --filter-trait "Category=UI" --ignore-exit-code 8` |
| 1 (workflow) | `gh workflow run ui-smoke-tests.yml --ref <branch> -f category=UI` (и `Smoke`), результат `success` |
| 2 | Сборка, тесты и format из CLAUDE.md; CI на PR зелёный |
| 3 | Diff: 40-символьный SHA и `# vX.Y.Z`; успешный ручной запуск |
| 4, 5, 6 | Diff `ui-smoke-tests.yml`; в логе запуска с `category=UI` прошли UI-тесты |
| 7 | Diff `ci.yml`; CI на PR зелёный |
| Добавленные пункты | Тесты из §6 |

## 8. Вне объёма
- Кнопки Save token и Check token (Save пишет в реальный Credential Manager).
- Rename-, Missing- и Returned-строки диалога, закрытие диалога по Escape и крестиком (покрыто тестами ViewModel в `App.Tests`).
- Backup account и Backup repository, Dry run, прогресс и отмена идущего прогона, просмотр истории.
- Кэш NuGet и summary результатов в workflows.
- Перевод `App.Tests.AppTestSettings` и `CliApplicationTests.ValidAppSettings` на общий помощник.
- Smoke-тесты (#45).

## 9. Решения
Приняты на ревью плана (2026-11-14):
1. Actions закрепляются на последних релизах (не на v4); одинаковые SHA в `ci.yml` и `ui-smoke-tests.yml`.
2. `GitHubApiStub` и `BackupConfigJson` переносятся в `GitHubBackup.TestUtilities`; туда добавляется WireMock.Net.
3. Скриншот при падении UI-теста делается сейчас и загружается в артефакт workflow; по ревью безопасности снимаются только окна приложения.
4. Сценарий Add в диалоге обнаружения остаётся (нужны git и 7-Zip).

## 10. Задачи
- [x] Добавить `FlaUI.UIA3` в `src/Directory.Packages.props`, проверить уязвимости
- [x] Перенести `GitHubApiStub` и `BackupConfigJson` в `GitHubBackup.TestUtilities`, добавить WireMock.Net и WebUtilities
- [x] Выделить `BuildOutput.CopyTo` и `GitRemoteRedirect`, переключить `CliInstallation`, прогнать `Cli.Tests`
- [x] UITests csproj: TestUtilities, FlaUI.UIA3, `GitHubBackupAppOutputPath`; отключить параллельный запуск
- [x] `AppInstallation`, `AppSession` (со скриншотом при падении), `UiTimeouts`
- [x] `AppLaunchTests`, `DiscoveryDialogTests`, `SettingsEditorTests`
- [x] `ui-smoke-tests.yml` и `ci.yml` по правилам `github-actions.md`
- [x] README: предусловия UI-тестов и артефакт workflow
- [x] Сборка, тесты, format; UI-тесты локально
- [ ] Ручной запуск workflow на ветке PR; CI на PR зелёный
