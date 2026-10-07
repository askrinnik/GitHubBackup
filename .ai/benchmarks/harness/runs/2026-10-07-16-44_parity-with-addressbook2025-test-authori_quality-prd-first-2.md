I haven't edited anything. Under this repo's rules the PRD changes first, then the code.

## Where things stand

- `GitHubBackup.Cli` is only a host skeleton at the moment. `CliApplication.RunAsync` builds the host, logs the start and end of the run, and returns. There is no `backup` command, no `System.CommandLine` wiring and no work-list code yet. They arrive with phase 3 ("Полный CLI").
- `--since` is not in FR-9.2, and `.claude/rules/cli.md` says a new option is a PRD change first.
- So I'd propose the PRD change, and the code would follow only once `backup` exists, or in the same issue that builds it.

## 1. PRD change (needs your approval)

- **Header and history.** Bump the version and date, and add a row to *История изменений документа*.
- **FR-9.2.** Add a row: `--since <date>` — "Только репозитории с последним push не раньше указанной даты".
- **Compatibility.** `--since` can be combined with `--account` and `--repo`. It doesn't replace them.
- **New requirement, FR-9.2a or the next free id.**
  - A repository's push date is the `pushed_at` field from the GitHub API.
  - The filter is inclusive: `pushed_at >= since`.
  - The date is parsed as ISO `yyyy-MM-dd`, or a full ISO instant with an offset.
  - A bare date means 00:00 local time, taken from `TimeProvider`.
  - A date in the future or an unparseable value is a parser error with exit code `2`.
- **§9, step 7.** Add "подходящие под `--since`" to the filter that builds the work list.
- **FR-9.5 (dry-run).** The preview shows repositories skipped because of `--since`.
- **Summary and history.** Skipped repositories are not failures and don't affect the exit code. The summary says how many were skipped by the date filter.

## 2. Code changes (once the PRD is approved)

**Core**
- Add `DateTimeOffset? Since` to the backup request or filter record that carries `--account` and `--repo`.
- Add `PushedAt` to the discovered-repository model.
- Add the date predicate to the work-list builder, next to the active/not-excluded/account/repo filters. It is pure logic with no I/O, so it is unit-testable.
- Add a skip reason, such as `NotPushedSince`, for the summary and dry-run.

**Infrastructure**
- Fill `PushedAt` in the Octokit mapping from `Repository.PushedAt`.
- No other change here.

**Cli**
- Declare `Option<DateTimeOffset?> --since` on `backup` with a custom parser that implements the rules above.
- The parser validates it, as `cli.md` says for `--account`/`--repo`.
- The handler copies the value into the request. It must not reimplement any filtering.
- The option must not interfere with `--Section:Key=value` configuration tokens.

**Tests**
- `Core.Tests`: boundary cases (equal to, one tick before, one tick after), `null` `PushedAt`, and the combination with `--account` and `--repo`.
- `Cli.Tests`: parsing of a bare date, an ISO date with an offset, a bad value and a future date. Also a dry-run snapshot that shows the skipped repositories.

**Docs**
- Update `README.md` where it lists options.
- Add a plan under `docs/plans/` for the issue.

## 3. Design choices

- **Discovery stays unfiltered.** The `--since` filter applies at step 7, after discovery. Filtering earlier would make repositories that weren't pushed to look "disappeared" and get marked `Unavailable` (FR-2.4, FR-9.4).
- **Skipped repositories skip change detection.** They skip the `ls-remote` quick check and the fetch. No state or history entry is written for them.

## Questions before I write the PRD text

1. Is `pushed_at` the right meaning of "pushed to"? Wiki pushes don't update the repository's `pushed_at`. I'd say a wiki follows its repository's date and document that.
2. Should a repository that has no clone or archive yet be backed up even if its `pushed_at` is older than the date? By the literal definition it is skipped. Alternatively, the first backup could ignore `--since`.
3. Should `--repo owner/name` together with `--since` apply the filter? FR-2.7 says `--repo` does no discovery, so there'd be no `pushed_at` and I'd need one extra API call. Or should the two options be mutually exclusive?
4. Is the date format and the local-time rule for a bare date acceptable? I'd keep it to ISO only.
5. Should I write this up as a new issue with a plan, or fold it into the issue that builds the `backup` command?
