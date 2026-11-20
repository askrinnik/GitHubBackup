# GitHubBackup developer guide

This guide covers building, testing, publishing and maintaining GitHubBackup. For installation and use, see the [README](../README.md). The requirements are in [PRD.md](PRD.md) (in Russian).

## Repository layout

```
assets/    icon/: the application icon, its vector source (GitHubBackup.Icon.xaml) and the rendered GitHubBackup.ico
docs/      documentation: PRD, implementation plans per issue, the AI harness description (docs/ai/), this guide
scripts/   publish.ps1: publishes the application folder; render-icon.ps1: renders the application icon;
           test-bench.ps1: starts a temporary copy of the WPF application against a stubbed GitHub API
src/       the GitHubBackup.slnx solution: projects and paired test projects
.claude/ .ai/   AI harness for Claude Code (see docs/ai/README.md)
```

## Build, run and test

You need the .NET SDK 10.0.100 or a newer feature band of the same major version (`global.json` in the repository root). Run the commands from the repository root:

| Action | Command |
|---|---|
| Build | `dotnet build src/GitHubBackup.slnx` |
| Test | `dotnet test --solution src/GitHubBackup.slnx` |
| Format check | `dotnet format src/GitHubBackup.slnx --verify-no-changes` |
| Run the console application | `dotnet run --project src/GitHubBackup.Cli` |
| Run the WPF application | `dotnet run --project src/GitHubBackup.App` |
| Publish the application folder | `pwsh -File scripts/publish.ps1` |
| Render the application icon | `pwsh -File scripts/render-icon.ps1` |
| Start the test bench | `pwsh -File scripts/test-bench.ps1 -Scenario missing-repository` |

With `dotnet run`, pass the application arguments after `--`: `dotnet run --project src/GitHubBackup.Cli -- backup`.

The tests run on Microsoft.Testing.Platform (xUnit v3). Pass the solution or project with `--solution` or `--project`, not as a positional argument.

### Test categories

Every test assembly carries the xUnit trait `Category`: `Unit`, `Integration`, `Architecture` or `UI`. `src/Directory.Build.props` sets it from the project name.

The end-to-end tests in `GitHubBackup.Cli.Tests` also carry the category `EndToEnd`. They start the built `GitHubBackup.Cli.exe` as a separate process, with real git and 7-Zip on local bare repositories. WireMock on loopback replaces the GitHub API. These tests belong to the normal run and to CI.

The publishing tests (`PublishedApplicationTests`, also `EndToEnd`) check:

- the layout of the published folder: only the two exe files;
- that `-Clean` refuses a folder that holds `appsettings.json`;
- a full `backup --silent` run of the published `GitHubBackup.Cli.exe`, with the history database and the log.

They take the folder from the environment variable `PUBLISHED_APP_PATH`. Without it, they run `scripts/publish.ps1` into a temporary folder, which takes up to a minute.

The tests against the real GitHub will get the category `Smoke`. Select a set with a filter, for example `--filter-not-trait "Category=Smoke"`.

### OTLP headers credential in tests

Both executables read `OpenTelemetry:Headers` from the Windows Credential Manager credential `GitHubBackup:OpenTelemetry:Headers`, and the settings editor and `set-otlp-headers` write it. No test may read or write that credential of the developer:

- A host built in the test process takes its target from `GitHubBackupHostSettings.OtlpHeadersCredentialTarget`. Without a target (the default of `TemporaryContentRoot` and of settings created in a test), the host has no Credential Manager source, and its header store saves nothing. A test that needs the real Credential Manager creates an `OtlpHeadersTestCredential` (`GitHubBackup.TestUtilities`), which names a unique target and deletes the credential on dispose.
- An executable started by a test (UI tests, CLI end-to-end tests, the test bench) gets the environment variable `GITHUBBACKUP_OTLP_HEADERS_CREDENTIAL_TARGET` with a unique target. `GitHubBackupHostSettings.ForCurrentProcess` uses that target in place of the default one, but only when it starts with `GitHubBackup.Tests:`; any other value is ignored, so the variable cannot point the headers at another credential, such as the GitHub token. The name lies outside the `GitHubBackup__` prefix, so the variable never becomes a configuration key. `IsolatedAppProcess` and `CliProcess` set it on every start; `AppInstallation` and the test bench own an `OtlpHeadersTestCredential` and delete it when they are disposed or stop.

The variable is a test aid only; the application does not document it for users.

### UI tests

The UI tests (`GitHubBackup.App.UITests`) drive the running WPF application, so the normal solution run skips them. To run them, pass the property `RunUITests` to both the build and the tests:

```bash
dotnet build src/GitHubBackup.slnx -p:RunUITests=true
```

```bash
dotnet test --solution src/GitHubBackup.slnx --no-build -p:RunUITests=true --filter-trait "Category=UI" --ignore-exit-code 8
```

Exit code 8 means that no test ran in a project. With `--filter-trait`, every project except the selected one ends this way, so this command ignores code 8. The normal run and CI do not ignore it: a project in which no test ran fails the run.

The UI tests use FlaUI. Each test copies the build folder of `GitHubBackup.App` to a temporary folder and starts the application from there. If the variable `PUBLISHED_APP_PATH` is set, the test copies the published folder and starts `GitHubBackup.exe`; the UI test workflow does this. The token comes from `GitHub:Token` in the `appsettings.json` the test writes, or from the variable `GitHubBackup__GitHub__Token` when a test starts without the file, and WireMock on loopback replaces the GitHub API. The tests therefore do not read the token from Windows Credential Manager and do not use the network; the OTLP headers go to a credential of the test (see [OTLP headers credential in tests](#otlp-headers-credential-in-tests)). A run needs:

- an interactive desktop (the tests open windows; do not lock the screen and do not minimize the session);
- git in `PATH` and 7-Zip in `C:\Program Files\7-Zip\7z.exe` (the discovery dialog scenario uses them).

The tests run one at a time. A failing test saves screenshots of the application windows to the folder from the variable `UI_TEST_SCREENSHOTS`; without it, they go to `Screenshots` next to the test assembly.

#### Screenshot tests

`MainWindowScreenshotTests`, `DiscoveryDialogScreenshotTests` and `SettingsEditorScreenshotTests` capture a window in a given state, in the light and the dark theme, and measure the capture. There are no reference images: each check measures one capture.

| Check | What it measures |
|---|---|
| One baseline per row | The top of the text of every cell of a table row lies on the same pixel row, within 2 device-independent pixels. A cell with two lines of text is left out. |
| Nothing is cut | The text of every cell, of the column header and of the totals card keeps at least 1 pixel from the top and the bottom of its region. At the smallest results panel height (80), the column header and one whole table row are visible. |
| Borders stand out | The borders of the four cards and the line above the result table differ from the background on both sides by a calibrated contrast. |
| Selection stays readable | The text of a selected row of the discovery dialog keeps a calibrated contrast to the selection. |

The states are a dry run with one row, a dry run with many rows and discovery lines, the smallest results panel height, the discovery dialog with a New, a Renamed, a Missing and a Returned row, and the repository tree of `paused-entries`, whose status icons line up in their two places and whose paused entries are muted. The scenarios come from `UiScenarios` in `GitHubBackup.TestUtilities`; the test bench starts the same scenarios by name.

The contrast limits are half of the lowest value measured on the current layout in both themes. They and their measured values are in `ScreenshotAssert`. `LayoutMeasurementTests` checks the measurements on synthetic images and needs no desktop.

Windows does not render windows while the workstation is locked, the session is minimized or disconnected. A capture is then one color, and the test fails with a message that says so. Run the screenshot tests on an unlocked interactive desktop:

```bash
dotnet test --project src/GitHubBackup.App.UITests/GitHubBackup.App.UITests.csproj --no-build -p:RunUITests=true --filter-class "*ScreenshotTests"
```

### Test bench

`scripts/test-bench.ps1` starts a temporary copy of the WPF application with one command, to check a window by hand or with the `netwright` MCP server:

```bash
pwsh -File scripts/test-bench.ps1 -Scenario missing-repository
```

The script builds and runs `GitHubBackup.TestBench`. The bench:

1. copies the build folder of `GitHubBackup.App` to a new folder under `%TEMP%\GitHubBackup.Tests`;
2. starts a WireMock stub of the GitHub API on loopback with the scenario;
3. writes `appsettings.json` with the stub address and a test token that grants nothing, `backup-config.json` of the scenario and, when asked, `ui-settings.json`;
4. starts the application from that folder in the `Production` environment, without inherited `GitHubBackup__*` and `GIT_*` variables, with git redirected from `https://github.com/` to an empty local folder and with an OTLP headers credential of its own (see [OTLP headers credential in tests](#otlp-headers-credential-in-tests));
5. stops the application and the stub and deletes the folder when the application exits, Enter is pressed or Ctrl+C is pressed.

The real installation, its settings, the credentials of the user in Windows Credential Manager and the network are never used.

| Parameter | Effect |
|---|---|
| `-Scenario` | `missing-repository` (default), `renamed-repository`, `new-repositories`, `discovery-review`, `many-rows` or `paused-entries`; `-List` describes them |
| `-Theme` | `light`, `dark` or `system`; by default the application follows Windows |
| `-SummaryPanelHeight` | the saved height of the results panel, such as the minimum 80 |
| `-NoLaunch` | prepares the folder and the stub and prints the path of the executable and the git variables without starting the application |
| `-Keep` | leaves the folder in place when the bench stops |
| `-NoBuild`, `-Configuration` | skip the build or choose the configuration (default `Debug`) |

## Application icon

Both exe files, the windows of the WPF application and its taskbar button use one icon, `assets/icon/GitHubBackup.ico`. Its source is the WPF `DrawingGroup` in `assets/icon/GitHubBackup.Icon.xaml`, drawn on a 256 x 256 canvas. After you change the source, render the icon and commit both files:

```bash
pwsh -File scripts/render-icon.ps1
```

The script (PowerShell 7 on Windows) renders the drawing with WPF into PNG frames of 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixels and writes them into the ICO file. It downloads nothing. The build uses the committed ICO and never runs the script, also in CI.

Loading XAML can create arbitrary objects and run code, so render only a source you have reviewed. The script refuses a source that contains `ObjectDataProvider`, `x:Code` or `clr-namespace:System.Diagnostics`.

## Publishing

The script `scripts/publish.ps1` (PowerShell 7) builds the application folder for `win-x64`:

```bash
pwsh -File scripts/publish.ps1
```

| File | Purpose |
|---|---|
| `GitHubBackup.exe` | WPF application |
| `GitHubBackup.Cli.exe` | console application |

Both exe files are framework-dependent single-file builds: the machine needs only the .NET Desktop Runtime 10. The native SQLite library sits inside the exe and unpacks at startup. No settings file is published: every setting has a default in code, and `appsettings.json` is optional (see [appsettings.json](../README.md#appsettingsjson) in the README). The user or the settings editor creates `appsettings.json`, the desktop app creates `ui-settings.json`, and the application creates the folders `data\`, `logs\` and `sources\` next to the exe itself. Updating an installed copy means replacing the two exe files only. `backup-config.json` is created only by the settings editor of `GitHubBackup.exe` (see [First run](../README.md#first-run) in the README).

| Parameter | Default | Purpose |
|---|---|---|
| `-OutputPath` | `artifacts\publish` in the repository root | output folder |
| `-Configuration` | `Release` | build configuration |
| `-Clean` | off | remove the previous publish result from the output folder |

Rules of the script:

- If the output folder exists, is not empty and `-Clean` is not passed, the script changes nothing and exits with code 1. This way it never overwrites a working folder with configuration, history and clones.
- `-Clean` removes only the files that publishing writes: the exe files. If the folder holds other files or subfolders, including `appsettings.json` or `ui-settings.json`, the script also exits with code 1 and removes nothing.
- If `dotnet publish` fails, the exit code is also 1.
- The script builds the projects in a temporary folder and does not touch `bin` and `obj` of the normal build.

## Run history migrations

The run history sits in the SQLite database `data\history.db` next to the exe. At startup, the host creates the database and applies the missing EF Core migrations. The migrations live in `src/GitHubBackup.Infrastructure/History/Migrations`. After you change the history model, add a migration with the `dotnet-ef` tool from the local manifest `.config/dotnet-tools.json`:

```bash
dotnet tool restore
```

```bash
dotnet ef migrations add <Name> --project src/GitHubBackup.Infrastructure --output-dir History/Migrations
```

The test `HistoryDbContextTests` fails if the model changed and no migration was added.

## Faster integration tests (optional)

The integration tests create temporary repositories in the folder `%TEMP%\GitHubBackup.Tests`. The class `TemporaryFolder` in `GitHubBackup.TestUtilities` sets the path. Microsoft Defender scans every file that git writes there, so the tests run noticeably longer locally.

A developer can exclude this folder from scanning. Start PowerShell as administrator under the same user that runs the tests: another account has another `TEMP`.

```powershell
Add-MpPreference -ExclusionPath (Join-Path $env:TEMP "GitHubBackup.Tests")
```

Check the exclusion:

```powershell
(Get-MpPreference).ExclusionPath
```

Remove the exclusion:

```powershell
Remove-MpPreference -ExclusionPath (Join-Path $env:TEMP "GitHubBackup.Tests")
```

Exclude only this folder. Do not exclude the process `git.exe`: Defender would then stop scanning all files that git touches, including real clones.

A variant that keeps more protection is a Dev Drive (Defender performance mode) that holds the repository and the `TEMP`/`TMP` folders.

The exclusion is optional and does not affect CI.

## CI

| Workflow | Trigger | What it does |
|---|---|---|
| `.github/workflows/ci.yml` | pull request and push to `main` | `windows-latest`: restore, build, format check, the check cases of the Claude Code hooks (`.claude/hooks/tests/*.Cases.ps1`), publish with `scripts/publish.ps1`, tests without the categories `UI` and `Smoke` (the publishing tests check the published folder); the results (`*.trx`) go to the artifact `test-results`, the application folder to the artifact `GitHubBackup` |
| `.github/workflows/ui-smoke-tests.yml` | manual (`workflow_dispatch`) | restore, build, publish with `scripts/publish.ps1`, tests of the selected category (`UI` or `Smoke`; the UI tests start the published `GitHubBackup.exe`); the artifact `test-results-<category>` holds the results (`*.trx`) and the screenshots of the application windows from failed UI tests (`Screenshots/*.png`) |

Compiler and analyzer warnings are errors (`TreatWarningsAsErrors`). NuGet package versions are set only in `src/Directory.Packages.props`; shared build settings are in `src/Directory.Build.props`.

## Issue and pull request templates

| File | Purpose |
|---|---|
| `.github/ISSUE_TEMPLATE/task.yml` | task: required fields "requirements source" (PRD sections, requirement ids), "task", "acceptance criteria", "dependencies" |
| `.github/ISSUE_TEMPLATE/bug.yml` | bug: the same requirements source fields, steps to reproduce, expected and actual behaviour, environment |
| `.github/ISSUE_TEMPLATE/config.yml` | disables blank issues, adds a link to `docs/PRD.md` |
| `.github/pull_request_template.md` | fills the description of every pull request: `Closes #<issue>` and a checklist (PRD requirements met, tests added, PRD updated when requirements changed, documentation updated) |

## Language conventions

- Code, comments, logs, and application messages are in English.
- Issue titles, commits, pull requests and their comments are in English.
- The program documentation (`README.md` and this guide) is in English.
- The PRD, the plans (`docs/plans/`), the AI harness documentation (`docs/ai/`) and the issue bodies are in Russian (NFR-7).
