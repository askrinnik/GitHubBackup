# Ground truth — documentation sync for a new option and setting

`seed.patch` adds two things that no document describes:

- the `--no-color` option of the `backup` command (`CliCommandLine`): plain console output without colors;
- the `Backup:CompressionLevel` setting (`BackupOptions.CompressionLevel`, default `5`, validated by
  `BackupOptionsValidator` to lie between 0 and 9, added to `src/GitHubBackup.Cli/appsettings.json`).

The repository rule (`CLAUDE.md`, *Source of truth*; `.claude/rules/update-docs-on-code-change.md`) is that a new
option or setting is described in `docs/PRD.md` first, then in `README.md` where it describes the same thing. The
assistant sees only the patched files, not the diff, so it has to find what is new by comparing the code with the
documents. The patch must be updated when `CliCommandLine.cs`, `BackupOptions.cs`, `BackupOptionsValidator.cs` or
`src/GitHubBackup.Cli/appsettings.json` change.

| # | Item |
|---|---|
| 1 | `docs/PRD.md`, FR-9.2: a row for `--no-color` in the table of `backup` options |
| 2 | `docs/PRD.md`, §7.2: `CompressionLevel` in the `Backup` section of the `appsettings.json` example and a startup validation rule for `Backup:CompressionLevel` (0 to 9) |
| 3 | `docs/PRD.md`: a new document version in the header and a row in the change history (§14). Saying that the PRD change should have been approved before the code (PRD first) is welcome but not required |
| 4 | `README.md`: a row for `--no-color` in the command table of the console application section (and the option list in the status line) |

## Scoring

Max score: 4

- **Score** = items satisfied. A partly satisfied item counts 0.5: the right document is named without the right
  place (for example "update the PRD" without FR-9.2 for item 1 or without §7.2 for item 2, or "update the README"
  without the command table for item 4). One statement that clearly covers two items (for example "PRD FR-9.2 and
  §7.2") counts for both.
- **False positives** = documents named as needing an update that have no real reason to change: `CLAUDE.md`,
  `docs/ai/README.md`, `.claude/rules/*`, existing plans in `docs/plans/`, issue and pull request templates,
  `.ai/benchmarks/**`; and false statements about the documents, for example that `README.md` lists the
  `appsettings.json` keys one by one, or that the PRD already describes `--no-color` or `CompressionLevel`. Real
  extra findings (the setting is not used by `SevenZipArchiver` yet, the validator rule has no test, the option has
  no effect yet) are not false positives.
