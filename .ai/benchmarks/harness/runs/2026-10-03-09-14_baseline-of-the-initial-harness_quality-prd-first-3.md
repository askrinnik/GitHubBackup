`--since` isn't in the PRD, so under the project rules I'd change the PRD first and write no code until you approve it. No `src/` exists yet either, because the solution is created by #2 (F0.1). The code changes below are therefore a plan against the PRD's planned architecture, not against existing files.

## 1. PRD change (`docs/PRD.md`, in Russian, version 0.4 → 0.5)

- Bump the version and date in the header, and add a row to the change history.
- Add a `--since <date>` row to the FR-9.2 table. It would read: "Only repositories whose last push (`pushed_at`) is on or after the date".
- Add a new requirement in §6.9 with the next free id after FR-9.x (I'd need to check the last id). It would define:
  - the accepted formats: `yyyy-MM-dd` (start of that day) and a full ISO 8601 timestamp;
  - the time zone for a date without an offset;
  - that the comparison is inclusive, against GitHub's `pushed_at`;
  - what happens to filtered-out repositories (see the questions below).
- Add the interaction with `--repo` and `--account` to the new requirement, and update the FR-2.7 wording if discovery is affected.
- Add the new option to the CLI-argument parsing line in the unit-test table.

## 2. Implementation, once approved

- **Core**
  - Add `DateTimeOffset? Since` to the run filter model that already carries `--account` and `--repo`.
  - Add `PushedAt` to the repository metadata returned by discovery.
  - Add a pure predicate, for example `RunFilter.Matches(repo)`, that returns true when `PushedAt >= Since`.
  - Make the orchestrator apply it before `ls-remote` and clone, and report excluded repositories with the status chosen below.
- **Infrastructure**
  - Map Octokit's `Repository.PushedAt` into the Core metadata type.
  - No new I/O is needed. The data is already in the repository list response, so filtering costs no extra API calls.
- **Cli**
  - Add the `--since` option to the `backup` command with a parser that validates the date and returns a clear error.
  - A bad value would exit with code `2`, to be confirmed against FR-9.10.
  - Pass the value into the filter.
- **Tests**
  - Core: the predicate at the boundary (equal, one tick before), a missing `PushedAt`, and the interaction with `--account`.
  - Cli: date parsing (valid, invalid, time zone) and the exit code.
  - Integration: none needed, since the filter sits before any git work.
  - `--dry-run` should show the filtered repositories.
- **Docs and issue**: update the plan in `docs/plans/` and the issue after the PRD is approved.

## 3. Questions to settle before editing the PRD

1. **Status of excluded repositories.** Should they appear in the summary as `Skipped`, which would need a reason, or be omitted? A new status would also change FR-9.8.
2. **Repositories not yet cloned.** If a repository has never been cloned and its last push is before the date, should it be skipped? A strict filter skips it.
3. **Wiki-only changes.** `pushed_at` doesn't reflect wiki pushes, so a wiki edited after the date could be missed. Should the PRD document this limitation, or should wikis be exempt from the filter?
4. **Combining with `--repo`.** `--repo` skips discovery, so there is no `pushed_at` without an extra API call. I'd suggest either rejecting `--since` together with `--repo`, as is already done for `--account` and `--repo`, or fetching that one repository's metadata. I recommend rejecting the combination.
5. **Time zone.** Should a date without an offset be read as local time or UTC? I'd suggest local time, with the filter logged in UTC.
6. **Discovery.** Should repositories that are new or have disappeared still go through discovery and its questions even when filtered out? I'd suggest yes, so discovery stays independent of the filter.
7. **Unavailable repositories.** Repositories with status `Unavailable` have no fresh `pushed_at`. I'd keep them skipped as today.

Tell me your answers, or accept my suggestions, and I'll draft the PRD edit for your review.
