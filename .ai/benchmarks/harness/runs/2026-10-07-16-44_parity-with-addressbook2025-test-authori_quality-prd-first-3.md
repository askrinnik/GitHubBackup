**Nothing implements this yet, and the PRD doesn't allow it yet.** I haven't edited anything. This is what I found, what I'd change and what I need from you.

## What the repository contains today
- `CliApplication` (`src/GitHubBackup.Cli/CliApplication.cs`) only builds and starts the host and logs the start and end of the run. There is no `backup` command, no option parsing and no orchestrator.
- A search for `IGitHubApi`, `PushedAt` and `Octokit` in `src` finds nothing. Discovery and the GitHub API wrapper are not built.
- FR-9.2 lists `--account`, `--repo`, `--dry-run`, `--silent` and `--config`, and no `--since`. `.claude/rules/cli.md` says a new option is a PRD change first.

## Step 1: PRD change (needs your approval before any code)
- Add a `--since <date>` row to the FR-9.2 table.
- Add a requirement for the filter itself, in the discovery section or §6.9. It would say that a repository is processed only if its `pushed_at` is on or after the date.
- Bump the version, set the date and add a row to the change history.
- Update `README.md` where it describes the options, and then the issue.

## Step 2: Code, once the PRD is approved
- **Core:**
  - Add `PushedAt` (`DateTimeOffset?`) to the discovered-repository model.
  - Add `Since` to the backup request.
  - Add a filter step in `BackupOrchestrator` that gives filtered-out repositories a `Skipped` status with a reason.
- **Infrastructure:** map Octokit's `Repository.PushedAt` in the `IGitHubApi` implementation. Add a WireMock contract test for it.
- **Cli:** add the `--since` option to the `backup` command. Parse and validate it in the parser, as `--account` and `--repo` are. The handler maps it to the request.
- **Tests:**
  - Core: unit tests for the filter boundary (exactly on the date, one second before, `PushedAt` null).
  - Core: a test that a filtered-out repository is not classified as missing.
  - Cli: parser tests.
  - Cli: a dry-run snapshot.

## Dependencies
`--since` depends on the discovery and `backup` command issues from phases 2 and 3. I would create the issue with a `## Зависимости` section and a GitHub "blocked by" relation, but only with your go-ahead.

## Risk to design around
Discovery marks repositories that vanish from the API list as `Unavailable` (FR-2.4). The filter must run after that classification, on the full list. Otherwise every repository older than the date would be flagged as missing.

## Questions I need answered before I write the PRD text
1. **Date format and zone:** should I accept `yyyy-MM-dd` only, or also a full timestamp? Is a date-only value read as local midnight or UTC midnight? I'd suggest ISO date or timestamp, with a date-only value read as local midnight via `TimeProvider`, and a future date rejected as an error.
2. **`--repo` with `--since`:** `--repo` skips discovery, so there is no `pushed_at`. Should I reject the combination, fetch that one repository's metadata, or ignore `--since`? I'd suggest rejecting it in the parser.
3. **New repositories older than the date:** in `--silent` mode, should they still be added to `backup-config.json`, or left untouched? I'd suggest still adding them, so that discovery's results don't depend on the filter. They would be marked `Skipped` and not backed up.
4. **Wiki-only changes:** `pushed_at` doesn't reflect pushes to the wiki, so a wiki-only change would be skipped. Is that acceptable, to be documented in the PRD? Or should wikis of repositories that fail the filter be checked anyway?
5. **Summary and exit code:** should filtered-out repositories appear as `Skipped` rows in the FR-9.7 table, or only as a count? I'd suggest a count in the totals and `Skipped` rows only in the log. They would not affect the exit code.
