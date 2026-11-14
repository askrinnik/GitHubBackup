# GitHubBackup developer guide

This guide covers building, testing, publishing and maintaining GitHubBackup. For installation and use, see the [README](../README.md). The requirements are in [PRD.md](PRD.md) (in Russian).

## Repository layout

```
docs/      documentation: PRD, implementation plans per issue, the AI harness description (docs/ai/), this guide
scripts/   publish.ps1: publishes the application folder
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

With `dotnet run`, pass the application arguments after `--`: `dotnet run --project src/GitHubBackup.Cli -- backup`.

The tests run on Microsoft.Testing.Platform (xUnit v3). Pass the solution or project with `--solution` or `--project`, not as a positional argument.

### Test categories

Every test assembly carries the xUnit trait `Category`: `Unit`, `Integration`, `Architecture` or `UI`. `src/Directory.Build.props` sets it from the project name.

The end-to-end tests in `GitHubBackup.Cli.Tests` also carry the category `EndToEnd`. They start the built `GitHubBackup.Cli.exe` as a separate process, with real git and 7-Zip on local bare repositories. WireMock on loopback replaces the GitHub API. These tests belong to the normal run and to CI.

The publishing tests (`PublishedApplicationTests`, also `EndToEnd`) check:

- the layout of the published folder;
- that its `appsettings.json` equals the source one;
- a full `backup --silent` run of the published `GitHubBackup.Cli.exe`, with the history database and the log.

They take the folder from the environment variable `PUBLISHED_APP_PATH`. Without it, they run `scripts/publish.ps1` into a temporary folder, which takes up to a minute.

The tests against the real GitHub will get the category `Smoke`. Select a set with a filter, for example `--filter-not-trait "Category=Smoke"`.

### UI tests

The UI tests (`GitHubBackup.App.UITests`) drive the running WPF application, so the normal solution run skips them. To run them, pass the property `RunUITests` to both the build and the tests:

```bash
dotnet build src/GitHubBackup.slnx -p:RunUITests=true
```

```bash
dotnet test --solution src/GitHubBackup.slnx --no-build -p:RunUITests=true --filter-trait "Category=UI" --ignore-exit-code 8
```

Exit code 8 means that no test ran in a project. With `--filter-trait`, every project except the selected one ends this way, so this command ignores code 8. The normal run and CI do not ignore it: a project in which no test ran fails the run.

The UI tests use FlaUI. Each test copies the build folder of `GitHubBackup.App` to a temporary folder and starts the application from there. If the variable `PUBLISHED_APP_PATH` is set, the test copies the published folder and starts `GitHubBackup.exe`; the UI test workflow does this. The token comes from `GitHub:Token` in the test `appsettings.json`, and WireMock on loopback replaces the GitHub API. The tests therefore do not read Windows Credential Manager and do not use the network. A run needs:

- an interactive desktop (the tests open windows; do not lock the screen and do not minimize the session);
- git in `PATH` and 7-Zip in `C:\Program Files\7-Zip\7z.exe` (the discovery dialog scenario uses them).

The tests run one at a time. A failing test saves screenshots of the application windows to the folder from the variable `UI_TEST_SCREENSHOTS`; without it, they go to `Screenshots` next to the test assembly.

## Publishing

The script `scripts/publish.ps1` (PowerShell 7) builds the application folder for `win-x64`:

```bash
pwsh -File scripts/publish.ps1
```

| File | Purpose |
|---|---|
| `GitHubBackup.exe` | WPF application |
| `GitHubBackup.Cli.exe` | console application |
| `appsettings.json` | settings shared by both exe files |

Both exe files are framework-dependent single-file builds: the machine needs only the .NET Desktop Runtime 10. The native SQLite library sits inside the exe and unpacks at startup. The application creates the folders `data\`, `logs\` and `sources\` next to the exe itself. `backup-config.json` is created only by the settings editor of `GitHubBackup.exe` (see [First run](../README.md#first-run) in the README).

| Parameter | Default | Purpose |
|---|---|---|
| `-OutputPath` | `artifacts\publish` in the repository root | output folder |
| `-Configuration` | `Release` | build configuration |
| `-Clean` | off | remove the previous publish result from the output folder |

Rules of the script:

- If the output folder exists, is not empty and `-Clean` is not passed, the script changes nothing and exits with code 1. This way it never overwrites a working folder with configuration, history and clones.
- `-Clean` removes only the files that publishing writes. If the folder holds other files or subfolders, the script also exits with code 1 and removes nothing.
- If the published `appsettings.json` sets `GitHub:Token`, the script deletes that file and exits with code 1: the token must not reach the artifact.
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
