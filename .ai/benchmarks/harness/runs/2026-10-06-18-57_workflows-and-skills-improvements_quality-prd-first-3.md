I haven't edited anything. Under `CLAUDE.md`, this starts as a PRD change, because `--since` isn't in FR-9.2 and a new CLI option is a PRD change first. The code is also barely started: `Program.cs` is just `return 0;`, and `Core` and `Infrastructure` have no source files. So there is no backup command to extend yet, and `--since` would depend on the issue that builds it.

## Proposed PRD change (`docs/PRD.md`, in Russian)
- Add a `--since <date>` row to the FR-9.2 table and a new requirement, FR-9.11 if that's the next free id in §6.9. It would say the following:
  - A run processes only repositories whose last push (GitHub `pushed_at`) is on or after the date.
  - Discovery still runs over everything. FR-2.4 would otherwise mark every older repository `Unavailable`, and renames would go undetected.
  - Filtered-out repositories get status `Skipped` with the reason "pushed before --since".
- Bump the version and date in the header and add a row to the change history.
- Update `README.md` where it lists `backup` options.

## Code changes, once the PRD is approved
- **Core**
  - `RepositoryInfo` (or whatever the discovery model is called) gets `DateTimeOffset? PushedAt`.
  - `BackupRequest` gets `DateTimeOffset? Since`.
  - A small pure type, for example `SinceFilter.ShouldProcess(pushedAt, since)`, holds the rule. This follows the layering rule that logic lives in Core, not in the host.
  - `BackupOrchestrator` applies it after discovery and before the per-repository work. Skipped items go into the FR-9.7 summary.
- **Infrastructure**: the Octokit-backed `IGitHubApi` maps `Repository.PushedAt` into the model.
- **Cli**
  - Add the option to the `backup` command with `System.CommandLine`.
  - Parse the date as ISO `yyyy-MM-dd` with the invariant culture, as a UTC date. Report bad input in the parser as a one-line error.
  - Map the value into `BackupRequest`.
- **Tests**
  - Core: boundary cases (equal to, before and after the date) and a `null` push date.
  - Cli: parsing, invalid dates, `--dry-run` combined with `--since`, and the summary output with `TestConsole` and Verify.
  - Infrastructure: a WireMock contract test for the `pushed_at` mapping.

## Questions to settle before the PRD edit
1. **Never-archived repositories:** should a repository with no clone or archive yet be backed up even if it was pushed before the date? I recommend yes, because skipping it would leave it with no backup at all. The alternative is to apply the date strictly.
2. **Missed changes:** `pushed_at` doesn't reflect wiki pushes. I also doubt it reflects every change FR-6.3 counts, such as branch deletion. Those changes would be missed under `--since`. I recommend documenting this in the PRD as a known limit of the option rather than working around it. Is that acceptable?
3. **Combination with `--repo`:** I recommend allowing `--since` with `--account` and rejecting it with `--repo`, since a single named repository makes the filter pointless. Does that work for you?
4. **Date format:** is date-only enough, or do you also want a full timestamp such as `2026-10-01T12:00Z`?

Once you've answered, I can draft the PRD edit for your review. The issue and plan would follow, with the dependency on the backup-command issue set in `## Зависимости` and as a GitHub "blocked by" relation.
