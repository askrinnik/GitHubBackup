`--since` isn't in the PRD, so the PRD has to change first. I haven't edited any files.

## What exists today

- **PRD:** FR-9.2 lists the `backup` options as `--account`, `--repo`, `--dry-run`, `--silent` and `--config`. FR-2.7 says discovery runs at the start of every run, except with `--repo`. Nothing describes filtering by push date.
- **Code:** `src/GitHubBackup.Cli/Program.cs` is just `return 0;`. There is no backup command, orchestrator or GitHub client yet. Everything below is a plan against the architecture the PRD and rules describe, not a patch to existing code.
- **Rule:** `.claude/rules/cli.md` says a new option is a PRD change first. I'd follow that and wait for your approval before touching code.

## Step 1: PRD change (Russian, per NFR-7)

- Bump the version in the header and add a row to the change history.
- Add a `--since <date>` row to the FR-9.2 table: "Только репозитории, в которые был push не раньше указанной даты".
- Add a short requirement saying the filter applies after discovery. Discovery still sees the full list, so repositories filtered out are not misclassified as missing or `Unavailable` (FR-2.4).
- State how filtered repositories show up in the summary, status and exit code (FR-9.7, FR-9.8, FR-9.10).
- Update the issue after the PRD.

## Step 2: Code, once the PRD is approved

- **Core:**
  - Add `PushedAt` (`DateTimeOffset?`) to the discovered-repository model.
  - Add a pure filter, for example `RunFilter.Matches(repo, since)`, that returns true when `PushedAt >= since`. It takes the date as a value and never reads the clock.
  - Add a `Since` field to the backup request.
  - In the orchestrator, apply the filter after discovery and the new, missing and renamed decisions, and before the quick check, clone or archive.
- **Infrastructure:** map Octokit's `Repository.PushedAt` into the Core model in the `IGitHubApi` implementation.
- **Cli:**
  - Add `--since` to the `backup` command with a `DateTimeOffset?` parser.
  - A bad value gives a one-line error and exit code `2`.
  - Validate the option combinations in the parser, not deep in the run, as is already done for `--account` and `--repo`.
  - The handler maps the option into the request, as the other options do.
  - `--dry-run` reports what the filter would skip.
- **Docs:** update `README.md` where it lists the options. If the change touches the harness, update `docs/ai-harness.md` too.

## Tests

- **Core.Tests:**
  - The filter at the boundary: equal to `since`, one tick before, and `PushedAt` null.
  - Orchestrator: a filtered-out repository is not marked missing.
  - Orchestrator: filtering applies after the `--account` scope.
- **Cli.Tests:** option parsing for a valid date, a full timestamp, garbage, and `--since` combined with `--repo`. Also a `TestConsole` snapshot of the summary.
- **Infrastructure.Tests:** the `PushedAt` mapping, with a stubbed API response and no network.

## Decisions for the PRD

1. **Date format and time zone.** I'd accept ISO 8601, with a bare `yyyy-MM-dd` meaning 00:00 UTC, because GitHub's `pushed_at` is UTC. Is that right, or do you want local midnight?
2. **`--since` with `--repo`.** `--repo` skips discovery, so there is no `pushed_at` without an extra API call. I recommend rejecting the combination in the parser.
3. **Status of skipped repositories.** Should they get `Skipped`, or be left out of the summary? I recommend `Skipped`, with a reason, and no effect on the exit code. I need to check the FR-9.8 table to see whether `Skipped` already exists with a suitable meaning.
4. **Empty repositories** have a null `pushed_at`. I'd skip them when `--since` is given.
5. **Wikis.** A wiki push doesn't change the repository's `pushed_at`. I'd skip the wiki together with its repository and document that `--since` is a coarse pre-filter, not a completeness guarantee. Change detection stays with the ref snapshots.
6. **`validate` and the GUI.** I'd keep `--since` on `backup` only. Is that right?

If you agree with the recommendations, I can draft the PRD edit and a plan under `docs/plans/` for you to review.
