---
paths:
  - "src/GitHubBackup.Infrastructure/**"
  - "src/GitHubBackup.Cli/**"
  - "src/GitHubBackup.App/**"
  - "src/GitHubBackup.Core/**"
---

# Security

GitHubBackup is a desktop and console application that holds a GitHub token with `repo` scope and writes data from the network to disk. The threats are not those of a web service: there is no inbound request, but there is a powerful secret and untrusted names that become paths and process arguments.

**Severity:** **CRITICAL** — exploitable or leaks the token, fix before merge. **IMPORTANT** — real risk, fix in the same issue. **SUGGESTION** — defense in depth.

## S — Secrets (the token)

- **S1 CRITICAL.** The token never appears in logs, console output, exception messages, process arguments, remote URLs, `.git/config`, archives, `backup-config.json` or the history database (NFR-1, FR-10.6, FR-11.3).
- **S2 CRITICAL.** Exceptions from HTTP or process layers can carry headers or command lines; sanitize before logging or rethrowing with context.
- **S3 IMPORTANT.** The log states which token source was used, never the value or a prefix of it (FR-11.1).
- **S4 IMPORTANT.** The token is stored only in Windows Credential Manager (`set-token`, UI); never write it to a file the application creates. It is read from configuration only because the user chose to put it there.
- **S5 SUGGESTION.** Keep the token in memory for as short a time as practical; do not cache it in static fields.

## I — Untrusted input becoming paths and arguments

Names of owners, repositories, branches and tags come from the GitHub API, from `backup-config.json` or from the command line; all are untrusted.

- **I1 CRITICAL.** A path built from an owner or repository name stays inside its root: reject or escape `..`, rooted segments, drive letters, reserved device names (`CON`, `NUL`, `COM1`…), trailing dots and spaces, invalid characters. Verify with `Path.GetFullPath` that the result starts with the intended root.
- **I2 CRITICAL.** Values passed to git are separate arguments and cannot be parsed as options (see `external-processes.md`).
- **I3 IMPORTANT.** URLs accepted in configuration or on the command line are validated to be `https://github.com/<owner>[/<repo>]` (§7.3); nothing else is ever fetched or cloned.
- **I4 IMPORTANT.** Exclusion masks support only `*` and `?`; build the matcher without passing user text to a regular expression unescaped (FR-1.6).
- **I5 IMPORTANT.** JSON files are deserialized into concrete types with `System.Text.Json`; no polymorphic type handling from data.

## F — Files and archives

- **F1 IMPORTANT.** Writes to `backup-config.json` and `.backup-state.json` are atomic and keep the previous version (FR-2.8, NFR-2); a crash never leaves a truncated file under the final name.
- **F2 IMPORTANT.** Temporary files are created in folders the application owns with unique names, and removed on failure and cancellation.
- **F3 IMPORTANT.** The application never deletes clones, archives or state files of a repository that disappeared or became unavailable (FR-2.4); deletion of a clone happens only in the documented rename fallback (FR-2.5).
- **F4 SUGGESTION.** If code ever reads or extracts an archive, every entry path is checked to stay inside the target folder (zip slip).

## P — Processes and environment

- **P1 IMPORTANT.** Child processes get an explicit environment: only the variables they need are added, prompts are disabled, nothing from the parent is echoed into logs unmasked.
- **P2 IMPORTANT.** Tool paths are absolute and verified at startup; do not search the current directory for `git.exe` or `7z.exe`.

## D — Dependencies

- **D1 IMPORTANT.** New packages go through `Directory.Packages.props`; check `dotnet list package --vulnerable` when adding or updating one. Prefer well-maintained packages; no package for a few lines of code.

## Review checklist

When reviewing a change in these projects, report findings as `<ID> <severity> <file> <symbol>: <problem>`. Check S1–S2 first: grep the diff for logging of options objects, exceptions with inner HTTP details, string interpolation into process arguments, and URL construction with credentials.
