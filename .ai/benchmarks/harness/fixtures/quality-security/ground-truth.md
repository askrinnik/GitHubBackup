# Ground truth — security review of seeded vulnerabilities

`seed.patch` plants seven vulnerabilities in the files the prompt names. Each breaks a rule of
`.claude/rules/security.md` or `.claude/rules/external-processes.md` and a PRD requirement. The reviewer sees only the
patched files, not the diff, so a finding counts when it describes the vulnerable code, whatever it calls it.

| # | Where | Vulnerability | Rule |
|---|---|---|---|
| 1 | `GitClient.GetRemoteRefsAsync` | The authorization header with the token is passed as a process argument (`git -c http.https://github.com/.extraHeader=Authorization: Basic …`): it is visible in the process list and in the logged command line | S1, FR-11.3, NFR-1 |
| 2 | `GitClient.CloneAsync` | The URL that carries the token (`authenticatedUrl`) is logged by `GitClientLog.Cloning` without `ISecretMasker.Mask` | S1, FR-10.6 |
| 3 | `GitClient.CloneAsync` | The token is put into the remote URL (`https://x-access-token:<token>@github.com/…`): git stores it in `.git/config` of the clone, which is then archived, and it also reaches the process arguments | S1, FR-11.3, NFR-1 |
| 4 | `GitEnvironment.Create` | `GIT_TERMINAL_PROMPT=0` is not set, so git can wait for credentials on the terminal and hang silent or scheduled runs; the XML documentation still promises the variable | FR-11.2, P1 |
| 5 | `RepositoryPathResolver.GetCloneFolder` | The clone folder is built from the owner and repository names with plain `Path.Combine`: no name validation (`EnsureValidNames`) and no containment check (`ContainedFolder.Combine`), so `..`, a rooted name or a device name escapes or misuses the sources root. `GetWikiCloneFolder` next to it still checks both | I1 |
| 6 | `GitClient.ValidateBranch` with `GitClient.UpdateAsync` | A branch that starts with `-` is not rejected, and `UpdateAsync` passes the branch to `git fetch origin <branch>` without a preceding `--`, so a branch named `--upload-pack=…` becomes a git option | I2 |
| 7 | `SevenZipArchiver.Validate` | The archive file name is only checked for being non-empty (`ArchiveFileName.IsValid` is not called), so a name with `..\` or a rooted path places the archive, and its temporary copy, outside the archive and temporary folders | I1, F2 |

## Scoring

Max score: 7

- **Score** = items found. A partly found item counts 0.5: the right member is flagged with a vague or wrong risk (for
  example item 3 described only as "token in arguments" without the remote URL or `.git/config`, or item 6 flagged as
  a missing check without the option injection into `git fetch`). One finding that clearly covers two items (for
  example "the token URL is logged and stored in `.git/config`") counts for both.
- **False positives** = findings that assert a vulnerability that is not there, for example claiming the token is
  written to the archive by `SevenZipArchiver`, that `ProcessRunner` builds a shell command line, or that
  `GetWikiCloneFolder` lacks the containment check. Real extra findings (defense in depth, a missing test) are not
  false positives.
