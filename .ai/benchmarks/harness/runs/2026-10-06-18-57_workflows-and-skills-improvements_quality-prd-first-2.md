`--since` is not in the PRD. FR-9.2 lists only `--account`, `--repo`, `--dry-run`, `--silent` and `--config`. Under the project rules, the PRD changes first, and no code is written until you approve it.

`src/GitHubBackup.Cli/Program.cs` is currently just `return 0;`, so the code changes below are a plan against the PRD's planned architecture. They are not edits to existing code. I edited nothing.

## Step 1: PRD change (Russian, per NFR-7)
- Bump `Версия документа` from 0.4, set `Дата`, and add a row to the change history.
- **FR-9.2:** add a row `--since <date>`: "Only repositories whose last push (`pushed_at`) is on or after the date". Also state that `--since` is a processing filter that does not affect discovery.
- **FR-2.7:** state that discovery still runs in full with `--since`.
- **New requirement** (next free FR id in the section): `--since` filters which repositories are processed, and the decision depends on `pushed_at`. Exactly how this works depends on the answers to the questions below.
- `README.md` gets the option wherever it lists `backup` options.

## Step 2: Code, after approval
**Core** (`net10.0`, no I/O)
- Add `PushedAt` (`DateTimeOffset?`) to the discovered-repository model from FR-2.1.
- Add a small pure filter, for example `RunFilter.Matches(repo)`, that returns true when `PushedAt >= since`. The date is a value passed in, never `DateTime.Now`.
- In the orchestrator, apply the filter after discovery and the FR-2.4/FR-2.5 decisions, and before the update/archive step. If it ran earlier, repositories filtered out by `--since` would look missing and be wrongly marked `Unavailable`, which also breaks FR-2.4's rule that nothing is deleted.
- A repository skipped by the filter gets an explicit status in the summary. Adding a status is itself a PRD change (FR-9.8).

**Infrastructure**
- Map Octokit's `Repository.PushedAt` into the Core model in the `IGitHubApi` implementation.

**Cli**
- Add `--since` to the `backup` command with a `DateTimeOffset?` parser. Invalid input gives a one-line English error and exit code `2` (FR-9.10).
- Reject `--since` with `--repo` in the parser, the same way `--account` and `--repo` are handled, if you agree to my recommendation below.
- Map the option to the run request. The handler stays a thin class that calls the shared core (FR-12.7).
- `--since` combines with `--account`, `--dry-run` and `--silent` without special cases.

**Tests**
- `Core.Tests`: filter boundaries (equal, one tick before, null `PushedAt`), and an orchestrator test showing a filtered-out repository is not marked `Unavailable`.
- `Cli.Tests`: parsing (valid date, full timestamp, garbage, `--since` with `--repo`) and a `TestConsole` snapshot of the skipped summary.
- `Infrastructure.Tests`: the `pushed_at` mapping, with a local fake and no network.

## Questions to settle before I touch the PRD
1. **Date format.** I suggest ISO 8601, with a bare `yyyy-MM-dd` meaning 00:00 UTC, since `pushed_at` is UTC. Is that right, or should a bare date be local midnight?
2. **`--repo` with `--since`.** `--repo` skips discovery, so there is no `pushed_at` without an extra API call. I recommend rejecting the combination. Do you agree?
3. **Repositories with no `pushed_at`** (empty repositories). I suggest skipping them. Do you agree?
4. **Wikis.** Pushes to a wiki don't change the repository's `pushed_at`. I suggest that a repository filtered out by `--since` also skips its wiki, and that the PRD documents this as a limitation. Do you agree?
5. **Change detection.** FR-4.3 compares ref snapshots, so `--since` is only a coarse pre-filter. For example, it would miss a change that doesn't move `pushed_at`. Should the PRD call it an optimisation and not a completeness guarantee?
6. **`validate`.** I assume `--since` applies to `backup` only. Is that right?
7. **Unavailable repositories.** I'd keep them skipped as they are today. Is that right?

Once you answer, I'll draft the PRD edit and wait for your approval before any code, a branch or an issue update.
