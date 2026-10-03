The solution doesn't exist yet. `src/` has no `.cs` files, and #2 (F0.1) creates the projects. So this is a plan, and the PRD has to change first.

## PRD first

FR-9.2 lists the `backup` options (`--account`, `--repo`, `--dry-run`, `--silent`, `--config`), and `--since` isn't among them. Nothing in the PRD describes filtering by push date. Per CLAUDE.md I'd propose a PRD change and wait for your approval before any code:

- Bump the version and add a row to the change history.
- Add a `--since <date>` row to the FR-9.2 table.
- Add a rule to the run flow in §9 and to FR-9.5 (dry-run), and a new status in FR-9.8 if you want one.

## Questions for you

These decide the PRD wording, and I don't want to assume answers:

1. **Date semantics.** Should `--since` accept `yyyy-MM-dd` only, or also a full timestamp? Is a bare date interpreted as UTC or local midnight? GitHub's `pushed_at` is UTC. I'd suggest ISO 8601, with a bare date meaning 00:00 UTC. An unparsable date would be a validation error with exit code `2`.
2. **Skipped repositories.** Do they appear in the FR-9.7 summary as a new status such as `Skipped`, or are they omitted from it? I'd suggest a `Skipped` status that is counted in the totals but doesn't affect the exit code.
3. **`--repo` combined with `--since`.** `--repo` has no discovery step, so I'd have to fetch the repository's metadata just to check the date. Should the two options be mutually exclusive, like `--account` and `--repo`, or should the filter still apply?
4. **Wiki.** `pushed_at` doesn't reflect pushes to the `.wiki.git` repository. I'd suggest that a repository filtered out by `--since` also skips its wiki, and that the PRD says so.

## Proposed implementation (after PRD approval)

**Core**
- Add `DateTimeOffset? Since` to the backup request or options model that `BackupOrchestrator` receives.
- Make sure the discovered repository model carries `PushedAt`. It comes from Octokit's `Repository.PushedAt`, so it also needs mapping in the Infrastructure GitHub client.
- Add a small pure filter, for example `IRepositoryFilter` or a static helper. It keeps a repository when `PushedAt >= since`, and when `PushedAt` is null (empty repository) it excludes it. It takes the date as a value and never calls `DateTime.Now`.
- In `BackupOrchestrator`, apply the filter **after** discovery and after the FR-2.5/FR-2.6 decisions about new, missing and renamed repositories, and **before** the clone/update/archive step. Discovery must see the full list. Otherwise every repository filtered out by `--since` would be wrongly marked `Unavailable` in silent mode.
- In dry-run, show filtered-out repositories as "would skip: last push before <date>".

**Cli**
- Add `--since` to the `backup` command with a `DateTimeOffset?` parser. It reports a clear validation error, exit code `2`, on bad input.
- Pass the value into the request.
- Add the new status to the summary table, if you choose that.

**Tests**
- `Core.Tests`: filter boundary cases (equal, one tick before, null `PushedAt`) and an orchestrator test that confirms missing-repository detection ignores the filter.
- `Cli.Tests`: option parsing (valid date, timestamp, garbage, `--since` with `--repo`).
- `Infrastructure.Tests` (WireMock): `pushed_at` is mapped correctly.

## Risk worth knowing

Change detection in this app compares ref snapshots (FR-4.3, step 4 around line 246). `--since` is only a coarse pre-filter, so a repository with a change that doesn't touch `pushed_at` would be skipped, for example a wiki-only edit. That's acceptable if the PRD says `--since` is an optimization filter and not a completeness guarantee.

Once you've answered the four questions, I can draft the PRD change. It would be in Russian, per NFR-7.
