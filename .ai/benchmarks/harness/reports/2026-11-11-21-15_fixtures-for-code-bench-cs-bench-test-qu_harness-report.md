# Harness report: 2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu

**Overall: no regressions.** 4 of 4 fixtures were compared with `2026-11-10-12-48_format-hook-for-edited-c-files` and none changed beyond the noise.

- **Note:** Fixtures for code: bench-cs, bench-test, quality-service-convention, quality-security
- **Setup:** commit 4f9c203c, uncommitted changes at start: yes; Claude Code 2.1.319, model Sonnet 5.5, effort medium, harness 3, MCP servers: none.
- **Fixtures** (one test each): bench-cs, bench-test, quality-security, quality-service-convention.
- **Compared with:** the most recent earlier run, `2026-11-10-12-48_format-hook-for-edited-c-files` (commit dceca651); each fixture is compared with the previous run that contains it.

## How to read this report

Every fixture is one scripted Claude Code session, or several repeats of it. All token numbers come from the session transcript. The columns of the first table are explained below from left to right, one paragraph per column; the terms of the Quality table and the `~` mark follow.

**Fixture (Repeats).** The name of the test, and in brackets how many times its session was run. `bench-*` fixtures measure cost (what a session loads and what reading a file adds); `quality-*` fixtures also have a judge that scores the answer. Where the number of repeats is more than 1, the other numbers are averages, shown as "mean (min-max)": the average followed by the lowest and the highest repeat.

**Start ctx (`FirstCtx`).** Tokens in the very first model call, before any work: tool definitions, system prompt, CLAUDE.md, memory index, skill and agent lists. Every later call of the session re-reads it. It depends on the tools loaded, so compare it only between runs of the same fixture. The bracket `(Δ +25)` is the change against the previous run of this fixture; up to 50 tokens counts as unchanged.

**End ctx (`LastCtx`).** Tokens in the last model call: the start context plus everything the task added. The bracket is the change against the previous run.

**Added by task (`Read`).** End ctx minus Start ctx: the tokens the task put into the context, that is the file or files read, the rules loaded for them and the model's answer. This is the number that shows what the instructions cost per task. The bracket is the change against the previous run; for fixtures with repeats, a change is noise when it is at most the larger of the previous run's own range and 1000 tokens ("within the spread").

**Model calls.** How many requests the session sent to the model. Each call re-reads the whole context, so more calls cost more.

**Output tokens.** Tokens the model wrote (answers and tool calls).

**Cost units.** The price of the session in relative units, where one new input token costs 1. Output is dearer and cached input is cheaper, so the session's tokens are counted by kind and multiplied by a weight: `cost units = new input x 1 + cache read x 0.1 + cache write x 1.25 + output x 5`. *New input* is context that was not cached, *cache read* is context taken from the prompt cache (the whole context is re-read on every model call), *cache write* is context stored into the cache the first time, *output* is what the model wrote. The sums are over all model calls of the session. Use it to compare sessions by cost, not by size. The token counts and the weights of every session are stored in `run-history.csv` (`InputTok`, `CacheReadTok`, `CacheWriteTok`, `OutTok`, `WInput`, `WCacheRead`, `WCacheWrite`, `WOutput`), so the value can be recomputed from the row; the breakdown is under the context table. The weights are relative prices assumed by the skill, not amounts of money.

**Tools loaded.** The tools the session was given. A fixture that sets none gets every built-in tool, and their definitions (about 17000 tokens) are part of its start context; fixtures limited to `Read` or a few tools carry almost none, which is why their start context is much smaller.

**Rules loaded.** Instruction files pulled in when a file of a matching type is read, as name=tokens. A change here is a real change to the instructions. A "Rules vs previous run" column appears only when some fixture's rules changed.

**Quality table.** *Score* is the number of checklist items the judge found, with the lowest and highest repeat, out of *Maximum*. *False positives* are findings the judge rejected as not real problems, summed over the repeats (previous run in brackets). The *Verdict* is `within noise` (no real change), `REGRESSION` (the mean score fell by more than the larger of the previous run's range and 0.5, or there are two or more extra false positives), `improvement` (the same, upward) or `no previous run`. One extra false positive is only noted, because the judge varies by that much.

**`~` after a number.** MCP servers were not ready when the session started, so the number can be about 1000 tokens off (see Caveats).

## Context and cost per fixture

The value in brackets after a number, (Δ ...), is the change against the previous run (0 or a few tens of tokens is noise). Values are averages over the repeats; "mean (min-max)" is shown for fixtures that ran more than once. Start context depends on the tools loaded (see the Tools loaded column), so compare it only between runs of the same fixture, never between fixtures with different tools.

| Fixture<br>(Repeats) | Start ctx (`FirstCtx`) | End ctx (`LastCtx`) | Added by task (`Read`) | Model calls | Output tokens | Cost units | Tools loaded | Rules loaded |
|---|---|---|---|---|---|---|---|---|
| bench-cs (1) | 9840 | 15570 | 5730 | 2 | 93 | 18992 | Read | csharp=2797<br>security=1085 |
| bench-test (1) | 9850 | 15993 | 6143 | 2 | 101 | 17983 | Read | csharp=2797<br>tests=1351 |
| quality-security (3) | 11654 | 39321 | 27667 (23393-36215) | 2.67 (2-4) | 10981 (9277-12830) | 79871 (51060-129698) | Read<br>Grep<br>Glob | csharp=2797<br>external-processes=971<br>security=1085 |
| quality-service-convention (3) | 11478 | 59799 | 48321 (39684-58655) | 7 (6-8) | 13634 (11335-16003) | 152630 (132175-176683) | Read<br>Grep<br>Glob | csharp=2797<br>docs=451<br>external-processes=971<br>security=1085<br>tests=1351<br>update-docs-on-code-change=451 |

<details><summary>How the cost units were calculated</summary>

Tokens by kind (averages over the repeats) multiplied by their weight. Weights: new input 1, cache read 0.1, cache write 1.25, output 5 per token.

| Fixture | New input (x 1) | Cache read (x 0.1) | Cache write (x 1.25) | Output (x 5) | Cost units (recomputed) | Cost units (stored) |
|---|---|---|---|---|---|---|
| bench-cs | 6 | 11508 | 13896 | 93 | 18992 | 18992 |
| bench-test | 6 | 12891 | 12946 | 101 | 17983 | 17983 |
| quality-security | 7.33 | 63572 | 14881 | 10981 | 79871 | 79871 |
| quality-service-convention | 18 | 211719 | 50615 | 13634 | 152630 | 152630 |

Example, bench-cs (first repeat): 6 x 1 + 11508 x 0.1 + 13896 x 1.25 + 93 x 5 = 18992.

</details>

## Summary

One line per fixture: what was measured, the comparison with the previous run, and the conclusion after the arrow.

- **bench-cs**: reading one file adds 5730 tokens, which is the file plus the rules loaded for that file type (csharp, security) and the answer. No earlier run to compare with.
- **bench-test**: reading one file adds 6143 tokens, which is the file plus the rules loaded for that file type (csharp, tests) and the answer. No earlier run to compare with.
- **quality-security**: one session adds 27667 (23393-36215) tokens on average (range over 3 repeats) in 2.67 model calls on average; this covers the files the model chose to read, the rules loaded for them and its answer. No earlier run to compare with. Judge: 7 of 7 checklist items found, 0 false positive(s).
- **quality-service-convention**: one session adds 48321 (39684-58655) tokens on average (range over 3 repeats) in 7 model calls on average; this covers the files the model chose to read, the rules loaded for them and its answer. No earlier run to compare with. Judge: 4.67 of 5 checklist items found (lowest repeat 4.5), 0 false positive(s).

## Quality (judged fixtures)

Look at Verdict: `within noise` means the review quality did not change. Score is the average number of checklist items the judge found, with the lowest and highest repeat; false positives are summed over the repeats.

| Fixture | Score (mean, min-max) | Maximum | False positives (previous run) | Previous mean score | Δ | Verdict |
|---|---|---|---|---|---|---|
| quality-security | 7 (7-7) | 7 | 0 | | | no previous run |
| quality-service-convention | 4.67 (4.5-5) | 5 | 0 | | | no previous run |

### Repeats that need a look

Repeats that scored below the maximum or raised a false positive; read the answer before repeating a judge verdict as fact.

- quality-service-convention repeat 1: 4.5/5, 0 false positive(s). Item 1 is met only partly: the interface and implementation are in Core and use IFileSystem, but the answer passes the folder in as a string and never names IRepositoryPathResolver.GetTemporaryArchiveFolder. Items 2–5 are met: singleton registration in AddGitHubBackupCore with constructor-injected interfaces, IFileSystem-only disk access with undeletable files logged and skipped, XML docs on the interface and implementation, and Core.Tests with MockFileSystem and Method_Scenario_ExpectedResult names. No false statements about the repository were found.
- quality-service-convention repeat 2: 4.5/5, 0 false positive(s). Item 1: interface and implementation in Core, host unchanged, folder from pathResolver.GetTemporaryArchiveFolder (met). Item 2: singleton registered in AddGitHubBackupCore with constructor-injected interfaces (met). Item 3: IFileSystem only, no TimeProvider needed, undeletable files logged and skipped (met). Item 4: interface, members and implementation carry XML docs with inheritdoc (met). Item 5: tests are in the correct Core.Tests project with MockFileSystem and cover leftover clean-up and an undeletable file, but test names such as Clean_FolderMissing_DoesNothing follow Method_Scenario_ExpectedResult only loosely, so item 5 is scored half. No false statements about the repository found.

<details><summary>Every repeat with the judge's notes and a link to the answer</summary>

| Fixture | Repeat | Score | False positives | Judge notes | Answer |
|---|---|---|---|---|---|
| quality-security | 1 | 7/7 | 0 | All seven items found: #1 (ls-remote header in args), #2 (authenticatedUrl logged unmasked), #3 (token URL in .git/config and process args), #4 (GIT_TERMINAL_PROMPT missing), #5 (GetCloneFolder lacks EnsureValidNames/ContainedFolder), #6 (branch option injection in fetch without --), #7 (SevenZipArchiver FileName not validated). No clear false positives; extra findings are plausible defense-in-depth. | [answer](../runs/2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu_quality-security-1.md) |
| quality-security | 2 | 7/7 | 0 | All seven items found: #1 (token header in args), #2 and #3 (token URL logged and stored in .git/config, in finding 1), #4 (missing GIT_TERMINAL_PROMPT), #5 (GetCloneFolder without validation/containment), #6 (branch without -- in fetch), #7 (archive FileName path). No clear false positives; extra findings are plausible defense in depth. | [answer](../runs/2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu_quality-security-2.md) |
| quality-security | 3 | 7/7 | 0 | All seven items found: #1 (GetRemoteRefs -c header), #2+#3 (clone URL with token in args, .git/config and log), #4 (GIT_TERMINAL_PROMPT missing), #5 (GetCloneFolder no validation/containment), #6 (branch option injection in fetch), #7 (Archiver.Validate filename). Extra findings are plausible defense-in-depth, not clear false positives. | [answer](../runs/2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu_quality-security-3.md) |
| quality-service-convention | 1 | 4.5/5 | 0 | Item 1 is met only partly: the interface and implementation are in Core and use IFileSystem, but the answer passes the folder in as a string and never names IRepositoryPathResolver.GetTemporaryArchiveFolder. Items 2–5 are met: singleton registration in AddGitHubBackupCore with constructor-injected interfaces, IFileSystem-only disk access with undeletable files logged and skipped, XML docs on the interface and implementation, and Core.Tests with MockFileSystem and Method_Scenario_ExpectedResult names. No false statements about the repository were found. | [answer](../runs/2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu_quality-service-convention-1.md) |
| quality-service-convention | 2 | 4.5/5 | 0 | Item 1: interface and implementation in Core, host unchanged, folder from pathResolver.GetTemporaryArchiveFolder (met). Item 2: singleton registered in AddGitHubBackupCore with constructor-injected interfaces (met). Item 3: IFileSystem only, no TimeProvider needed, undeletable files logged and skipped (met). Item 4: interface, members and implementation carry XML docs with inheritdoc (met). Item 5: tests are in the correct Core.Tests project with MockFileSystem and cover leftover clean-up and an undeletable file, but test names such as Clean_FolderMissing_DoesNothing follow Method_Scenario_ExpectedResult only loosely, so item 5 is scored half. No false statements about the repository found. | [answer](../runs/2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu_quality-service-convention-2.md) |
| quality-service-convention | 3 | 5/5 | 0 | All five items are met. The interface sits in Core with a Core implementation that reaches the disk through IFileSystem and takes the folder from IRepositoryPathResolver.GetTemporaryArchiveFolder, with no logic in the host. It is registered as a singleton in AddGitHubBackupCore with constructor-injected interfaces. Files that cannot be deleted are logged and skipped. XML docs are on the interface and the implementation uses inheritdoc. Tests are in Core.Tests with MockFileSystem and cover leftover clean-up and an undeletable file, with Method_Scenario_ExpectedResult names. No clear false positives. | [answer](../runs/2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu_quality-service-convention-3.md) |

</details>

## Instruction files that differ from the most recent earlier run

Not available: the earlier run had uncommitted changes, so the exact instruction files it used are unknown.

## History

Start context, end context, added tokens and score of each fixture over the latest 8 runs that contain it; use it to see whether a number is stable over time.

<details><summary>History tables per fixture</summary>

### bench-cs

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu | 4f9c203c | yes | 3 | 9840 | 15570 | 5730 |  |

### bench-test

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu | 4f9c203c | yes | 3 | 9850 | 15993 | 6143 |  |

### quality-security

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu | 4f9c203c | yes | 3 | 11654 | 39321 | 27667 | 7/7 |

### quality-service-convention

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu | 4f9c203c | yes | 3 | 11478 | 59799 | 48321 | 4.67/5 |

</details>

## Caveats

- The run had uncommitted changes: their file list is part of the start context and the commit id alone does not identify the instructions that were tested. Applies to: all fixtures.

