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
- Only commands that talk to the server get the token: `clone`, `fetch`, `ls-remote`, `submodule update`, `lfs pull`. Local steps (`checkout`, `reset`, `clean`, `status`, `for-each-ref`, `config`, `remote set-url`) run without it, because they start filters and other programs the repository or the user's working copy configures.
- Every git process runs with `core.hooksPath` pointing at a folder inside `.git` that the application never creates, with `core.fsmonitor=false` and with `core.protectNTFS=true`: hooks and file system monitors are programs of the repository or the user and must never run in the application's processes.
- Local steps run with `GIT_LFS_SKIP_SMUDGE=1`, since the git-lfs filter could not authenticate without the token; the tokened `git lfs pull` then fetches and checks out the LFS content of the checked-out branch. `submodule update` keeps the token (accepted risk, see `security.md` P3).
- Never put the token in arguments, in a remote URL, in `.git/config`, in a credential helper or in any file inside the clone — the clone is archived as is.
- Interactive prompts are disabled for every git process: `GIT_TERMINAL_PROMPT=0`, `GCM_INTERACTIVE=never`. `git credential fill` is not used (FR-11.2).
- `core.longpaths=true` and `core.symlinks=false` are set for every git invocation that touches the working tree (FR-4.6): a symbolic link in a clone would make 7-Zip archive the content of a file outside the clone.

## Output, exit codes and logging

- Success or failure is decided by the **exit code** only. git writes normal progress to stderr; never treat stderr output as an error (FR-4.7).
- Log before start: the full command line with secrets masked. Log every stdout and stderr line as its own event with `Tool`, `Stream` and `Repository` properties. Log after exit: exit code and duration (FR-10.4).
- Masking is applied by `IProcessRunner` to the logged command line and to environment values it logs; secret values are registered with it, not filtered ad hoc by callers.
- Read stdout and stderr asynchronously and concurrently; a full pipe buffer must not deadlock the child process.

## Cancellation and timeouts

- `IProcessRunner` honours the `CancellationToken`: on cancellation it kills the whole process tree (`Kill(entireProcessTree: true)`), waits for exit and then throws `OperationCanceledException`.
- After a cancelled or failed operation, temporary files are removed: partial archives in `<sourcesRoot>\.tmp\`, half-written state files (FR-7.4, NFR-4).

## git specifics

- The server is the source of truth and the update sequence is exactly FR-4.3, but a clone may be the user's working copy (`clonePath`, FR-3.10). Nothing that changes the working tree, the branches or the folder of an existing clone (update, move) runs before the clone check of FR-4.8 passes; a clone with local work is left untouched and its item fails with the reason.
- The clone check only reads: no `fetch`, `status` with `--no-optional-locks`, unpushed commits counted against the `origin/*` refs the clone already has. An update is fetch, then the check again without the network, then checkout, reset and clean, so work saved during the fetch survives; the second check counts unpushed commits against the `origin/*` commits read before the fetch, and the update checks out exactly the branch the checks expected.
- A clone set by `clonePath` keeps its ignored files (`git clean -ffd`, not `-ffdx`), leaves them out of the archive (7-Zip exclude list in `<sourcesRoot>\.tmp\`) and is never deleted, not even after a failed move or `remote set-url`. A working copy with ignored files is not moved into `sourcesRoot`. No clone whose tracked symbolic links are real links on disk is archived, wherever it lies.
- A folder that is not empty and has no `.git` folder, or whose `origin` does not point at the repository URL (or, for a move, at its previous URL), is never cloned into, updated, moved or repointed (FR-4.9, FR-3.10).
- Submodules are found from the gitlinks of the index (`ls-files --stage`, mode `160000`), never from `.gitmodules`; a submodule path must lie strictly inside the folder that lists it, each folder is visited once and the nesting is limited.
- Ref snapshots consider only `refs/heads/*` and `refs/tags/*`; `refs/pull/*` and other server refs are ignored (FR-6.2, П-8).
- LFS content is fetched only for the checked-out branch (FR-4.2).

## 7-Zip specifics

- Archives are ZIP without a password; the root folder inside the archive is the name of the clone folder on disk: `<repo>\` (or `<repo>.wiki\`), or the last segment of `clonePath` (FR-7.2).
- Create and test (`7z t`) the archive in `<sourcesRoot>\.tmp\`, then move it to the archive folder: rename on the same volume, otherwise copy under a temporary name, rename, delete the source (FR-7.4, FR-7.5).
- A state record is appended only after the archive is in its final place (FR-6.4).
