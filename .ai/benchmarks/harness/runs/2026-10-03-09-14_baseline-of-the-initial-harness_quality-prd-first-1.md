`--since` isn't in the PRD, and no code exists yet, so the PRD has to change before any code.

## Step 1: PRD change (needs your approval first)

I'd propose PRD **0.5** (header version and date, plus a row in the change history). It would contain:

- **FR-9.2:** a new row, `--since <date>`: "Only repositories whose last push (`pushed_at` in the GitHub API) is on or after the date".
- **A new FR-9.x** that fixes the behaviour:
  - Accepted formats: `yyyy-MM-dd`, or a full ISO 8601 timestamp. A date without a time means 00:00 UTC. An unparsable value is an error with exit code `2`.
  - A date in the future is a validation error.
  - The comparison is `pushed_at >= since`.
  - Filtered-out repositories get a new item status, `Skipped`, in FR-9.8. They are shown in the summary (FR-9.7) and recorded in the run history (FR-8.3).
  - Skipped repositories are not cloned, updated, `ls-remote`-checked or archived.
- **FR-2.7:** discovery still runs as normal. `--since` filters only what gets processed, not what gets discovered.
- **FR-9.2:** `--since` is allowed together with `--account`. With `--repo` it is either rejected or has no effect (see the questions below).
- **FR-6.2:** the filter runs before the archive decision.

## Step 2: Implementation (after the PRD is approved)

1. **Core**
   - Add `PushedAt` (`DateTimeOffset?`) to the discovered-repository model that FR-2.1 builds.
   - Add `Since` (`DateTimeOffset?`) to the run options record next to `Account`, `Repo`, `DryRun` and `Silent`.
   - Add a `Skipped` value to the item status enum.
   - Add a pure `RepositoryFilter` / `SinceFilter` that selects items from the run's repository list. It has no I/O, and `TimeProvider` is injected only if "future date" is validated here.
   - Call it in the backup orchestrator before the per-repository pipeline. Skipped items are reported as `Skipped` and don't enter the pipeline.
2. **Infrastructure**
   - Octokit already returns `Repository.PushedAt`. I'd map it into the Core model in the GitHub repository source.
   - Persist the skipped status in the EF Core history (FR-8.3). This may need a migration if the status is stored as a string or enum column.
3. **Cli**
   - Add `--since` to the `backup` command with a parser and validation. Errors return exit code `2`.
   - Add a `Skipped` row to the summary and the dry-run output.
4. **Tests**, in the paired projects:
   - Core: filter boundaries (exactly equal to `since`, one tick before, null `PushedAt`) and orchestrator behaviour for skipped items.
   - Cli: parsing valid and invalid values, and the option combinations.
   - Infrastructure: the `PushedAt` mapping.
   - Integration tests use local bare repositories only, so they can't cover GitHub metadata. That part stays at the unit level with a fake source.

I'd create the issue, plan and branch the usual way, with `/implement-issue`, once the PRD is approved.

## Questions to settle before the PRD edit

1. **Wiki:** pushes to a wiki don't change the repository's `pushed_at`. Should `--since` ignore wiki-only changes and skip the wiki along with its repository? I'd say yes, and document it as a limitation.
2. **`--repo` with `--since`:** error, or apply the filter anyway? I'd make it an error, because `--repo` already names exactly one repository.
3. **Repositories with no `pushed_at`** (empty repositories): skip them, or process them? I'd skip them.
4. **Date semantics:** is UTC for a bare date acceptable, or do you want local time?
5. **Config-only entries:** a repository in the settings that wasn't found during collection (FR-2.4). Should it follow the existing unavailable path, or be skipped? I'd leave FR-2.4 unchanged.
6. **`validate` command:** should it also accept `--since`? I'd say no.

Tell me your answers and whether you approve the PRD 0.5 change, and I'll draft the exact PRD text.
