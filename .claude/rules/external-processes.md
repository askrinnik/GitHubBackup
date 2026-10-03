---
paths:
  - "src/GitHubBackup.Infrastructure/**"
  - "src/GitHubBackup.Infrastructure.IntegrationTests/**"
---

# External processes: git, git-lfs, 7-Zip

The application drives `git.exe` and `7z.exe` as child processes (FR-4.1, FR-7.1). Every rule below exists because a mistake here leaks the token, hangs silent mode or corrupts a backup.

## One way to start a process

- Start processes only through `IProcessRunner`. No `Process.Start` anywhere else, no shell (`cmd /c`, `powershell -c`) in between.
- Pass arguments as a list (`ProcessStartInfo.ArgumentList`), never as one concatenated string — repository names, branch names and paths come from the network or the user.
- Any value that comes from outside (branch, tag, path, URL) and is placed where git could read it as an option is preceded by `--` or validated not to start with `-`. A branch named `--upload-pack=…` must not become an option.
- Set the working directory explicitly; never rely on the current directory of the application.
- Tool paths come from `IToolLocator` (configured path or `PATH`), checked at startup (FR-4.1, §4).

## Credentials

- The GitHub token reaches git only through environment variables of that one process: `GIT_CONFIG_COUNT`, `GIT_CONFIG_KEY_<n>` = `http.https://github.com/.extraHeader`, `GIT_CONFIG_VALUE_<n>` = `Authorization: …` (FR-11.3).
- Never put the token in arguments, in a remote URL, in `.git/config`, in a credential helper or in any file inside the clone — the clone is archived as is.
- Interactive prompts are disabled for every git process: `GIT_TERMINAL_PROMPT=0`, `GCM_INTERACTIVE=never`. `git credential fill` is not used (FR-11.2).
- `core.longpaths=true` is set for every git invocation that touches the working tree (FR-4.6).

## Output, exit codes and logging

- Success or failure is decided by the **exit code** only. git writes normal progress to stderr; never treat stderr output as an error (FR-4.7).
- Log before start: the full command line with secrets masked. Log every stdout and stderr line as its own event with `Tool`, `Stream` and `Repository` properties. Log after exit: exit code and duration (FR-10.4).
- Masking is applied by `IProcessRunner` to the logged command line and to environment values it logs; secret values are registered with it, not filtered ad hoc by callers.
- Read stdout and stderr asynchronously and concurrently; a full pipe buffer must not deadlock the child process.

## Cancellation and timeouts

- `IProcessRunner` honours the `CancellationToken`: on cancellation it kills the whole process tree (`Kill(entireProcessTree: true)`), waits for exit and then throws `OperationCanceledException`.
- After a cancelled or failed operation, temporary files are removed: partial archives in `<sourcesRoot>\.tmp\`, half-written state files (FR-7.4, NFR-4).

## git specifics

- Clones are read-only mirrors of the server state; the update sequence is exactly FR-4.3. Local changes in a clone are discarded by design.
- Ref snapshots consider only `refs/heads/*` and `refs/tags/*`; `refs/pull/*` and other server refs are ignored (FR-6.2, П-8).
- LFS content is fetched only for the checked-out branch (FR-4.2).

## 7-Zip specifics

- Archives are ZIP without a password; the root folder inside the archive is `<repo>\` (or `<repo>.wiki\`) (FR-7.2).
- Create and test (`7z t`) the archive in `<sourcesRoot>\.tmp\`, then move it to the archive folder: rename on the same volume, otherwise copy under a temporary name, rename, delete the source (FR-7.4, FR-7.5).
- A state record is appended only after the archive is in its final place (FR-6.4).
