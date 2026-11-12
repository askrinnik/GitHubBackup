# Harness report: 2026-11-12-22-11_baseline-before-moving-the-issue-workflo

**Overall: worse in bench-base, quality-prd-first** (see Summary for what moved and why).

- **Note:** Baseline before moving the issue workflow to the issue-kit plugin
- **Setup:** commit 9e3d625d, uncommitted changes at start: no; Claude Code 2.1.319, model Sonnet 5.5, effort medium, harness 3, MCP servers: none.
- **Fixtures** (one test each): bench-base, bench-cs, bench-test, quality-prd-first, quality-security, quality-service-convention.
- **Compared with:** the most recent earlier run, `2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu` (commit 4f9c203c); each fixture is compared with the previous run that contains it.

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
| bench-base (1) | 38341 (Δ +192) | 38341 (Δ +192) | 0 (Δ 0) | 1 | 6 | 28378 | all built-in<br>(15 tools, 18914 tokens) | none | unchanged |
| bench-cs (1) | 9856 (Δ +16) | 15596 (Δ +26) | 5740 (Δ +10) | 2 | 90 | 19011 | Read | csharp=2797<br>security=1085 | unchanged |
| bench-test (1) | 9866 (Δ +16) | 16009 (Δ +16) | 6143 (Δ 0) | 2 | 104 | 18019 | Read | csharp=2797<br>tests=1351 | unchanged |
| quality-prd-first (3) | 11433 (Δ +1629) | 38406 (Δ +13017) | 26973 (25168-29264) (Δ +11388) | 6.67 (6-8) | 5541 (4833-6460) | 79704 (74174-87987) | Read<br>Grep<br>Glob | cli=614<br>csharp=2797<br>docs=451<br>security=1085<br>update-docs-on-code-change=451 | unchanged |
| quality-security (3) | 11664 (Δ +10) | 45817 (Δ +6496) | 34153 (23618-40350) (Δ +6486) | 3.33 (2-4) | 11488 (10167-13376) | 110569 (90819-127933) | Read<br>Grep<br>Glob | csharp=2797<br>external-processes=971<br>security=1085<br>tests=1351 | +tests (1351) |
| quality-service-convention (3) | 11494 (Δ +16) | 55736 (Δ -4063) | 44242 (42182-45733) (Δ -4079) | 5 (5-5) | 10301 (7634-11880) | 122496 (111986-129843) | Read<br>Grep<br>Glob | csharp=2797<br>docs=451<br>external-processes=971<br>security=1085<br>tests=1351<br>update-docs-on-code-change=451 | unchanged |

<details><summary>How the cost units were calculated</summary>

Tokens by kind (averages over the repeats) multiplied by their weight. Weights: new input 1, cache read 0.1, cache write 1.25, output 5 per token.

| Fixture | New input (x 1) | Cache read (x 0.1) | Cache write (x 1.25) | Output (x 5) | Cost units (recomputed) | Cost units (stored) |
|---|---|---|---|---|---|---|
| bench-base | 2 | 17024 | 21315 | 6 | 28378 | 28378 |
| bench-cs | 6 | 11524 | 13922 | 90 | 19011 | 19011 |
| bench-test | 6 | 12907 | 12962 | 104 | 18019 | 18019 |
| quality-prd-first | 17.33 | 148474 | 29709 | 5541 | 79704 | 79704 |
| quality-security | 9.33 | 74839 | 36507 | 11488 | 110569 | 110569 |
| quality-service-convention | 13.33 | 128033 | 46539 | 10301 | 122496 | 122496 |

Example, bench-base (first repeat): 2 x 1 + 17024 x 0.1 + 21315 x 1.25 + 6 x 5 = 28378.

</details>

## Summary

One line per fixture: what was measured, the comparison with the previous run, and the conclusion after the arrow.

- **bench-base**: **start context**: 38341 tokens in the first model call, before any work (tools, system prompt, CLAUDE.md, memory index, skill list); every later call re-reads it. Previous run (`2026-11-10-12-48_format-hook-for-edited-c-files`): 38149, so +192. Where it moved: claude.md +173; memory index +46; skill list +74; the previous run had uncommitted changes, whose file list was part of its start context and part of the unattributed difference (about 700 tokens per 30 files); this run had none. **-> tokens worse (more tokens).**
- **bench-cs**: reading one file adds 5740 tokens, which is the file plus the rules loaded for that file type (csharp, security) and the answer. Previous run: 5730 (+10); a difference of up to 50 tokens counts as unchanged. **-> tokens unchanged.**
- **bench-test**: reading one file adds 6143 tokens, which is the file plus the rules loaded for that file type (csharp, tests) and the answer. Previous run: 6143 (0); a difference of up to 50 tokens counts as unchanged. **-> tokens unchanged.**
- **quality-prd-first**: one session adds 26973 (25168-29264) tokens on average (range over 3 repeats) in 6.67 model calls on average; this covers the files the model chose to read, the rules loaded for them and its answer. Previous run (`2026-10-07-16-44_parity-with-addressbook2025-test-authori`): 15585 (13966-16434). Allowed difference: 2468 (the larger of that run's own range and 1000); actual difference: +11388. Judge: 3.5 of 4 checklist items found (previous run 3.83), 0 false positive(s). **-> tokens worse (more tokens); quality unchanged (within noise).**
- **quality-security**: one session adds 34153 (23618-40350) tokens on average (range over 3 repeats) in 3.33 model calls on average; this covers the files the model chose to read, the rules loaded for them and its answer. Previous run: 27667 (23393-36215). Allowed difference: 12822 (the larger of that run's own range and 1000); actual difference: +6486. Rules loaded changed: +tests (1351). Judge: 7 of 7 checklist items found (previous run 7), 0 false positive(s). **-> tokens unchanged; quality unchanged (within noise).**
- **quality-service-convention**: one session adds 44242 (42182-45733) tokens on average (range over 3 repeats) in 5 model calls on average; this covers the files the model chose to read, the rules loaded for them and its answer. Previous run: 48321 (39684-58655). Allowed difference: 18971 (the larger of that run's own range and 1000); actual difference: -4079. Judge: 4.5 of 5 checklist items found (previous run 4.67), 0 false positive(s). **-> tokens unchanged; quality unchanged (within noise).**

## Quality (judged fixtures)

Look at Verdict: `within noise` means the review quality did not change. Score is the average number of checklist items the judge found, with the lowest and highest repeat; false positives are summed over the repeats.

| Fixture | Score (mean, min-max) | Maximum | False positives (previous run) | Previous mean score | Δ | Verdict |
|---|---|---|---|---|---|---|
| quality-prd-first | 3.5 (3.5-3.5) | 4 | 0 (0) | 3.83 | -0.33 | within noise |
| quality-security | 7 (7-7) | 7 | 0 (0) | 7 | 0 | within noise |
| quality-service-convention | 4.5 (4.5-4.5) | 5 | 0 (0) | 4.67 | -0.17 | within noise |

### Repeats that need a look

Repeats that scored below the maximum or raised a false positive; read the answer before repeating a judge verdict as fact.

- quality-prd-first repeat 1: 3.5/4, 0 false positive(s). Item 1: states PRD must change first and code only after approval, though it follows with a lengthy implementation plan (mostly satisfied). Item 2: names FR-9.2, version bump and change history row. Item 3: raises many decisions (date format/UTC, --repo interaction, --dry-run, summary/exit code and FR-9.8, pushed_at vs ls-remote, discovery FR-2.7). Item 4: ends by asking user to answer questions and agree before preparing the PRD text, though it presents a detailed code plan first; partial credit on item 1 and 4 is at most 0.5 total deducted.
- quality-prd-first repeat 2: 3.5/4, 0 false positive(s). Item 1 is mostly met: the answer opens with PRD-first and notes --since is absent from FR-9.2, though it cites .claude/rules/cli.md rather than the CLAUDE.md source-of-truth rule and then gives a full implementation plan (0.5 to 1). Item 2 is met: FR-9.2, version bump and change-history row. Item 3 is met: date format, --repo/--account interaction, and skipped status/summary/exit codes. Item 4 is met: it ends by asking the user to answer the questions before the PRD edit and implementation. No false positives.
- quality-prd-first repeat 3: 3.5/4, 0 false positive(s). Item 1 is only partly met: the answer opens by saying `--since` can't be CLI-only and lists PRD as 'Step 0' per project rules, but it never plainly states the PRD lacks `--since`, and it includes a full implementation plan. Item 2 is met (FR-9.2, version bump, change-history row). Item 3 is met (time zone/date format, pushed_at vs ls-remote, discovery FR-2.7, interaction with --repo/--account, Skipped/summary FR-9.7). Item 4 is met: it ends by asking for answers and for approval of the PRD change. No false positives; the proposals are marked as proposals.
- quality-service-convention repeat 1: 4.5/5, 0 false positive(s). Item 1: interface and implementation in Core via IFileSystem, orchestrator delegates; but the folder is passed as a parameter and IRepositoryPathResolver is not named (still a reasonable pass). Item 2: registered in AddGitHubBackupCore as singleton with constructor-injected interfaces. Item 3: IFileSystem only, undeletable entries logged and skipped, no time needed. Item 4: full XML docs and inheritdoc. Item 5: tests in Core.Tests with MockFileSystem covering leftovers and an undeletable file, names follow Method_Scenario_ExpectedResult. Minor uncertainty on the MockFileSystem locked-file approach and the helper; no clear false statements about the repository.
- quality-service-convention repeat 2: 4.5/5, 0 false positive(s). Items 1, 2, 3 and 4 are satisfied: the interface is in Core, the implementation goes through IFileSystem, it is a singleton in AddGitHubBackupCore, failures are logged and skipped, and the XML docs are complete. Item 1 is only partly satisfied because the folder comes from the existing call site's resolved temporaryFolder, not named as IRepositoryPathResolver. Item 5 is satisfied with MockFileSystem tests in Core.Tests, covering leftovers and an undeletable file. No clear false positives, since the existing orchestrator method it describes appears to be a real finding.
- quality-service-convention repeat 3: 4.5/5, 0 false positive(s). Item 1 is met: the interface and implementation live in Core and the host holds no clean-up logic, but the answer never says where the folder comes from (IRepositoryPathResolver.GetTemporaryArchiveFolder), so I gave it only partial credit. Items 2, 3, 4 and 5 are met: singleton registration in AddGitHubBackupCore, IFileSystem only, failed deletes logged and skipped, XML docs, and MockFileSystem tests in Core.Tests named Method_Scenario_ExpectedResult. Nothing false was claimed about the repo.

<details><summary>Every repeat with the judge's notes and a link to the answer</summary>

| Fixture | Repeat | Score | False positives | Judge notes | Answer |
|---|---|---|---|---|---|
| quality-prd-first | 1 | 3.5/4 | 0 | Item 1: states PRD must change first and code only after approval, though it follows with a lengthy implementation plan (mostly satisfied). Item 2: names FR-9.2, version bump and change history row. Item 3: raises many decisions (date format/UTC, --repo interaction, --dry-run, summary/exit code and FR-9.8, pushed_at vs ls-remote, discovery FR-2.7). Item 4: ends by asking user to answer questions and agree before preparing the PRD text, though it presents a detailed code plan first; partial credit on item 1 and 4 is at most 0.5 total deducted. | [answer](../runs/2026-11-12-22-11_baseline-before-moving-the-issue-workflo_quality-prd-first-1.md) |
| quality-prd-first | 2 | 3.5/4 | 0 | Item 1 is mostly met: the answer opens with PRD-first and notes --since is absent from FR-9.2, though it cites .claude/rules/cli.md rather than the CLAUDE.md source-of-truth rule and then gives a full implementation plan (0.5 to 1). Item 2 is met: FR-9.2, version bump and change-history row. Item 3 is met: date format, --repo/--account interaction, and skipped status/summary/exit codes. Item 4 is met: it ends by asking the user to answer the questions before the PRD edit and implementation. No false positives. | [answer](../runs/2026-11-12-22-11_baseline-before-moving-the-issue-workflo_quality-prd-first-2.md) |
| quality-prd-first | 3 | 3.5/4 | 0 | Item 1 is only partly met: the answer opens by saying `--since` can't be CLI-only and lists PRD as 'Step 0' per project rules, but it never plainly states the PRD lacks `--since`, and it includes a full implementation plan. Item 2 is met (FR-9.2, version bump, change-history row). Item 3 is met (time zone/date format, pushed_at vs ls-remote, discovery FR-2.7, interaction with --repo/--account, Skipped/summary FR-9.7). Item 4 is met: it ends by asking for answers and for approval of the PRD change. No false positives; the proposals are marked as proposals. | [answer](../runs/2026-11-12-22-11_baseline-before-moving-the-issue-workflo_quality-prd-first-3.md) |
| quality-security | 1 | 7/7 | 0 | All seven items found: GetRemoteRefsAsync token in -c argument (#2), CloneAsync token URL in args, .git/config and unmasked log (#1 covers items 1-3 combined), missing GIT_TERMINAL_PROMPT (#8), GetCloneFolder lacking validation/containment (#3), branch option injection with --upload-pack (#4), SevenZipArchiver.Validate file name (#5). No clear false positives; extra findings are plausible defense in depth. | [answer](../runs/2026-11-12-22-11_baseline-before-moving-the-issue-workflo_quality-security-1.md) |
| quality-security | 2 | 7/7 | 0 | All seven items found: #1 (item 3, token in args), #2 (item 1 in GetRemoteRefsAsync), item 2 (token URL logged unmasked) and item 3 (.git/config) combined in finding 1, GIT_TERMINAL_PROMPT (4), GetCloneFolder (5), ValidateBranch/UpdateAsync option injection (6), SevenZipArchiver.Validate (7). Extra findings (host validation, GIT_CEILING_DIRECTORIES, etc.) are plausible defense-in-depth, not clear false positives. | [answer](../runs/2026-11-12-22-11_baseline-before-moving-the-issue-workflo_quality-security-2.md) |
| quality-security | 3 | 7/7 | 0 | All seven items found: token in args in GetRemoteRefsAsync, unmasked logged authenticatedUrl, token in clone URL/.git/config, missing GIT_TERMINAL_PROMPT, GetCloneFolder lacking validation/containment, branch option injection without `--`, and SevenZipArchiver.Validate filename check. Extra findings (semicolon ceiling, URL validation, git path) are plausible defense-in-depth, and GetWikiCloneFolder was correctly described as protected. | [answer](../runs/2026-11-12-22-11_baseline-before-moving-the-issue-workflo_quality-security-3.md) |
| quality-service-convention | 1 | 4.5/5 | 0 | Item 1: interface and implementation in Core via IFileSystem, orchestrator delegates; but the folder is passed as a parameter and IRepositoryPathResolver is not named (still a reasonable pass). Item 2: registered in AddGitHubBackupCore as singleton with constructor-injected interfaces. Item 3: IFileSystem only, undeletable entries logged and skipped, no time needed. Item 4: full XML docs and inheritdoc. Item 5: tests in Core.Tests with MockFileSystem covering leftovers and an undeletable file, names follow Method_Scenario_ExpectedResult. Minor uncertainty on the MockFileSystem locked-file approach and the helper; no clear false statements about the repository. | [answer](../runs/2026-11-12-22-11_baseline-before-moving-the-issue-workflo_quality-service-convention-1.md) |
| quality-service-convention | 2 | 4.5/5 | 0 | Items 1, 2, 3 and 4 are satisfied: the interface is in Core, the implementation goes through IFileSystem, it is a singleton in AddGitHubBackupCore, failures are logged and skipped, and the XML docs are complete. Item 1 is only partly satisfied because the folder comes from the existing call site's resolved temporaryFolder, not named as IRepositoryPathResolver. Item 5 is satisfied with MockFileSystem tests in Core.Tests, covering leftovers and an undeletable file. No clear false positives, since the existing orchestrator method it describes appears to be a real finding. | [answer](../runs/2026-11-12-22-11_baseline-before-moving-the-issue-workflo_quality-service-convention-2.md) |
| quality-service-convention | 3 | 4.5/5 | 0 | Item 1 is met: the interface and implementation live in Core and the host holds no clean-up logic, but the answer never says where the folder comes from (IRepositoryPathResolver.GetTemporaryArchiveFolder), so I gave it only partial credit. Items 2, 3, 4 and 5 are met: singleton registration in AddGitHubBackupCore, IFileSystem only, failed deletes logged and skipped, XML docs, and MockFileSystem tests in Core.Tests named Method_Scenario_ExpectedResult. Nothing false was claimed about the repo. | [answer](../runs/2026-11-12-22-11_baseline-before-moving-the-issue-workflo_quality-service-convention-3.md) |

</details>

## Instruction files that differ from the most recent earlier run

Not available: the earlier run had uncommitted changes, so the exact instruction files it used are unknown.

## Start context by area (bench-base, client claude-desktop)

What the first model call consists of, so a change in start context can be traced to an area. Token sizes are estimates; the last row is the exact figure reported by the API.

<details><summary>Area table, MCP servers, skills and instruction files</summary>

| Area | Tokens (est.) | Share of start context | Δ vs previous run |
|---|---|---|---|
| Built-in tool definitions | 18914 | 49 % | 0 |
| MCP tool definitions (loaded) | 0 | 0 % | 0 |
| System prompt | 1793 | 5 % | 0 |
| CLAUDE.md | 2992 | 8 % | +173 |
| Memory index | 181 | 0 % | +46 |
| Skill list | 7709 | 20 % | +74 |
| Agent list | 2037 | 5 % | 0 |
| Deferred tool names | 44 | 0 % | -4 |
| MCP server instructions | 0 | 0 % | 0 |
| Not accounted for (reminders, environment, git status, estimate error) | 4671 | 12 % | |
| **FirstCtx (API)** | **38341** | 100% | +192 |

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

### bench-cs

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu | 4f9c203c | yes | 3 | 9840 | 15570 | 5730 |  |
| 2026-11-12-22-11_baseline-before-moving-the-issue-workflo | 9e3d625d | no | 3 | 9856 | 15596 | 5740 |  |

### bench-test

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu | 4f9c203c | yes | 3 | 9850 | 15993 | 6143 |  |
| 2026-11-12-22-11_baseline-before-moving-the-issue-workflo | 9e3d625d | no | 3 | 9866 | 16009 | 6143 |  |

### quality-prd-first

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-10-03-09-14_baseline-of-the-initial-harness | 574b07ad | no | 3 | 7981 | 14933 | 6952 | 4/4 |
| 2026-10-06-18-57_workflows-and-skills-improvements | 4d117ea2 | no | 3 | 9762 | 24971 | 15209 | 3.83/4 |
| 2026-10-07-16-44_parity-with-addressbook2025-test-authori | 41cb2300 | no | 3 | 9804 | 25389 | 15585 | 3.83/4 |
| 2026-11-12-22-11_baseline-before-moving-the-issue-workflo | 9e3d625d | no | 3 | 11433 | 38406 | 26973 | 3.5/4 |

### quality-security

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu | 4f9c203c | yes | 3 | 11654 | 39321 | 27667 | 7/7 |
| 2026-11-12-22-11_baseline-before-moving-the-issue-workflo | 9e3d625d | no | 3 | 11664 | 45817 | 34153 | 7/7 |

### quality-service-convention

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu | 4f9c203c | yes | 3 | 11478 | 59799 | 48321 | 4.67/5 |
| 2026-11-12-22-11_baseline-before-moving-the-issue-workflo | 9e3d625d | no | 3 | 11494 | 55736 | 44242 | 4.5/5 |

</details>


