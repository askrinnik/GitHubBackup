# GitHubBackup

GitHubBackup keeps local backups of your GitHub repositories on Windows 11. It clones every repository you choose, with all branches and tags, and writes a new ZIP archive each time something changes.

- It backs up all repositories of the accounts you list (users and organizations): public and private, forks and archived ones. You can also add single repositories by URL.
- It detects new, missing and renamed repositories and asks you to confirm the changes before it writes them to the settings.
- It supports exclusion masks (`test-*`, `someone/*`).
- It clones with all branches and tags and submodules, and it pulls Git LFS content for the selected branch. A wiki gets its own archive.
- It creates a new ZIP archive (with 7-Zip, including `.git`) only when any branch or tag changed.
- Clones stay on a local disk, apart from the archives. You can keep the archives in OneDrive or another cloud storage.
- It records the history of runs and archives.
- It has two hosts that share one configuration: a WPF desktop app (`GitHubBackup.exe`) and a console app (`GitHubBackup.Cli.exe`) for scripts and the Task Scheduler.

## Contents

- [Requirements](#requirements)
- [Installation](#installation)
- [GitHub token](#github-token)
- [Configuration](#configuration)
- [OneDrive scenario](#onedrive-scenario)
- [First run](#first-run)
- [Command-line reference](#command-line-reference)
- [Scheduled backups (Task Scheduler)](#scheduled-backups-task-scheduler)
- [Desktop app](#desktop-app)
- [Logs, history and telemetry](#logs-history-and-telemetry)
- [Development](#development)

## Requirements

| Component | Version |
|---|---|
| Windows | 11 |
| .NET | .NET Desktop Runtime 10 (the .NET SDK 10 is needed only to build from source) |
| Git | 2.4x or newer, in `PATH` or set by a path in the settings |
| Git LFS | 3.x |
| 7-Zip | `C:\Program Files\7-Zip\7z.exe` or set by a path in the settings |
| GitHub token | classic personal access token (PAT) with the scopes `repo` and `read:org` (see [GitHub token](#github-token)) |

## Installation

GitHubBackup has no installer. You use a published application folder.

1. Get the folder in one of two ways:
   - download the artifact `GitHubBackup` of a run of the CI workflow `ci.yml` on GitHub and unzip it;
   - build it yourself from the repository root: `pwsh -File scripts/publish.ps1`. The folder is `artifacts\publish`; `-OutputPath <folder>` selects another one (see the [developer guide](docs/development.md#publishing)).
2. Copy the folder to a place where you can write, for example `C:\Tools\GitHubBackup`. Do not use `C:\Program Files`: the application writes its settings, logs, history and (by default) clones next to the exe files. The folder must be writable unless you set `sourcesRoot` to another folder (see [Configuration](#backup-configjson)).
3. Install the .NET Desktop Runtime 10, git, Git LFS and 7-Zip if they are missing.

The folder holds two programs that use the same files:

```
<application folder>\
├── GitHubBackup.exe          desktop app
├── GitHubBackup.Cli.exe      console app
├── appsettings.json          application settings (read only)
├── user-settings.json        tool paths saved by the settings editor (optional)
├── backup-config.json        what to back up and where (you create it, see "First run")
├── backup-config.json.bak    previous version, written on every save
├── logs\                     text and JSON logs, one file per day
├── data\history.db           run history
└── sources\                  clones (default location)
```

The publish step creates only the two exe files and `appsettings.json`. The application creates `logs\`, `data\` and `sources\` itself. Do not put the application folder into OneDrive unless you set `sourcesRoot` to a local folder: the clones would then sync to the cloud (see [OneDrive scenario](#onedrive-scenario)).

Both exe files are framework-dependent single-file builds. The native SQLite library sits inside the exe and unpacks at startup.

## GitHub token

GitHubBackup reads your repositories through the GitHub API and clones them with git. For private repositories and for organizations, it needs a token.

### Create the token

1. Open GitHub, then **Settings**, **Developer settings**, **Personal access tokens**, **Tokens (classic)**.
2. Select **Generate new token (classic)**.
3. Select the scopes `repo` and `read:org`.
4. Set an expiration date you can live with. When the token expires, create a new one and save it again.
5. Copy the token. GitHub shows it only once.
6. If an organization uses SAML single sign-on, open the token in the list and select **Configure SSO**, then **Authorize** for each such organization. Without this, GitHub hides the organization's repositories.

### Why not a fine-grained token

Use a classic token. A fine-grained token often cannot reach what a backup needs:

- A fine-grained token belongs to one resource owner: your user or one organization. One token cannot cover your own repositories and those of an organization together.
- An organization can require an owner to approve each fine-grained token. Until an owner approves it, the token reads only public resources of the organization.
- A fine-grained token cannot reach repositories where you are an outside collaborator or a repository collaborator. The backup then misses the repositories of other accounts that you were invited to.
- Fine-grained tokens have no scopes, so the check in the settings editor cannot show the access it grants.

An organization can also restrict classic tokens. GitHub then answers `403` for that organization's repositories, and only a token type that the organization allows works there.

The GitHub documentation describes the details: [Managing your personal access tokens](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens). A fine-grained token can work if all repositories belong to one owner that allows it, but this is not the recommended setup.

### Save the token

Save the token in the Windows Credential Manager. The token then does not appear in any file or command line.

- Console: run `GitHubBackup.Cli.exe set-token`. The program asks for the token with hidden input and stores it as the credential `GitHubBackup:https://github.com`. The token is never accepted as an argument.
- Desktop app: open **Settings**, paste the token into the token field and select **Save token** (see [Desktop app](#settings-editor)).

### Where the program looks for the token

The program takes the first token it finds in this order:

1. The configuration key `GitHub:Token`, for example the environment variable `GitHubBackup__GitHub__Token` or `appsettings.json`.
2. The Windows Credential Manager entry `GitHubBackup:https://github.com`, which `set-token` or **Save token** writes.
3. GitHub CLI: `gh auth token --hostname github.com`, when `gh.exe` is in `PATH` and you are signed in.

The log names only the source, never the value. If no source has a token, the program works anonymously: it sees only public repositories, and the GitHub API allows 60 requests per hour.

Do not pass the token on the command line (`--GitHub:Token=...`). Other processes can read it, and it stays in your shell history. Use the Credential Manager (`set-token`) or an environment variable instead. Do not write the token into `appsettings.json` of a folder you share or publish.

## Configuration

Three files control the program. All of them sit next to the exe files.

| File | Who writes it | Purpose |
|---|---|---|
| `appsettings.json` | you (optional edits) | application settings: GitHub API address, tool paths, logging, telemetry |
| `user-settings.json` | the settings editor | the paths to git and 7-Zip |
| `backup-config.json` | you and the program | what to back up and where |

### appsettings.json

The published folder contains this file with these defaults. The program needs the file: without it, it stops with a configuration error.

```json
{
  "GitHub": {
    "ApiBaseUrl": "https://api.github.com",
    "Token": null
  },
  "Tools": {
    "GitPath": null,
    "SevenZipPath": "C:\\Program Files\\7-Zip\\7z.exe"
  },
  "Backup": {
    "ConfigPath": "backup-config.json",
    "ShortHashLength": 10,
    "VerifyArchive": true
  },
  "History": {
    "DatabasePath": "data\\history.db"
  },
  "OpenTelemetry": {
    "Enabled": false,
    "OtlpEndpoint": "http://localhost:4317"
  },
  "Serilog": {
    "MinimumLevel": { "Default": "Information" }
  }
}
```

| Key | Default | Rule |
|---|---|---|
| `GitHub:ApiBaseUrl` | `https://api.github.com` | Absolute URI with the scheme `https`. The scheme `http` is allowed only for a loopback address (`localhost`, `127.0.0.1`, `[::1]`), so the token never crosses the network unencrypted. |
| `GitHub:Token` | `null` | The token, if you choose to keep it in a file. Prefer the Credential Manager. |
| `Tools:GitPath` | `null` | Absolute path to `git.exe`. `null` means git comes from `PATH`. |
| `Tools:SevenZipPath` | `C:\Program Files\7-Zip\7z.exe` | Absolute path to `7z.exe`. |
| `Backup:ConfigPath` | `backup-config.json` | Path to the backup configuration. A relative path starts at the exe folder. Must not be empty. |
| `Backup:ShortHashLength` | `10` | Length of the commit hash in archive names, from 4 to 40. |
| `Backup:VerifyArchive` | `true` | Test each archive with `7z t` before the program moves it to the archive folder. |
| `History:DatabasePath` | `data\history.db` | Path to the history database. Must not be empty. |
| `OpenTelemetry:Enabled` | `false` | Turns telemetry on (see [Telemetry](#telemetry-opentelemetry)). |
| `OpenTelemetry:OtlpEndpoint` | `http://localhost:4317` | Absolute URI of the OTLP collector, checked when telemetry is on. |
| `Serilog:MinimumLevel:Default` | `Information` | One of `Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal`. |
| `Serilog:MinimumLevel:Override:<SourceContext>` | not set | Optional level for sources with that prefix, for example `"Microsoft": "Warning"`. |

If a rule fails, the program exits with code `2` and prints one line per error with the key name and without the value of `GitHub:Token`.

The program reads its configuration from these sources. A later source overrides an earlier one:

1. `appsettings.json` next to the exe.
2. `user-settings.json` next to the exe (optional, see below).
3. user secrets, only in the `Development` environment.
4. Environment variables with the prefix `GitHubBackup__`, for example `GitHubBackup__Backup__ShortHashLength`. Variables without the prefix and files `appsettings.{Environment}.json` are not read.
5. The command line: `--Section:Key=value`.

The variable `DOTNET_ENVIRONMENT` sets the environment; the default is `Production`.

### user-settings.json

The settings editor writes this optional file when you select **Save tool paths**. You can also create it by hand:

```json
{
  "Tools": {
    "GitPath": "C:\\Program Files\\Git\\cmd\\git.exe",
    "SevenZipPath": null
  }
}
```

The program reads only the two keys `Tools:GitPath` and `Tools:SevenZipPath` from this file and ignores any other key. The values follow the same rules as in `appsettings.json` (absolute paths). `null` means "not set": git comes from `PATH`, and 7-Zip from `C:\Program Files\7-Zip\7z.exe`. The values override `appsettings.json`; environment variables and the command line override this file. New paths apply after you restart the program.

### backup-config.json

This file says what to back up and where to put it. The program reads and writes it. It never creates it by itself on the command line: you create it before the first backup (see [First run](#first-run)). The desktop app creates it from the settings editor.

The smallest useful file names the archive folder and one account:

```json
{
  "schemaVersion": 1,
  "backupRoot": "C:\\Users\\Me\\OneDrive\\GitHubBackups",
  "sourcesRoot": null,
  "exclude": [],
  "discoverAccessibleRepositories": false,
  "accounts": [
    { "url": "https://github.com/octocat", "path": null, "exclude": [], "repositories": [] }
  ],
  "repositories": []
}
```

With an empty `repositories` list, the first `backup` finds the repositories of the account and adds them to the file (after you confirm them, or automatically with `--silent`).

A full file with every option:

```json
{
  "schemaVersion": 1,
  "backupRoot": "C:\\Users\\Me\\OneDrive\\GitHubBackups",
  "sourcesRoot": null,
  "exclude": [ "someone/*" ],
  "discoverAccessibleRepositories": false,
  "accounts": [
    {
      "url": "https://github.com/octocat",
      "path": null,
      "exclude": [ "test-*" ],
      "repositories": [
        { "name": "hello-world", "id": 1001, "branch": null, "path": null, "status": "Active" },
        { "name": "old-tool", "id": 1002, "branch": "legacy", "path": "E:\\Archive\\old-tool", "status": "Active" },
        { "name": "work-app", "id": 1003, "branch": null, "path": null, "clonePath": "D:\\Work\\WorkApp", "status": "Active" }
      ]
    },
    { "url": "https://github.com/my-org", "path": "E:\\WorkBackups\\my-org", "exclude": [], "repositories": [] }
  ],
  "repositories": [
    { "url": "https://github.com/someone-else/library", "id": 1004, "branch": "develop", "path": null, "status": "Active" }
  ]
}
```

| Key | Meaning |
|---|---|
| `schemaVersion` | Format version, `1`. |
| `backupRoot` | Absolute path of the folder for archives. Required. |
| `sourcesRoot` | Absolute path of the folder for clones. `null` means `sources\` next to the exe. |
| `exclude` | Global exclusion masks, for example `someone/*`. Only `*` and `?` work as wildcards. |
| `discoverAccessibleRepositories` | Optional, `false` by default. See [Discovery mode](#discovery-mode-discoveraccessiblerepositories). The program writes the key on every save. |
| `accounts[].url` | `https://github.com/<user or organization>`. |
| `accounts[].path` | Archive folder that replaces `<backupRoot>\<owner>` for this account. `null` keeps the default. |
| `accounts[].exclude` | Exclusion masks for this account. |
| `accounts[].repositories[]` | Repositories of the account: `name`, `id`, `branch` (`null` means the default branch), `path`, optional `clonePath`, `status`. |
| `repositories[]` | Single repositories: the same keys, with `url` instead of `name`. |
| `path` of a repository | Archive folder of that repository. It replaces the whole path. |
| `clonePath` | Optional absolute path of the whole clone folder (see [Own clone path](#own-clone-path-clonepath)). |
| `id` | GitHub repository ID. It is optional in entries you add by hand (see [Entries without `id`](#entries-without-id)). |
| `status` | `Active` or `Unavailable`. |

Archive folders follow these rules:

- The default archive folder of a repository is `<backupRoot>\<owner>\<repo>`, for your own and for other people's repositories alike.
- The `path` of an account replaces `<backupRoot>\<owner>`, so the archive folder is `<account path>\<repo>`.
- The `path` of a repository replaces the whole archive folder.
- The clone of a repository is `<sourcesRoot>\<owner>\<repo>`, and the wiki clone is `<sourcesRoot>\<owner>\<repo>.wiki`. The `path` values do not change clones.

Validation runs when the file is loaded, in `validate` and in the settings editor. It checks:

- `backupRoot` is set and absolute; `sourcesRoot`, every `path` and every `clonePath` are absolute (a relative path is an error);
- URLs are valid and point to github.com;
- owner and repository names are valid folder names;
- `id`, when set, is a positive number; a missing `id`, `null` or `0` means "no id"; a negative `id` is an error;
- `discoverAccessibleRepositories` is `true` or `false`;
- there are no duplicate repositories (by ID among entries with an ID, and by name among all entries);
- two entries never share the same archive folder;
- `clonePath` is not a drive root, does not equal `sourcesRoot`, is not inside it and does not contain it, and its clone and wiki folders do not equal or nest into the folders of other entries;
- a warning appears when clones end up inside the archive tree (`backupRoot` or an archive folder).

`validate` never calls GitHub.

## OneDrive scenario

You can keep the archives in OneDrive and the clones on a local disk. Set `backupRoot` to a folder inside OneDrive and leave `sourcesRoot` at `null` (or point it to another local folder):

```json
{
  "schemaVersion": 1,
  "backupRoot": "C:\\Users\\Me\\OneDrive\\GitHubBackups",
  "sourcesRoot": null,
  "exclude": [],
  "discoverAccessibleRepositories": false,
  "accounts": [
    { "url": "https://github.com/octocat", "path": null, "exclude": [], "repositories": [] }
  ],
  "repositories": []
}
```

Clones and archives live in different trees:

```
<sourcesRoot>\                         default: <application folder>\sources (local disk only)
├── .tmp\                              temporary archives before the move
├── .clone-locations.json              previous clone folders by GitHub ID
└── octocat\
    ├── hello-world\                   clone of the repository
    └── hello-world.wiki\              clone of the wiki (if there is one)

<backupRoot>\                          for example inside OneDrive
└── octocat\
    └── hello-world\                   archive folder of the repository
        ├── .backup-state.json
        ├── octocat_hello-world_20261002-143015_a1b2c3d4e5.zip        repository archives
        └── octocat_hello-world.wiki_20261002-143020_9f8e7d6c5b.zip   wiki archives
```

Rules that keep OneDrive safe:

- The program builds and tests each archive on the local disk, in `<sourcesRoot>\.tmp\`. It moves the finished archive to the archive folder only after the test passes. OneDrive therefore never uploads a half-written file.
- If `sourcesRoot` lies inside `backupRoot` or inside an archive folder, the program warns you at startup: the clones would sync to the cloud. The same warning applies to `clonePath` folders.
- If you leave `sourcesRoot` at `null`, the clones sit in `sources\` of the application folder. Do not place the application folder inside OneDrive in that case. Install it on a local disk, or set `sourcesRoot` to a local folder.
- The program never deletes archives.

An archive name is `<owner>_<repo>_<yyyyMMdd-HHmmss>_<shortHash>.zip`. The time is the local time of the machine when the archive was created. `shortHash` is the first characters of the commit hash of the selected branch (10 by default, `Backup:ShortHashLength`). A wiki archive uses `<repo>.wiki`. The archive holds the whole clone folder including `.git`; the root folder inside the archive is the clone folder name (`hello-world\`, `hello-world.wiki\`).

## First run

Follow these steps once. Each step builds on the previous one.

1. **Install** the application folder (see [Installation](#installation)).
2. **Save the token.** Run `GitHubBackup.Cli.exe set-token`, or paste it into **Settings** in `GitHubBackup.exe` and select **Save token**.
3. **Create `backup-config.json`.** You have two ways:
   - Desktop app: start `GitHubBackup.exe`. Without the file, the main window shows `No settings yet. Open the settings editor to create backup-config.json.` and a button **Open settings**; the backup buttons are disabled. The editor opens with an empty configuration and a note that the file does not exist yet and that **Save** creates it. Fill in at least the archive folder (`backupRoot`) and one account, then select **Save**. **Save** checks the fields; without `backupRoot` the file is not created and the error appears at the field. After a successful save, the main window shows the repository tree at once, without a restart. **Cancel** does not create a file.
   - By hand: copy the minimal example from [backup-config.json](#backup-configjson), edit the values and save it as `backup-config.json` next to the exe files.
4. **Validate.** Run `GitHubBackup.Cli.exe validate`. It checks `backup-config.json`, git, git-lfs, 7-Zip and the token source, and prints `Token: <source name>`. It does not call GitHub and changes nothing. Fix every error it reports.
5. **Try a dry run.** Run `GitHubBackup.Cli.exe backup --dry-run`. It shows what a real run would do and changes nothing (see [Dry run](#dry-run---dry-run)). New repositories appear in the list below the table.
6. **Run the first backup.** Run `GitHubBackup.Cli.exe backup`. The program lists the repositories it found and asks what to do with each new one. Then it clones them and creates the archives. At the end it prints a summary table and the path of the day's log.

Without `backup-config.json`, `backup` and `validate` exit with code `2` and print `The backup configuration file '<path>' does not exist. Create it in the settings editor of GitHubBackup.exe.` The console app never creates the file.

Check the result: the archive folder holds a `.zip` per repository, `sources\` holds the clones, and `logs\` holds the log.

## Command-line reference

`GitHubBackup.Cli.exe` has three commands. With no command, the program only checks its settings and exits with code `0`.

| Command | What it does |
|---|---|
| `GitHubBackup.Cli backup` | Discovers repositories and backs up the repositories of `backup-config.json`: accounts first, then single repositories, in file order |
| `GitHubBackup.Cli backup --config <path>` | The same with another `backup-config.json` (default: next to the exe) |
| `GitHubBackup.Cli backup --account <url\|login>` | Only the entries of one tracked account |
| `GitHubBackup.Cli backup --repo <url\|owner/repo>` | Only one repository of `backup-config.json`; `--account` and `--repo` together are an error |
| `GitHubBackup.Cli backup --silent` | Never asks a question; decides everything automatically (see below) |
| `GitHubBackup.Cli backup --dry-run` | Shows what a normal run would do and changes nothing except saving found `id` values (see [Dry run](#dry-run---dry-run)); works with `--account`, `--repo` and `--silent` |
| `GitHubBackup.Cli validate [--config <path>]` | Checks `backup-config.json`, git, git-lfs, 7-Zip and the token source without calling GitHub and without changes; prints the token source name (`Token: ...`), never the value; exit code `0` or `2` |
| `GitHubBackup.Cli set-token` | Asks for the token with hidden input and stores it in the Windows Credential Manager (`GitHubBackup:https://github.com`); exit code `0`. If the input is redirected or the write fails, it prints one error line and exits with `2`. The token is not accepted as an argument |
| `GitHubBackup.Cli --help` | Help for commands and options (also `backup --help`, `validate --help`, `set-token --help`) |

With `dotnet run`, pass the arguments after `--`: `dotnet run --project src/GitHubBackup.Cli -- backup`. The .NET configuration keys (`--Section:Key=value`) are also accepted. Any argument error produces one line `Command-line error: ...` and exit code `2`. Option values never appear in the message. A stray argument appears only when it is at most 32 characters long, and for `set-token` it never appears.

Only one `backup` run (including `--dry-run`) can run on the machine at a time. If another run is active, the second run changes nothing, writes `Error: A backup is already running.` and the log path to stderr, and exits with `2`. The lock is the system mutex `Global\GitHubBackup.BackupRun`, shared by all users and sessions. If a previous run crashed without releasing the lock, the next run takes it and writes a warning to the log.

Before it processes repositories, `backup` cleans `<sourcesRoot>\.tmp\` of files from interrupted runs. Then it compares the repositories of the tracked accounts with `backup-config.json`. With `discoverAccessibleRepositories: true`, it also compares other repositories your token can reach (see [Discovery mode](#discovery-mode-discoveraccessiblerepositories)). Without `--silent`, it asks about every new and every renamed repository. With `--silent`, it decides on its own:

- New repository: in interactive mode, a table shows its data (name, URL, owner, visibility, fork, archived, default branch, wiki, account) and a proposed entry. You can add it, add it with another branch and archive folder, skip it this time, or exclude it (from the account or globally). With `--silent`, the repository is added with the default branch and folder and is backed up in the same run.
- Missing repository: the program marks it `Unavailable`. Its clone and archives stay in place. There is no question.
- Renamed repository: it gets the new name, and the clone is moved in every case. In interactive mode you choose whether to rename the archive folder (a) or keep it and store it in `path` (b, the default). With `--silent`, the answer is always (b). If the archive folder does not depend on the name, there is no question.

Without `--silent`, a run with redirected input (for example from the Task Scheduler) exits with code `2` and a hint to use `--silent`. It processes no repositories and changes nothing. This check runs before any call to GitHub and applies to every filter (`--account`, `--repo`). With `--dry-run`, there is no such check.

### Discovery mode (`discoverAccessibleRepositories`)

This is an optional top-level key of `backup-config.json` (after `exclude`, before `accounts`). The default is `false`. The program writes the key on every save.

- `false` (strict mode): only repositories whose owner matches (case-insensitive) an account in `accounts` count as new. Repositories of organizations and other owners that GitHub returns for your token are not offered, not added to the file and not backed up: in interactive mode, with `--silent` and with `--dry-run`. They never cause exit code `3`.
- `true`: repositories of other owners that your token can reach (for example, where you are a collaborator) count as new too. Without `--silent`, the program asks about each of them. With `--silent`, it adds the repository to `repositories`.

In both modes, the program backs up all entries of `repositories`, and it recognizes missing, returned and renamed entries as usual.

To back up an organization, add it to `accounts`. To back up a repository where you are a collaborator, add it to `repositories` or turn the key on.

### Entries without `id`

Each repository entry in `backup-config.json` stores the GitHub ID (`id`). The ID is how the program recognizes a rename. In an entry you add by hand (alone or in an account list), you can leave `id` out: a missing `id`, `"id": null` and `"id": 0` mean the same. Never write an arbitrary `id`: an entry with the ID of another repository is taken for a renamed repository. A negative `id` is a validation error. `validate` does not call GitHub and accepts an entry without `id`.

At the start of a run, before discovery, `backup` looks up such an entry by name (`GET /repos/{owner}/{repo}`). It writes the found ID to `backup-config.json` without a question, in one save together with the discovery changes. It looks up only the entries the run processes: it respects `--account` and `--repo` and skips excluded entries. Filling in an ID does not by itself cause exit code `3`.

- If GitHub answers 404 or 403, the entry gets `Failed` with the error `The repository was not found on GitHub.` It stays without `id` and is not marked `Unavailable`. The other repositories are processed.
- If the found ID already belongs to another entry, the program does not write it, and the entry gets `Failed` with an error that names that entry.
- Any other GitHub API failure during the lookup is a critical error with exit code `2`.

The program saves the changed `backup-config.json` before it processes repositories. For each repository, it updates the clone and, if something changed, creates a new archive. It skips a repository with the status `Unavailable`. An error in one repository does not stop the run.

At the end, the program prints a table with the repository, status (`Archived`, `UpToDate`, `Warning`, `Failed`, `Skipped`, `Cancelled`), archive, duration and error. Below it are the totals by status and the path of the text log of the day.

### Cancel a run

Ctrl+C cancels the run. The program stops the current git or 7-Zip process and deletes its temporary files. If an archive was already moved to the archive folder, the program still records it in `.backup-state.json`. The summary shows:

- the repositories processed before the cancel, with their statuses;
- the interrupted repository or wiki with the status `Cancelled`;
- the unprocessed repositories with the status `Skipped` and the reason `Not processed: the run was cancelled.`;
- the line `Cancelled: the run was stopped before it finished.`; the totals then count `Cancelled` too.

If the cancel comes during discovery or the questions, there is no table. The cancel line and the totals remain. Cancelling `validate` and `set-token` prints one line `Error: The operation was cancelled.` to stderr. In every case, the exit code is `2`.

### Exit codes

| Exit code | Meaning |
|---|---|
| `0` | No errors |
| `1` | At least one repository ended with `Failed` |
| `2` | Critical error: wrong arguments, `appsettings.json`, `backup-config.json`, missing tools, another run is active, or a GitHub API failure during discovery or `id` lookup (for example, the rate limit is exhausted or the token is rejected). The program processes no repositories and prints one line per error to stderr, then the log path. The same code means a run cancelled by the user (Ctrl+C) |
| `3` | No errors, but the run found new repositories (by the rule of `discoverAccessibleRepositories`), missing repositories or renamed repositories |

When several conditions apply, the first one wins: `2`, then `1`, then `3`.

### Own clone path (`clonePath`)

By default the clone is `<sourcesRoot>\<owner>\<repo>` and the wiki clone is `<sourcesRoot>\<owner>\<repo>.wiki`. The optional key `clonePath` of a repository entry (in the `repositories` of an account or in the single `repositories`) sets the absolute path of the whole clone folder. The wiki clone then sits next to it, in `<clonePath>.wiki`. With this key, you can back up the working copy you work in, without a second clone:

```json
{ "name": "work-app", "id": 1003, "branch": null, "path": null, "clonePath": "D:\\Work\\WorkApp", "status": "Active" }
```

What the program does with such a folder:

- If the folder does not exist or is empty, the program clones the repository into it.
- An existing working copy whose `origin` points to `https://github.com/<owner>/<repo>.git` is used as the clone.
- When it updates the clone, ignored files (`.gitignore`) stay: the program runs `git clean -ffd`, not `git clean -ffdx`. Builds, local settings and other ignored files are not deleted.
- The archive holds only `.git` and the tracked files, including submodules. Ignored files are not archived. The root folder inside the archive is the folder name on disk (`WorkApp\`, `WorkApp.wiki\`).
- The program never deletes `<clonePath>` and `<clonePath>.wiki`.
- Git hooks (`.git/hooks`, `core.hooksPath`, for example `.husky`) and `core.fsmonitor` do not run in the processes of the program. Only the commands that talk to the server receive the token.
- If a new commit on the server tracks a file at the path of your ignored file, `checkout` and `reset --hard` overwrite it. This is how git works.
- If a tracked file of the working copy is a real symbolic link or junction on disk (the copy was created with `core.symlinks=true`), the program creates no archive and the entry gets `Failed`: 7-Zip would take files from outside the clone. Replace the links with regular files. This rule applies to every clone, including a working copy that was moved into `sourcesRoot` or accepted there as a clone.

If you set, change or remove `clonePath`, the program moves the clone to the new folder at the next run and points `origin` to the repository URL. The previous clone folders and the URLs of their `origin` are stored in `<sourcesRoot>\.clone-locations.json`. The program does not move the clone, the entry gets `Failed` with a reason, and nothing is deleted, when:

- the clone has local work (see [Clone check](#clone-check-before-an-update));
- the new folder already exists (for example, you pointed to your working copy while the old clone is in `sourcesRoot`: delete the old clone or rename one of the folders);
- the folders are on different volumes;
- the `origin` of the clone points to another repository (then `origin` stays unchanged too);
- you removed the key and the working copy has ignored files (for example `.env`): in `sourcesRoot`, an update runs `git clean -ffdx` and would delete them. Move or delete these files, or set `clonePath` again.

When the repository is renamed on GitHub, the clone at `clonePath` is not moved; only the `origin` URL changes.

`validate` and loading check the key: the path is absolute and not a drive root; it does not equal `sourcesRoot`, is not inside it and does not contain it; the clone and wiki folders of the entry do not equal and are not nested in the folders of other entries. If the clone folder is inside `backupRoot` or an archive folder, a warning appears.

Limits:

- `origin` must point to `https://github.com/...`. A working copy with an SSH `origin` gets `Failed`.
- A linked worktree (`.git` as a file) is not supported.
- You edit the key only in `backup-config.json`; the dialogs do not have it.

### Clone check before an update

Updating a clone (`checkout`, `reset --hard`, `clean`) would overwrite local work. So the program checks the clone before it updates or moves it, and your work is never lost. The check applies to all clones: in `sourcesRoot` and in `clonePath`, for the repository and for the wiki. It only reads the clone and does not run `fetch`. The check fails when the clone has:

- uncommitted changes, including changes in submodules;
- untracked files, except ignored ones;
- another branch or a detached HEAD (the program expects the configured branch, or the default branch if the configured one does not exist on the server; the branch of the latest archive is also allowed, so that a changed `branch` in the settings switches the clone);
- commits that are not in `origin/<branch>` (without such a branch: in none of the `origin/*` branches).

If the branch of the latest archive is selected and the local branch with the name of the expected branch holds unpushed commits, that is a reason too: the update would recreate this branch. The check repeats without contacting the server after `fetch` and before `checkout`, `reset` and `clean`, so work saved during `fetch` is not lost either. The repeated check counts unpushed commits against `origin/*` as they were before `fetch`, so a force-push on the server does not turn old server commits into "unpushed" ones. The update switches the clone to exactly the branch the check expected.

Then the entry gets `Failed` with a reason, for example `The clone 'D:\Work\WorkApp' has local changes: uncommitted changes, 2 unpushed commit(s). Commit and push them or switch the branch, then run the backup again.` The clone does not change, no archive is created, the exit code is `1`, and the other repositories are processed. Commit and push your changes or switch the branch, and the next run updates the clone.

If nothing changed on the server, the quick check gives `UpToDate` and does not touch the clone, even with local changes.

A non-empty folder without `.git` and a clone whose `origin` points to another repository also give `Failed`. The folder stays unchanged.

### Dry run (`--dry-run`)

`backup --dry-run` runs discovery and the quick check (`git ls-remote`) and shows what a normal run would do:

- `WouldClone`: there is no clone, and a normal run would clone the repository. The program does not call git.
- `WouldArchive`: a normal run would create an archive. The archive column shows its future name.
- `UpToDate`: nothing changed, or the repository is empty.
- `Warning`: the requested branch does not exist on the server, and the default branch would be used. The future archive name is shown too.
- `Failed`: the check failed (for example, `ls-remote` returned an error). The other repositories are processed.
- `Skipped`: the repository is marked `Unavailable` or disappeared from GitHub (a normal run would mark it `Unavailable`).

The wiki is checked like a repository. Above the table, the program prints `Dry run: nothing was cloned, updated, archived or saved.` (or the line about saved `id` values, see below). Below the table, it prints what discovery found: `New`, `Missing`, `Renamed`, `Returned`. New repositories appear only in this list and have no table rows. A returned (`Returned`) repository is checked as an active one. The totals count `WouldClone`, `WouldArchive`, `UpToDate`, `Warning`, `Failed` and `Skipped`.

The clone check and the folder check run as in a normal run, and the entry gets the same `Failed` with the same reason. A clone move is only shown in the error column: `The clone would be moved from '<old>' to '<new>'.`

A dry run clones, updates, moves and archives nothing. It does not write `.backup-state.json` and `.clone-locations.json`, does not clean `<sourcesRoot>\.tmp\` and does not ask about new and renamed repositories. So it works with redirected input too. The exit code is the same as for a normal run: `3` if it found new, missing or renamed repositories.

A dry run changes `backup-config.json` in one case only: for entries without `id`. The run always checks such an entry: it looks up the ID by name, and the repository appears in the table as usual. In an interactive console, the program asks for each such entry: `ID is missing for <owner>/<repo>; found <id>. Save it to backup-config.json?` The answer "yes" writes the ID to the file. The answer "no" leaves the file unchanged, and the ID is used only until the end of the run. With `--silent` or with redirected input, the program writes the ID without a question. When it wrote an ID, the line above the table is `Dry run: nothing was cloned, updated or archived; only the missing repository ids were saved to backup-config.json.` An entry that GitHub cannot find gets `Failed`, as in a normal run. The desktop app writes the found ID without a question, also in a dry run.

## Scheduled backups (Task Scheduler)

Use the console app with `--silent` to back up on a schedule. `backup --silent` never asks a question and never waits for input, so the Task Scheduler can start it unattended.

Settings of the task:

| Setting | Value |
|---|---|
| Program | the full path of `GitHubBackup.Cli.exe` |
| Arguments | `backup --silent` |
| Start in | the application folder (optional: the program finds its files next to the exe) |
| Trigger | daily, at a time when the computer is on |
| User | the same user that ran `set-token`, because the Credential Manager entry belongs to that user |

Notes:

- Do not select **Do not store password** if the task must run when you are logged off: the task then cannot read your Credential Manager entry. With the default choice, Windows asks for your password once.
- If you do not want to use the Credential Manager, set the environment variable `GitHubBackup__GitHub__Token` for the user of the task instead.
- The task needs the computer on, or **Run task as soon as possible after a scheduled start is missed**.
- Only one `backup` run can be active at a time. A second start exits with code `2`.
- With `--silent`, the program adds new repositories automatically. Run `backup --dry-run` now and then to see what changes.
- If `backup-config.json` is missing, the task ends with code `2`. Create it first (see [First run](#first-run)).

### Create the task in the Task Scheduler

1. Press Win+R, enter `taskschd.msc` and press Enter.
2. In the right pane, select **Create Task...**. (Do not use **Create Basic Task**: it has fewer options.)
3. On **General**: enter the name `GitHubBackup`. Select **Run whether user is logged on or not**. Leave **Do not store password** cleared.
4. On **Triggers**: select **New...**, set **Daily** and a time, for example 03:00, then **OK**.
5. On **Actions**: select **New...**, keep **Start a program**. In **Program/script** enter the full path, for example `C:\Tools\GitHubBackup\GitHubBackup.Cli.exe`. In **Add arguments** enter `backup --silent`. In **Start in** enter `C:\Tools\GitHubBackup`. Select **OK**.
6. On **Conditions** and **Settings**, adjust power options as you like. Select **Run task as soon as possible after a scheduled start is missed**.
7. Select **OK** and enter your Windows password when asked.
8. To test, right-click the task and select **Run**. Check the column **Last Run Result** in the task list.

### Optional: create the task with PowerShell

This example creates the same task for the current user. It runs only while you are logged on. Use the GUI for the option **Run whether user is logged on or not**. Run it in a normal PowerShell window and adjust the paths:

```powershell
$folder = 'C:\Tools\GitHubBackup'
$action = New-ScheduledTaskAction -Execute "$folder\GitHubBackup.Cli.exe" -Argument 'backup --silent' -WorkingDirectory $folder
$trigger = New-ScheduledTaskTrigger -Daily -At '03:00'
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 12)
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive
Register-ScheduledTask -TaskName 'GitHubBackup' -Action $action -Trigger $trigger -Settings $settings -Principal $principal
```

### Check the result

The column **Last Run Result** of the task shows the exit code of the program:

| Last Run Result | Meaning |
|---|---|
| `0x0` | No errors |
| `0x1` | At least one repository ended with `Failed` |
| `0x2` | Critical error, another run was active, or the run was cancelled |
| `0x3` | No errors, but the run found new, missing or renamed repositories |

Open the log of the day in `logs\` for details (see [Logs, history and telemetry](#logs-history-and-telemetry)).

## Desktop app

`GitHubBackup.exe` uses the same configuration and the same core as the console app.

- **Main window.** A tree "accounts, repositories" with a group for single repositories. Each item shows its status and the date of the last archive. The buttons **Backup all**, **Backup account** and **Backup repository** start a run. The check box **Dry run** switches the next run to dry-run mode. It is a mode, not a command: the check box does not start a run. The buttons **Settings** and **History** open the settings editor and the history.
- **Discovery dialog.** A table of new, missing, renamed and returned repositories. You can add, skip or exclude each row or all rows at once, choose the rename variant, and edit the branch and the archive folder in the table.
- **Progress.** The current repository and operation, a live log feed and a **Cancel** button. Cancel stops the current operation and removes temporary files.
- **Summary.** The same information as the console summary when the run ends. A found repository `id` is saved without a question, also in a dry run.
- **History.** The list of runs with filters (period, status, mode) and run details, and the archive history of a selected repository with the actions **Open folder** and **Show in Explorer**.

### Settings editor

The button **Settings** on the main screen opens the settings editor in place of the repository tree. While a backup run is active, the button is disabled: the run also writes `backup-config.json`.

The editor changes `backup-config.json`:

- the archive root (`backupRoot`), the clone root (`sourcesRoot`; an empty field means the folder `sources` next to the exe) and the global exclusions (masks, comma separated);
- the switch `discoverAccessibleRepositories`;
- accounts: URL, archive folder, exclusions; and for the repositories of an account, the branch and the archive folder. You can remove a repository of an account but not add one: discovery adds new repositories;
- single repositories: URL, branch and archive folder. A new entry gets `id` 0 and the status `Active`; the next run finds the `id` by name.

Before it writes, the editor runs the same validation as the load of the file. Errors appear at the fields; errors without a field of their own appear under the form. While errors exist, the editor writes no file. The warning about clones inside the archive tree appears as a banner while you edit and does not block saving. The fields the editor does not show (`id`, `status`, repository names of an account, `schemaVersion`) stay as they were. The write is atomic, and the previous version stays in `backup-config.json.bak`. If the write fails, the editor shows the error, keeps the fields filled and leaves the file unchanged.

**Cancel** returns to the tree and writes nothing. If there are unsaved changes, the editor first asks for confirmation; it asks the same when you close the window.

First run: without `backup-config.json`, see [First run](#first-run).

The button **Save tool paths** saves the paths to git and 7-Zip to the optional file `user-settings.json` (see [user-settings.json](#user-settingsjson)).

The token section:

- **Save token** stores the entered token in the Windows Credential Manager, like `set-token`. An empty token is not saved: the field shows `The token cannot be empty.` After saving, the field is cleared. The running program keeps using the previous token, so the editor asks you to restart it. If the key `GitHub:Token` is also set, the editor warns you: that source ranks above the Credential Manager, and the saved token will not be used.
- **Check token** checks the token in the field, or, if the field is empty, the token that the source chain found, and names its source. It shows the login, the scopes (a fine-grained token has none, and the editor says so), the remaining and total request limit and the reset time. Without a token, it shows only the limit for anonymous access. An invalid or expired token (401) and other API errors appear on a separate line. **Cancel check** stops the check. The request goes only to the configured `GitHub:ApiBaseUrl`.

The token value never reaches the log, the error messages or any file.

## Logs, history and telemetry

### Logs

The program writes one log per day to the folder `logs\` next to the exe:

- `logs\githubbackup-YYYYMMDD.log`: readable text;
- `logs\githubbackup-YYYYMMDD.json`: structured log (CLEF, one JSON line per event).

Every entry has a run ID and, where it applies, the repository (`owner/repo`) and the operation (`clone`, `fetch`, `archive`). The log holds the full command line of every git and 7-Zip call with secrets masked, every output line of the tools, and the exit code and duration. The token and authorization headers never appear in the log. The program does not delete old logs. The level comes from `Serilog:MinimumLevel` in `appsettings.json`.

### History

The history of runs and archives sits in the SQLite database `data\history.db` next to the exe. The program creates it at startup. The desktop app shows it under **History**.

### Telemetry (OpenTelemetry)

Telemetry is off by default (`OpenTelemetry:Enabled=false`): the OpenTelemetry SDK and exporter are not registered. With `OpenTelemetry:Enabled=true`, the program sends traces and metrics over OTLP (gRPC) to the address `OpenTelemetry:OtlpEndpoint` (default `http://localhost:4317`). The resource is named `GitHubBackup`.

Traces:

```
backup.run                      githubbackup.dry_run, githubbackup.run.outcome
├── backup.discovery            absent with --repo
├── backup.repository           githubbackup.repository, githubbackup.target, githubbackup.status
│   ├── ls-remote | clone | fetch
│   └── archive
└── backup.repository           wiki of the same repository (githubbackup.target = Wiki)
```

- `githubbackup.run.outcome`: `completed`, `stopped` (critical error), `cancelled` or `failed` (unexpected exception).
- An error sets the status `Error` and the tag `error.type` (the exception type name). Traces hold neither the exception text nor the token.
- The tag `githubbackup.repository` holds repository names (`owner/name`), private ones included. An address with the scheme `http` sends them unencrypted, so connect a remote collector over `https`. Set the credentials of the collector with the variable `OTEL_EXPORTER_OTLP_HEADERS`, not in `OtlpEndpoint`.
- Repositories that were not processed (not started because of a cancel, missing in a dry run, new and unconfirmed) appear only in the metrics.

Metrics (meter `GitHubBackup`):

| Metric | Type and unit | Tags |
|---|---|---|
| `githubbackup.repositories` | Counter, `{repository}` | `githubbackup.status`, `githubbackup.target`, `githubbackup.dry_run` |
| `githubbackup.repository.duration` | Histogram, `s` | the same |
| `githubbackup.run.duration` | Histogram, `s` | `githubbackup.run.outcome`, `githubbackup.dry_run` |
| `githubbackup.archive.size` | Histogram, `By` | `githubbackup.target`; only created archives |

To check it by hand with the .NET Aspire Dashboard:

1. Start the dashboard in Docker. The OTLP port `18889` of the dashboard is published as `4317`:

   ```
   docker run --rm -it -p 18888:18888 -p 4317:18889 --name aspire-dashboard mcr.microsoft.com/dotnet/aspire-dashboard:latest
   ```

2. Open the link `http://localhost:18888/login?t=...` from the container output.
3. Run a backup with telemetry on: `GitHubBackup.Cli.exe backup --dry-run --OpenTelemetry:Enabled=true`. Another address is set with `--OpenTelemetry:OtlpEndpoint=<uri>`.
4. On the **Traces** tab, select the resource `GitHubBackup`: the trace `backup.run` shows the hierarchy above and the span tags.
5. On the **Metrics** tab, select the meter `GitHubBackup`. The counter and the durations are recorded at the end of a run, and the program sends the last batch of metrics when it exits, so they appear after the run ends.

## Development

To build, test, publish and extend GitHubBackup, read the [developer guide](docs/development.md). The requirements are in [docs/PRD.md](docs/PRD.md) (in Russian). The program documentation (this file and the developer guide) is in English; the PRD, the plans and the AI harness documentation are in Russian.
