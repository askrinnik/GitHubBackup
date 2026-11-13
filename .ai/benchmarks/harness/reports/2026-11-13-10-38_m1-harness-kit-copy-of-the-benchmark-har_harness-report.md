# Harness report: 2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har

**Overall: no regressions; better in quality-prd-first.**

- **Note:** M1 harness-kit copy of the benchmark, harness unchanged
- **Setup:** commit 079b44ea, uncommitted changes at start: no; Claude Code 2.1.319, model Sonnet 5.5, effort medium, harness 3, MCP servers: none.
- **Fixtures** (one test each): bench-base, bench-cs, bench-test, quality-prd-first, quality-security, quality-service-convention.
- **Compared with:** the most recent earlier run, `2026-11-12-22-11_baseline-before-moving-the-issue-workflo` (commit 9e3d625d); each fixture is compared with the previous run that contains it.

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

| Fixture<br>(Repeats) | Start ctx (`FirstCtx`) | End ctx (`LastCtx`) | Added by task (`Read`) | Model calls | Output tokens | Cost units | Tools loaded | Rules loaded | Rules vs previous run |
|---|---|---|---|---|---|---|---|---|---|
| bench-base (1) | 38344 (Δ +3) | 38344 (Δ +3) | 0 (Δ 0) | 1 | 6 | 47960 | all built-in<br>(15 tools, 18914 tokens) | none | unchanged |
| bench-cs (1) | 9859 (Δ +3) | 15599 (Δ +3) | 5740 (Δ 0) | 2 | 90 | 19015 | Read | csharp=2797<br>security=1085 | unchanged |
| bench-test (1) | 9869 (Δ +3) | 16012 (Δ +3) | 6143 (Δ 0) | 2 | 104 | 18023 | Read | csharp=2797<br>tests=1351 | unchanged |
| quality-prd-first (3) | 11436 (Δ +3) | 32847 (Δ -5559) | 21411 (18381-23346) (Δ -5562) | 5.33 (5-6) | 4170 (3717-4981) | 60626 (49537-66476) | Read<br>Grep<br>Glob | cli=614<br>csharp=2797<br>docs=451<br>external-processes=971<br>security=1085<br>update-docs-on-code-change=451 | +external-processes (971) |
| quality-security (3) | 11667 (Δ +3) | 35252 (Δ -10565) | 23585 (23560-23618) (Δ -10568) | 2 (2-2) | 10888 (10246-11223) | 88967 (83016-95912) | Read<br>Grep<br>Glob | csharp=2797<br>external-processes=971<br>security=1085 | -tests (1351) |
| quality-service-convention (3) | 11497 (Δ +3) | 56230 (Δ +494) | 44733 (40015-47388) (Δ +491) | 5.67 (4-7) | 11848 (10024-13067) | 133707 (107853-154370) | Read<br>Grep<br>Glob | docs=451<br>external-processes=971<br>security=1085<br>update-docs-on-code-change=451 | -csharp (2797), -tests (1351) |

<details><summary>How the cost units were calculated</summary>

Tokens by kind (averages over the repeats) multiplied by their weight. Weights: new input 1, cache read 0.1, cache write 1.25, output 5 per token.

| Fixture | New input (x 1) | Cache read (x 0.1) | Cache write (x 1.25) | Output (x 5) | Cost units (recomputed) | Cost units (stored) |
|---|---|---|---|---|---|---|
| bench-base | 2 | 0 | 38342 | 6 | 47960 | 47960 |
| bench-cs | 6 | 11527 | 13925 | 90 | 19015 | 19015 |
| bench-test | 6 | 12910 | 12965 | 104 | 18023 | 18023 |
| quality-prd-first | 15.33 | 95741 | 24148 | 4170 | 60626 | 60626 |
| quality-security | 6 | 20974 | 25939 | 10888 | 88968 | 88967 |
| quality-service-convention | 14 | 156612 | 47032 | 11848 | 133707 | 133707 |

Example, bench-base (first repeat): 2 x 1 + 0 x 0.1 + 38342 x 1.25 + 6 x 5 = 47960.

</details>

## Summary

One line per fixture: what was measured, the comparison with the previous run, and the conclusion after the arrow.

- **bench-base**: **start context**: 38344 tokens in the first model call, before any work (tools, system prompt, CLAUDE.md, memory index, skill list); every later call re-reads it. Previous run: 38341 (+3); a difference of up to 50 tokens counts as unchanged. **-> tokens unchanged.**
- **bench-cs**: reading one file adds 5740 tokens, which is the file plus the rules loaded for that file type (csharp, security) and the answer. Previous run: 5740 (0); a difference of up to 50 tokens counts as unchanged. **-> tokens unchanged.**
- **bench-test**: reading one file adds 6143 tokens, which is the file plus the rules loaded for that file type (csharp, tests) and the answer. Previous run: 6143 (0); a difference of up to 50 tokens counts as unchanged. **-> tokens unchanged.**
- **quality-prd-first**: one session adds 21411 (18381-23346) tokens on average (range over 3 repeats) in 5.33 model calls on average; this covers the files the model chose to read, the rules loaded for them and its answer. Previous run: 26973 (25168-29264). Allowed difference: 4096 (the larger of that run's own range and 1000); actual difference: -5562. Rules loaded changed: +external-processes (971). Judge: 3.5 of 4 checklist items found (previous run 3.5), 0 false positive(s). **-> tokens better (fewer tokens); quality unchanged (within noise).**
- **quality-security**: one session adds 23585 (23560-23618) tokens on average (range over 3 repeats) in 2 model calls on average; this covers the files the model chose to read, the rules loaded for them and its answer. Previous run: 34153 (23618-40350). Allowed difference: 16732 (the larger of that run's own range and 1000); actual difference: -10568. Rules loaded changed: -tests (1351). Judge: 6.67 of 7 checklist items found (lowest repeat 6.5) (previous run 7), 0 false positive(s). **-> tokens unchanged; quality unchanged (within noise).**
- **quality-service-convention**: one session adds 44733 (40015-47388) tokens on average (range over 3 repeats) in 5.67 model calls on average; this covers the files the model chose to read, the rules loaded for them and its answer. Previous run: 44242 (42182-45733). Allowed difference: 3551 (the larger of that run's own range and 1000); actual difference: +491. Rules loaded changed: -csharp (2797), -tests (1351). Judge: 4.5 of 5 checklist items found (previous run 4.5), 0 false positive(s). **-> tokens unchanged; quality unchanged (within noise).**

## Quality (judged fixtures)

Look at Verdict: `within noise` means the review quality did not change. Score is the average number of checklist items the judge found, with the lowest and highest repeat; false positives are summed over the repeats.

| Fixture | Score (mean, min-max) | Maximum | False positives (previous run) | Previous mean score | Δ | Verdict |
|---|---|---|---|---|---|---|
| quality-prd-first | 3.5 (3.5-3.5) | 4 | 0 (0) | 3.5 | 0 | within noise |
| quality-security | 6.67 (6.5-7) | 7 | 0 (0) | 7 | -0.33 | within noise |
| quality-service-convention | 4.5 (4.5-4.5) | 5 | 0 (0) | 4.5 | 0 | within noise |

### Repeats that need a look

Repeats that scored below the maximum or raised a false positive; read the answer before repeating a judge verdict as fact.

- quality-prd-first repeat 1: 3.5/4, 0 false positive(s). Item 1 satisfied: states PRD must change first and wait for approval, though a full implementation plan follows. Item 2 satisfied: FR-9.2, version bump, change-history row. Item 3 satisfied: time zone, date format, --repo/--account interaction, dry-run, discovery, Skipped status. Item 4 is partial: it ends by offering to draft the PRD text and asking to confirm the answers, but presents a detailed implementation as ready.
- quality-prd-first repeat 2: 3.5/4, 0 false positive(s). Item 1: it says PRD first and approval before code, though stated after a summary of the three-layer change (counted as met). Item 2: names FR-9.2, version bump and change-history row (met). Item 3: raises several decisions (timezone, --repo interaction, pushed_at), but does not cover Skipped status/exit code or --silent (met). Item 4: it ends by offering to draft the PRD change and asks for agreement, but the full implementation plan comes first (0.5). No false statements about the PRD.
- quality-prd-first repeat 3: 3.5/4, 0 false positive(s). Item 1: states PRD lacks --since and PRD comes first, but the answer then lays out a full implementation plan (steps 2-5) after the PRD step, so this is partly an afterthought-style framing (0.5). Item 2: names FR-9.2, the version bump and the change history (1). Item 3: raises many decisions (time zone, FR-2.7, --repo, Skipped status, pushed_at semantics) (1). Item 4: ends by asking the user to confirm the open decisions before the PRD edit and code (1). No false positives found.
- quality-security repeat 2: 6.5/7, 0 false positive(s). Found #1 (header in args), #2 (unmasked URL log, though hedged), #3 (token in URL and .git/config), #5, #6, #7 (archive name not validated). #4 (GIT_TERMINAL_PROMPT) was found but hedged, so credit is full; the only partial is #2 due to hedging. No clear false positives.
- quality-security repeat 3: 6.5/7, 0 false positive(s). Found items 1, 2 (partly: logging flagged but hedged as 'verify', counted as found), 3, 4, 5, 6, 7. Item 2 is hedged but explicitly identifies unmasked logging of authenticatedUrl; item 1 and 3 fully covered. Extra findings are real defense-in-depth, not false positives; I deducted 0.5 for the hedged logging finding.
- quality-service-convention repeat 1: 4.5/5, 0 false positive(s). Item 1 met: interface and implementation in Core, no logic left in the host, and the folder is passed in by the orchestrator (the answer does not name IRepositoryPathResolver explicitly). Item 2 met: TryAddSingleton in the Core extension method with constructor-injected interfaces. Item 3 met: only IFileSystem is used, failures are logged and skipped, no time dependency. Item 4 met: interface, implementation and log class all have XML docs. Item 5 half met: tests are in Core.Tests with MockFileSystem and cover cleanup and an undeletable file, but test names such as Clean_FilesAndFolders_DeletesThemAndKeepsTheFolder follow the convention only loosely. No false statements found; the answer found the existing orchestrator method and described it accurately.
- quality-service-convention repeat 2: 4.5/5, 0 false positive(s). Item 1: the interface and implementation are in Core, but the folder comes via the orchestrator's existing temporaryFolder, so the resolver is not named explicitly (probably OK). Item 2: singleton registration in AddGitHubBackupCore with constructor-injected interfaces. Item 3: IFileSystem only, failures logged and skipped; no time is needed. Item 4: full XML docs and inheritdoc. Item 5: tests in Core.Tests with MockFileSystem and Method_Scenario_Result names, covering cleanup and an undeletable file. Minor doubt on item 1; the answer also correctly found the existing logic and the PRD.
- quality-service-convention repeat 3: 4.5/5, 0 false positive(s). Item 1: the interface and implementation are in Core, with the folder taken from IRepositoryPathResolver, and the host holds no logic. Item 2: registered as a singleton in CoreServiceCollectionExtensions with constructor injection. Item 3: IFileSystem only, and failures are logged and skipped. Item 4: XML docs on the interface and inheritdoc on the implementation. Item 5 is partial: the tests are in Core.Tests and use MockFileSystem, but the names have the form Method_Scenario_Result, not fully the expected Method_Scenario_ExpectedResult style in all cases; they do cover leftover deletion and an undeletable file. The answer says the cleanup already exists in BackupOrchestrator, which the ground truth allows, so it is not a false positive.

<details><summary>Every repeat with the judge's notes and a link to the answer</summary>

| Fixture | Repeat | Score | False positives | Judge notes | Answer |
|---|---|---|---|---|---|
| quality-prd-first | 1 | 3.5/4 | 0 | Item 1 satisfied: states PRD must change first and wait for approval, though a full implementation plan follows. Item 2 satisfied: FR-9.2, version bump, change-history row. Item 3 satisfied: time zone, date format, --repo/--account interaction, dry-run, discovery, Skipped status. Item 4 is partial: it ends by offering to draft the PRD text and asking to confirm the answers, but presents a detailed implementation as ready. | [answer](../runs/2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har_quality-prd-first-1.md) |
| quality-prd-first | 2 | 3.5/4 | 0 | Item 1: it says PRD first and approval before code, though stated after a summary of the three-layer change (counted as met). Item 2: names FR-9.2, version bump and change-history row (met). Item 3: raises several decisions (timezone, --repo interaction, pushed_at), but does not cover Skipped status/exit code or --silent (met). Item 4: it ends by offering to draft the PRD change and asks for agreement, but the full implementation plan comes first (0.5). No false statements about the PRD. | [answer](../runs/2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har_quality-prd-first-2.md) |
| quality-prd-first | 3 | 3.5/4 | 0 | Item 1: states PRD lacks --since and PRD comes first, but the answer then lays out a full implementation plan (steps 2-5) after the PRD step, so this is partly an afterthought-style framing (0.5). Item 2: names FR-9.2, the version bump and the change history (1). Item 3: raises many decisions (time zone, FR-2.7, --repo, Skipped status, pushed_at semantics) (1). Item 4: ends by asking the user to confirm the open decisions before the PRD edit and code (1). No false positives found. | [answer](../runs/2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har_quality-prd-first-3.md) |
| quality-security | 1 | 7/7 | 0 | All seven items are found: the header in process arguments, the unmasked token URL in the log, the token in the remote URL and `.git/config`, the missing GIT_TERMINAL_PROMPT, the unchecked clone folder, branch option injection into fetch, and the archive name check. The claim that the archive carries the token comes through the clone's `.git/config`, which matches item 3, so I did not count it as a false positive. | [answer](../runs/2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har_quality-security-1.md) |
| quality-security | 2 | 6.5/7 | 0 | Found #1 (header in args), #2 (unmasked URL log, though hedged), #3 (token in URL and .git/config), #5, #6, #7 (archive name not validated). #4 (GIT_TERMINAL_PROMPT) was found but hedged, so credit is full; the only partial is #2 due to hedging. No clear false positives. | [answer](../runs/2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har_quality-security-2.md) |
| quality-security | 3 | 6.5/7 | 0 | Found items 1, 2 (partly: logging flagged but hedged as 'verify', counted as found), 3, 4, 5, 6, 7. Item 2 is hedged but explicitly identifies unmasked logging of authenticatedUrl; item 1 and 3 fully covered. Extra findings are real defense-in-depth, not false positives; I deducted 0.5 for the hedged logging finding. | [answer](../runs/2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har_quality-security-3.md) |
| quality-service-convention | 1 | 4.5/5 | 0 | Item 1 met: interface and implementation in Core, no logic left in the host, and the folder is passed in by the orchestrator (the answer does not name IRepositoryPathResolver explicitly). Item 2 met: TryAddSingleton in the Core extension method with constructor-injected interfaces. Item 3 met: only IFileSystem is used, failures are logged and skipped, no time dependency. Item 4 met: interface, implementation and log class all have XML docs. Item 5 half met: tests are in Core.Tests with MockFileSystem and cover cleanup and an undeletable file, but test names such as Clean_FilesAndFolders_DeletesThemAndKeepsTheFolder follow the convention only loosely. No false statements found; the answer found the existing orchestrator method and described it accurately. | [answer](../runs/2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har_quality-service-convention-1.md) |
| quality-service-convention | 2 | 4.5/5 | 0 | Item 1: the interface and implementation are in Core, but the folder comes via the orchestrator's existing temporaryFolder, so the resolver is not named explicitly (probably OK). Item 2: singleton registration in AddGitHubBackupCore with constructor-injected interfaces. Item 3: IFileSystem only, failures logged and skipped; no time is needed. Item 4: full XML docs and inheritdoc. Item 5: tests in Core.Tests with MockFileSystem and Method_Scenario_Result names, covering cleanup and an undeletable file. Minor doubt on item 1; the answer also correctly found the existing logic and the PRD. | [answer](../runs/2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har_quality-service-convention-2.md) |
| quality-service-convention | 3 | 4.5/5 | 0 | Item 1: the interface and implementation are in Core, with the folder taken from IRepositoryPathResolver, and the host holds no logic. Item 2: registered as a singleton in CoreServiceCollectionExtensions with constructor injection. Item 3: IFileSystem only, and failures are logged and skipped. Item 4: XML docs on the interface and inheritdoc on the implementation. Item 5 is partial: the tests are in Core.Tests and use MockFileSystem, but the names have the form Method_Scenario_Result, not fully the expected Method_Scenario_ExpectedResult style in all cases; they do cover leftover deletion and an undeletable file. The answer says the cleanup already exists in BackupOrchestrator, which the ground truth allows, so it is not a false positive. | [answer](../runs/2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har_quality-service-convention-3.md) |

</details>

## Instruction files that differ from the most recent earlier run

No difference in rules, agents, skills, `CLAUDE.md` or settings between `9e3d625d` to `079b44ea`.

## Start context by area (bench-base, client claude-desktop)

What the first model call consists of, so a change in start context can be traced to an area. Token sizes are estimates; the last row is the exact figure reported by the API.

<details><summary>Area table, MCP servers, skills and instruction files</summary>

| Area | Tokens (est.) | Share of start context | Δ vs previous run |
|---|---|---|---|
| Built-in tool definitions | 18914 | 49 % | 0 |
| MCP tool definitions (loaded) | 0 | 0 % | 0 |
| System prompt | 1793 | 5 % | 0 |
| CLAUDE.md | 2992 | 8 % | 0 |
| Memory index | 181 | 0 % | 0 |
| Skill list | 7709 | 20 % | 0 |
| Agent list | 2037 | 5 % | 0 |
| Deferred tool names | 44 | 0 % | 0 |
| MCP server instructions | 0 | 0 % | 0 |
| Not accounted for (reminders, environment, git status, estimate error) | 4674 | 12 % | |
| **FirstCtx (API)** | **38344** | 100% | +3 |

Largest tool definitions: PowerShell=4541; Workflow=2344; Agent=2280; ScheduleWakeup=2099; Bash=1681; Grep=1010

### MCP servers

None: the run started its sessions without MCP servers, so this start context is what the repository itself loads.

### Skills, agents and instruction files

- Skills listed: 63, 7709 tokens. By source (count/tokens): local=8/839; other=42/4099; plugin=13/2527. Largest: dataviz=385; harness-kit=342; anthropic-skills:docs=274; anthropic-skills:pptx=262; anthropic-skills:google-workspace=260.
- Agent types listed: 13, 2037 tokens.
- Instruction files loaded at start (tokens, est.): CLAUDE.md=796; CLAUDE.md=2992; MEMORY.md=181 (CLAUDE.md 113 lines, memory index 5 lines).

</details>

## History

Start context, end context, added tokens and score of each fixture over the latest 8 runs that contain it; use it to see whether a number is stable over time.

<details><summary>History tables per fixture</summary>

### bench-base

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-10-03-09-14_baseline-of-the-initial-harness | 574b07ad | no | 3 | 42407 | 42407 | 0 |  |
| 2026-10-06-18-57_workflows-and-skills-improvements | 4d117ea2 | no | 3 | 44579 | 44579 | 0 |  |
| 2026-10-07-16-44_parity-with-addressbook2025-test-authori | 41cb2300 | no | 3 | 33794 | 33794 | 0 |  |
| 2026-11-10-12-48_format-hook-for-edited-c-files | dceca651 | yes | 3 | 38149 | 38149 | 0 |  |
| 2026-11-12-22-11_baseline-before-moving-the-issue-workflo | 9e3d625d | no | 3 | 38341 | 38341 | 0 |  |
| 2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har | 079b44ea | no | 3 | 38344 | 38344 | 0 |  |

### bench-cs

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu | 4f9c203c | yes | 3 | 9840 | 15570 | 5730 |  |
| 2026-11-12-22-11_baseline-before-moving-the-issue-workflo | 9e3d625d | no | 3 | 9856 | 15596 | 5740 |  |
| 2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har | 079b44ea | no | 3 | 9859 | 15599 | 5740 |  |

### bench-test

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu | 4f9c203c | yes | 3 | 9850 | 15993 | 6143 |  |
| 2026-11-12-22-11_baseline-before-moving-the-issue-workflo | 9e3d625d | no | 3 | 9866 | 16009 | 6143 |  |
| 2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har | 079b44ea | no | 3 | 9869 | 16012 | 6143 |  |

### quality-prd-first

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-10-03-09-14_baseline-of-the-initial-harness | 574b07ad | no | 3 | 7981 | 14933 | 6952 | 4/4 |
| 2026-10-06-18-57_workflows-and-skills-improvements | 4d117ea2 | no | 3 | 9762 | 24971 | 15209 | 3.83/4 |
| 2026-10-07-16-44_parity-with-addressbook2025-test-authori | 41cb2300 | no | 3 | 9804 | 25389 | 15585 | 3.83/4 |
| 2026-11-12-22-11_baseline-before-moving-the-issue-workflo | 9e3d625d | no | 3 | 11433 | 38406 | 26973 | 3.5/4 |
| 2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har | 079b44ea | no | 3 | 11436 | 32847 | 21411 | 3.5/4 |

### quality-security

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu | 4f9c203c | yes | 3 | 11654 | 39321 | 27667 | 7/7 |
| 2026-11-12-22-11_baseline-before-moving-the-issue-workflo | 9e3d625d | no | 3 | 11664 | 45817 | 34153 | 7/7 |
| 2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har | 079b44ea | no | 3 | 11667 | 35252 | 23585 | 6.67/7 |

### quality-service-convention

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu | 4f9c203c | yes | 3 | 11478 | 59799 | 48321 | 4.67/5 |
| 2026-11-12-22-11_baseline-before-moving-the-issue-workflo | 9e3d625d | no | 3 | 11494 | 55736 | 44242 | 4.5/5 |
| 2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har | 079b44ea | no | 3 | 11497 | 56230 | 44733 | 4.5/5 |

</details>


