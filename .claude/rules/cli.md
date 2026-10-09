---
paths:
  - "src/GitHubBackup.Cli/**"
  - "src/GitHubBackup.Cli.Tests/**"
---

# Console application

## Structure

- `GitHubBackup.Cli` is a host: argument parsing (`System.CommandLine`), console interaction (`Spectre.Console`), the composition root on the .NET Generic Host. Backup logic is reached through the shared services, never reimplemented here (FR-12.7).
- Commands and options are exactly those of FR-9.1 and FR-9.2. A new command, option or changed default is a PRD change first.
- Each command handler is a small class resolved from DI; it maps parsed options to a request, calls the core and maps the result to an exit code.
- Standard .NET configuration keys on the command line (`--GitHub:Token=…`) keep working: do not let `System.CommandLine` reject unknown `--Section:Key=value` tokens.

## Exit codes and modes

- Exit codes come from the single domain definition (FR-9.10): `2` critical, `1` some repositories failed, `3` discovery events without failures, `0` success — first matching wins.
- `--silent` never asks a question and never waits for input; every decision goes through the automatic decision strategy (FR-9.4).
- Interactive mode requires an interactive console: with redirected input and no `--silent`, exit with `2` and a hint (FR-9.6). Check `Console.IsInputRedirected`, not the presence of a terminal emulator.
- `--dry-run` performs discovery and the quick check only and changes nothing on disk: no clone, fetch, archive, config or state write, no questions (FR-9.5).
- `--account` and `--repo` are mutually exclusive; validate it in the parser, not deep in the run.
- Ctrl+C cancels the run's `CancellationToken`; the process exits after the current operation is stopped and temporary files are removed (NFR-4).

## Output

- Questions, tables and the summary use `Spectre.Console` through an injected `IAnsiConsole`, so tests can use `TestConsole` and assert the output against an expected string written in the test.
- The console shows results; diagnostics go to the log. Do not print stack traces to the console — print a one-line error and the log path.
- Secrets are never echoed: `set-token` reads the token with hidden input and never accepts it as an argument (FR-9.1).
- Messages are English (NFR-7) and stable enough to snapshot: no timestamps or absolute temp paths in snapshot-tested output unless scrubbed.
