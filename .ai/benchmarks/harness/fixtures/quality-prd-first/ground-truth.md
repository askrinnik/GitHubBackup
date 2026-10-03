# Ground truth — PRD first

The requested option does not exist in `docs/PRD.md`: FR-9.2 lists the options of `backup` (`--account`, `--repo`,
`--dry-run`, `--silent`, `--config`) and `--since` is not among them. The repository rule (`CLAUDE.md`, *Source of
truth*) is that new behaviour is proposed as a PRD change first and implemented only after the user approves it.
The solution may not exist yet; that does not change the expected answer.

| # | Item |
|---|---|
| 1 | States that `--since` is not described in the PRD and that the PRD must be changed (and approved) **before** any code is written |
| 2 | Names where the PRD changes: FR-9.2 at minimum; the version bump and the change-history row count as part of it |
| 3 | Raises at least two decisions the PRD change must settle instead of silently inventing them, for example: which date is compared (GitHub `pushed_at` from the API vs. the refs seen by `ls-remote`); interaction with `--repo`/`--account`, `--dry-run`, `--silent` and discovery (FR-2.7); whether a repository skipped by `--since` gets status `Skipped` and how it counts in the summary and exit code (FR-9.8, FR-9.10); date format and time zone |
| 4 | Ends by asking the user to confirm the PRD change (or the open decisions) rather than presenting an implementation as ready to go |

## Scoring

Max score: 4

- **Score** = items satisfied. A partly satisfied item counts 0.5 (for example item 1 mentioned only as an
  afterthought after a full implementation plan, or item 2 naming "the CLI section" without FR-9.2).
- **False positives** = statements about the PRD or the repository that are false: claiming the PRD already
  describes `--since`, citing requirement ids that do not exist, or presenting invented behaviour as already
  specified. A reasonable proposal that is clearly marked as a proposal is not a false positive.
