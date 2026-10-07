---
name: debug-issue
description: Reproduce and diagnose a defect in GitHubBackup systematically — reproduce with a failing test or a CLI run against local repositories, read the logs, find the root cause in the right layer, and validate the fix. Use when something behaves wrongly, a test fails unexpectedly, or a backup run reports an error.
---

# Debug Issue

Diagnose a defect systematically and validate the fix with the existing test projects and commands.

Ask for the failing behaviour, the command or steps, and the log excerpt if they are not already clear.

## Procedure

1. **Reproduce before fixing.** In order of preference:
   - a failing unit test in the layer that owns the decision;
   - a failing integration test on a local bare repository in a temp folder (git and 7-Zip for real, no network);
   - a run of the CLI against a temp folder with `file://` remotes, capturing the exit code, the summary and the log.
   If it cannot be reproduced as described, report what was tried and ask before going further.
2. **Read the evidence.** The structured log (`logs\githubbackup-YYYYMMDD.json`) has `RunId`, `Repository`, `Operation`, and one event per line of git/7-Zip output with `Tool` and `Stream`; filter it instead of reading the whole file. Remember git writes normal progress to stderr — the exit code decides failure.
3. **Find the root cause, not the symptom.** Name the type and method where the wrong decision is made, and why it is wrong against the PRD requirement.
4. **Fix minimally** in that layer, keeping the architecture and conventions; no unrelated cleanup.
5. **Validate:** the reproducing test now passes, every existing test still passes, the build is clean.
6. **Record** the root cause and the fix in a form ready for the `github-issue` skill.

Never "fix" a failure by weakening a test, deleting an assertion, or swallowing an exception.
