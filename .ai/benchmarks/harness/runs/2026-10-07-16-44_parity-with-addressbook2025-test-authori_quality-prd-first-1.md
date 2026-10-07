The PRD doesn't describe `--since`, so under this repo's rules the first step is a PRD change that you approve. I haven't edited anything.

## What I found

- **PRD:** FR-9.2 lists the `backup` options (`--account`, `--repo`, `--dry-run`, `--silent`, `--config`), and `--since` isn't one of them. `.claude/rules/cli.md` says a new option is a PRD change first.
- **CLI code:** `GitHubBackup.Cli` currently has only the host (`CliApplication.cs`, `Program.cs`, `CliLog.cs`). There is no `backup` command or `System.CommandLine` parsing yet. A grep for `pushed`, `PushedAt` and `--since` across `src/**/*.cs` and the PRD found nothing. `--since` therefore depends on the `backup` command issue and on the discovery code that reads repository metadata from GitHub.
- **No pushed-at field:** since the grep found no `PushedAt`, I expect the discovery model doesn't carry GitHub's `pushed_at` timestamp. I haven't read the discovery code, so confirm that when planning.

## Step 1: PRD change (Russian, `docs/PRD.md`)

- Add a row to the FR-9.2 table: `--since <date>`. A run processes only repositories whose last push on GitHub is on or after that date.
- Add a new requirement in §6.2 or §6.9 with the next free id. It would define:
  - **Date format and zone:** an ISO date (`2026-09-01`) or date-time. I propose a bare date means 00:00 UTC, and the comparison uses GitHub's `pushed_at` (UTC).
  - **Scope of the filter:** it applies after discovery and before the quick check. Discovery (new, missing and renamed repositories, FR-2.x) stays unfiltered, so config updates and exit code `3` behave as usual. Otherwise a filtered run would wrongly mark unseen repositories as missing.
  - **Filtered-out repositories:** they get `Skipped` with a reason (FR-9.8) and are not touched. I need your decision on whether they appear in the summary table or only in the totals.
  - **Combinations:** it works with `--account` and `--dry-run`. With `--repo` I'd either allow it, giving a single repository that is skipped if it is too old, or reject the combination. I need your decision.
  - **Invalid date:** an unparseable or future date exits with `2` and a one-line message.
- Bump the document version and date, and add a change-history row. The current version is 0.6, so this would be 0.7.
- Update `README.md` if it lists the options.

## Step 2: Code, once the PRD is approved

- **Core:**
  - Add a `PushedAt` (`DateTimeOffset`) property to the discovered-repository model, if it's missing.
  - Add a pure filter, for example `SinceFilter`, that takes the repositories and a cutoff and returns the ones to process and the ones to skip. Keeping it in Core makes it unit-testable and shareable with the UI later (FR-12.7).
  - Add a nullable `Since` to the backup request.
- **Infrastructure:** map Octokit's `Repository.PushedAt` into that field. Clamp to the repository's `CreatedAt` if `PushedAt` is null, because an empty repository has no push.
- **Cli:**
  - Define the `--since` option in the `backup` command with a parser that validates the date, in the parser and not deep in the run, as `cli.md` requires.
  - The handler maps it to the request.
  - Use an injected `TimeProvider` for the future-date check, never `DateTime.Now`.
- **Orchestrator:** apply the filter after discovery. It should still check that the repository is cloned, so a skipped repository isn't mistaken for a missing one.

## Tests

- **Core.Tests:** the filter at the boundary (exactly the cutoff is included), a null `PushedAt`, and an empty list.
- **Cli.Tests:** the date parser (valid date, date-time, garbage, future date), plus `--since` with `--repo` according to your decision.
- **Backup tests:** a filtered run marks old repositories `Skipped`, leaves the config and exit code `3` logic untouched, and works together with `--dry-run`.

## Decisions I need from you

1. Should `--since` combine with `--repo`, or be rejected?
2. Should skipped-by-date repositories show as rows in the summary table, or only in the totals?
3. Is a bare date read as 00:00 UTC, or as local time?

Once you've decided, I can draft the PRD text. Under the repo rules this would be an issue first, with a branch and commit only on your go-ahead.
