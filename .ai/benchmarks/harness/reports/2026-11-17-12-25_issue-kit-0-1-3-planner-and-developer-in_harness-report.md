# Harness report: 2026-11-17-12-25_issue-kit-0-1-3-planner-and-developer-in

**Overall: no regressions.** 6 of 6 fixtures were compared with `2026-11-17-11-47_issue-kit-0-1-2-testextra-verifier-testa` and none changed beyond the noise.

- **Note:** issue-kit 0.1.3 planner and developer inherit MCP tools
- **Setup:** commit bc4f6924, uncommitted changes at start: no; Claude Code 2.1.319, model Sonnet 5.5, effort medium, harness 3, MCP servers: none.
- **Fixtures** (one test each): bench-base, bench-cs, bench-test, bench-xaml, quality-prd-first, quality-service-convention.
- **Compared with:** the most recent earlier run, `2026-11-17-11-47_issue-kit-0-1-2-testextra-verifier-testa` (commit c7fc276f); each fixture is compared with the previous run that contains it.

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
| bench-base (1) | 38675 (Δ +26) | 38675 (Δ +26) | 0 (Δ 0) | 1 | 6 | 26994 | all built-in<br>(15 tools, 18914 tokens) | none | unchanged |
| bench-cs (1) | 10251 (Δ +24) | 16346 (Δ +24) | 6095 (Δ 0) | 2 | 90 | 18409 | Read | csharp=2797<br>security=1338 | unchanged |
| bench-test (1) | 10261 (Δ +24) | 16404 (Δ +24) | 6143 (Δ 0) | 2 | 104 | 18552 | Read | csharp=2797<br>tests=1351 | unchanged |
| bench-xaml (1) | 10255 (Δ +24) | 14522 (Δ +24) | 4267 (Δ 0) | 2 | 96 | 16159 | Read | security=1338<br>wpf-mvvm=1280 | unchanged |
| quality-prd-first (3) | 11828 (Δ +24) | 36703 (Δ +1675) | 24875 (17051-28891) (Δ +1651) | 5.33 (5-6) | 4483 (3392-5391) | 67433 (47160-84794) | Read<br>Grep<br>Glob | cli=614<br>csharp=2797<br>docs=526<br>security=1338<br>update-docs-on-code-change=465 | unchanged |
| quality-service-convention (3) | 11889 (Δ +24) | 56414 (Δ -991) | 44525 (31599-51509) (Δ -1015) | 5.67 (5-7) | 8985 (5046-12143) | 119329 (83357-146492) | Read<br>Grep<br>Glob | csharp=2797<br>docs=526<br>security=1338<br>tests=1351 | -update-docs-on-code-change (465) |

<details><summary>How the cost units were calculated</summary>

Tokens by kind (averages over the repeats) multiplied by their weight. Weights: new input 1, cache read 0.1, cache write 1.25, output 5 per token.

| Fixture | New input (x 1) | Cache read (x 0.1) | Cache write (x 1.25) | Output (x 5) | Cost units (recomputed) | Cost units (stored) |
|---|---|---|---|---|---|---|
| bench-base | 2 | 18591 | 20082 | 6 | 26994 | 26994 |
| bench-cs | 6 | 13292 | 13299 | 90 | 18409 | 18409 |
| bench-test | 6 | 13302 | 13357 | 104 | 18552 | 18552 |
| bench-xaml | 6 | 13296 | 11475 | 96 | 16159 | 16159 |
| quality-prd-first | 13.33 | 108965 | 27285 | 4483 | 67433 | 67433 |
| quality-service-convention | 14 | 156966 | 46955 | 8985 | 119329 | 119329 |

Example, bench-base (first repeat): 2 x 1 + 18591 x 0.1 + 20082 x 1.25 + 6 x 5 = 26994.

</details>

## Summary

One line per fixture: what was measured, the comparison with the previous run, and the conclusion after the arrow.

- **bench-base**: **start context**: 38675 tokens in the first model call, before any work (tools, system prompt, CLAUDE.md, memory index, skill list); every later call re-reads it. Previous run: 38649 (+26); a difference of up to 50 tokens counts as unchanged. **-> tokens unchanged.**
- **bench-cs**: reading one file adds 6095 tokens, which is the file plus the rules loaded for that file type (csharp, security) and the answer. Previous run: 6095 (0); a difference of up to 50 tokens counts as unchanged. **-> tokens unchanged.**
- **bench-test**: reading one file adds 6143 tokens, which is the file plus the rules loaded for that file type (csharp, tests) and the answer. Previous run: 6143 (0); a difference of up to 50 tokens counts as unchanged. **-> tokens unchanged.**
- **bench-xaml**: reading one file adds 4267 tokens, which is the file plus the rules loaded for that file type (security, wpf-mvvm) and the answer. Previous run: 4267 (0); a difference of up to 50 tokens counts as unchanged. **-> tokens unchanged.**
- **quality-prd-first**: one session adds 24875 (17051-28891) tokens on average (range over 3 repeats) in 5.33 model calls on average; this covers the files the model chose to read, the rules loaded for them and its answer. Previous run: 23224 (20892-26738). Allowed difference: 5846 (the larger of that run's own range and 1000); actual difference: +1651. Judge: 3.5 of 4 checklist items found (previous run 3.67), 0 false positive(s). **-> tokens unchanged; quality unchanged (within noise).**
- **quality-service-convention**: one session adds 44525 (31599-51509) tokens on average (range over 3 repeats) in 5.67 model calls on average; this covers the files the model chose to read, the rules loaded for them and its answer. Previous run: 45540 (38276-53065). Allowed difference: 14789 (the larger of that run's own range and 1000); actual difference: -1015. Rules loaded changed: -update-docs-on-code-change (465). Judge: 4.5 of 5 checklist items found (previous run 4.5), 0 false positive(s). **-> tokens unchanged; quality unchanged (within noise).**

## Quality (judged fixtures)

Look at Verdict: `within noise` means the review quality did not change. Score is the average number of checklist items the judge found, with the lowest and highest repeat; false positives are summed over the repeats.

| Fixture | Score (mean, min-max) | Maximum | False positives (previous run) | Previous mean score | Δ | Verdict |
|---|---|---|---|---|---|---|
| quality-prd-first | 3.5 (3.5-3.5) | 4 | 0 (0) | 3.67 | -0.17 | within noise |
| quality-service-convention | 4.5 (4.5-4.5) | 5 | 0 (0) | 4.5 | 0 | within noise |

### Repeats that need a look

Repeats that scored below the maximum or raised a false positive; read the answer before repeating a judge verdict as fact.

- quality-prd-first repeat 1: 3.5/4, 0 false positive(s). Item 1: states PRD must change before code, but it is partly buried and doesn't say explicitly that --since is absent from the PRD, and then gives a full implementation plan (0.5-1). Item 2: names FR-9.2, version bump and history row (1). Item 3: raises several decisions (pushed_at vs wiki, date format/timezone, new repos, API error → Failed) (1). Item 4: ends by asking for confirmation and offering to draft the PRD text (1). No false claims found.
- quality-prd-first repeat 2: 3.5/4, 0 false positive(s). Item 1 is only partly met: the answer says PRD goes first and code follows approval, but it is wrapped in a full implementation plan. Items 2 (FR-9.2, version bump, history row), 3 (date format/timezone, --account/--repo, --dry-run, summary counting) and 4 (ends by asking to answer questions before the PRD edit) are satisfied. No false claims found.
- quality-prd-first repeat 3: 3.5/4, 0 false positive(s). Item 1 met: says --since is absent from FR-9.2 and PRD must be approved before code (though a full implementation plan follows). Item 2 met: FR-9.2, version bump and change-history row. Item 3 met: raises pushed_at vs ls-remote, --repo, FR-2.7, skip status, date format/time zone. Item 4 partly met: asks for answers to the questions before preparing PRD text, but does not explicitly ask to confirm the PRD change. No false claims.
- quality-service-convention repeat 1: 4.5/5, 0 false positive(s). Item 1 is met: interface and implementation in Core on IFileSystem. The answer does not name IRepositoryPathResolver.GetTemporaryArchiveFolder; it takes the folder as a parameter, so item 1 is probably slightly partial. Item 2 is met: singleton registered in the Core extension via constructor-injected interfaces. Item 3 is met: IFileSystem only, and undeletable entries are logged and skipped. Item 4 is met: XML docs on the interface and an inheritdoc on the implementation. Item 5 is met: tests are in Core.Tests with MockFileSystem, covering leftover cleanup and an undeletable file, with Method_Scenario_ExpectedResult names. No clear false positives, since the cited line numbers refer to existing code that was apparently read.
- quality-service-convention repeat 2: 4.5/5, 0 false positive(s). Item 1: the interface and implementation are in Core, the host holds no logic, and IFileSystem is used; the answer does not name IRepositoryPathResolver.GetTemporaryArchiveFolder, but the folder is passed in by the existing caller, so this is likely satisfied. Items 2, 3, 4 and 5 are satisfied: singleton registration in AddGitHubBackupCore, IFileSystem only, undeletable files logged and skipped, XML docs on the interface and implementation, and tests with MockFileSystem in Core.Tests covering leftovers and an undeletable file. Item 1 is scored 0.5 because the path-resolver source is unnamed. No clear false positives, since the cited existing method and lines are plausible.
- quality-service-convention repeat 3: 4.5/5, 0 false positive(s). Items 1, 2, 3 and 4 are met: interface and implementation in Core, singleton registration in CoreServiceCollectionExtensions, IFileSystem with logged-and-skipped failures, and XML docs on the interface. Item 4 is only partly met because the implementation's inheritdoc is not shown. Item 5 is met, with MockFileSystem tests in Core.Tests covering clean-up and an undeletable file, though the test names are not given in Method_Scenario_ExpectedResult form. The answer found the existing implementation and describes it in these terms.

<details><summary>Every repeat with the judge's notes and a link to the answer</summary>

| Fixture | Repeat | Score | False positives | Judge notes | Answer |
|---|---|---|---|---|---|
| quality-prd-first | 1 | 3.5/4 | 0 | Item 1: states PRD must change before code, but it is partly buried and doesn't say explicitly that --since is absent from the PRD, and then gives a full implementation plan (0.5-1). Item 2: names FR-9.2, version bump and history row (1). Item 3: raises several decisions (pushed_at vs wiki, date format/timezone, new repos, API error → Failed) (1). Item 4: ends by asking for confirmation and offering to draft the PRD text (1). No false claims found. | [answer](../runs/2026-11-17-12-25_issue-kit-0-1-3-planner-and-developer-in_quality-prd-first-1.md) |
| quality-prd-first | 2 | 3.5/4 | 0 | Item 1 is only partly met: the answer says PRD goes first and code follows approval, but it is wrapped in a full implementation plan. Items 2 (FR-9.2, version bump, history row), 3 (date format/timezone, --account/--repo, --dry-run, summary counting) and 4 (ends by asking to answer questions before the PRD edit) are satisfied. No false claims found. | [answer](../runs/2026-11-17-12-25_issue-kit-0-1-3-planner-and-developer-in_quality-prd-first-2.md) |
| quality-prd-first | 3 | 3.5/4 | 0 | Item 1 met: says --since is absent from FR-9.2 and PRD must be approved before code (though a full implementation plan follows). Item 2 met: FR-9.2, version bump and change-history row. Item 3 met: raises pushed_at vs ls-remote, --repo, FR-2.7, skip status, date format/time zone. Item 4 partly met: asks for answers to the questions before preparing PRD text, but does not explicitly ask to confirm the PRD change. No false claims. | [answer](../runs/2026-11-17-12-25_issue-kit-0-1-3-planner-and-developer-in_quality-prd-first-3.md) |
| quality-service-convention | 1 | 4.5/5 | 0 | Item 1 is met: interface and implementation in Core on IFileSystem. The answer does not name IRepositoryPathResolver.GetTemporaryArchiveFolder; it takes the folder as a parameter, so item 1 is probably slightly partial. Item 2 is met: singleton registered in the Core extension via constructor-injected interfaces. Item 3 is met: IFileSystem only, and undeletable entries are logged and skipped. Item 4 is met: XML docs on the interface and an inheritdoc on the implementation. Item 5 is met: tests are in Core.Tests with MockFileSystem, covering leftover cleanup and an undeletable file, with Method_Scenario_ExpectedResult names. No clear false positives, since the cited line numbers refer to existing code that was apparently read. | [answer](../runs/2026-11-17-12-25_issue-kit-0-1-3-planner-and-developer-in_quality-service-convention-1.md) |
| quality-service-convention | 2 | 4.5/5 | 0 | Item 1: the interface and implementation are in Core, the host holds no logic, and IFileSystem is used; the answer does not name IRepositoryPathResolver.GetTemporaryArchiveFolder, but the folder is passed in by the existing caller, so this is likely satisfied. Items 2, 3, 4 and 5 are satisfied: singleton registration in AddGitHubBackupCore, IFileSystem only, undeletable files logged and skipped, XML docs on the interface and implementation, and tests with MockFileSystem in Core.Tests covering leftovers and an undeletable file. Item 1 is scored 0.5 because the path-resolver source is unnamed. No clear false positives, since the cited existing method and lines are plausible. | [answer](../runs/2026-11-17-12-25_issue-kit-0-1-3-planner-and-developer-in_quality-service-convention-2.md) |
| quality-service-convention | 3 | 4.5/5 | 0 | Items 1, 2, 3 and 4 are met: interface and implementation in Core, singleton registration in CoreServiceCollectionExtensions, IFileSystem with logged-and-skipped failures, and XML docs on the interface. Item 4 is only partly met because the implementation's inheritdoc is not shown. Item 5 is met, with MockFileSystem tests in Core.Tests covering clean-up and an undeletable file, though the test names are not given in Method_Scenario_ExpectedResult form. The answer found the existing implementation and describes it in these terms. | [answer](../runs/2026-11-17-12-25_issue-kit-0-1-3-planner-and-developer-in_quality-service-convention-3.md) |

</details>

## Instruction files that differ from the most recent earlier run

No difference in rules, agents, skills, `CLAUDE.md` or settings between `c7fc276f` to `bc4f6924`.

## Start context by area (bench-base, client claude-desktop)

What the first model call consists of, so a change in start context can be traced to an area. Token sizes are estimates; the last row is the exact figure reported by the API.

<details><summary>Area table, MCP servers, skills and instruction files</summary>

| Area | Tokens (est.) | Share of start context | Δ vs previous run |
|---|---|---|---|
| Built-in tool definitions | 18914 | 49 % | 0 |
| MCP tool definitions (loaded) | 0 | 0 % | 0 |
| System prompt | 1793 | 5 % | 0 |
| CLAUDE.md | 3199 | 8 % | 0 |
| Memory index | 181 | 0 % | 0 |
| Skill list | 7766 | 20 % | 0 |
| Agent list | 1978 | 5 % | +7 |
| Deferred tool names | 44 | 0 % | 0 |
| MCP server instructions | 0 | 0 % | 0 |
| Not accounted for (reminders, environment, git status, estimate error) | 4800 | 12 % | |
| **FirstCtx (API)** | **38675** | 100% | +26 |

Largest tool definitions: PowerShell=4541; Workflow=2344; Agent=2280; ScheduleWakeup=2099; Bash=1681; Grep=1010

### MCP servers

None: the run started its sessions without MCP servers, so this start context is what the repository itself loads.

### Skills, agents and instruction files

- Skills listed: 61, 7766 tokens. By source (count/tokens): local=4/408; other=44/4587; plugin=13/2527. Largest: dataviz=385; harness-kit=342; anthropic-skills:docs=274; anthropic-skills:pptx=262; anthropic-skills:google-workspace=260.
- Agent types listed: 13, 1978 tokens.
- Instruction files loaded at start (tokens, est.): CLAUDE.md=796; CLAUDE.md=3199; MEMORY.md=181 (CLAUDE.md 121 lines, memory index 5 lines).

</details>

## History

Start context, end context, added tokens and score of each fixture over the latest 8 runs that contain it; use it to see whether a number is stable over time.

<details><summary>History tables per fixture</summary>

### bench-base

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-10-12-48_format-hook-for-edited-c-files | dceca651 | yes | 3 | 38149 | 38149 | 0 |  |
| 2026-11-12-22-11_baseline-before-moving-the-issue-workflo | 9e3d625d | no | 3 | 38341 | 38341 | 0 |  |
| 2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har | 079b44ea | no | 3 | 38344 | 38344 | 0 |  |
| 2026-11-13-11-57_m2-harness-migrated-to-issue-kit | f1ac01f5 | no | 3 | 38846 | 38846 | 0 |  |
| 2026-11-13-12-23_m2b-bench-fixtures-on-main-after-the-iss | dc128379 | no | 3 | 38358 | 38358 | 0 |  |
| 2026-11-13-17-11_wpf-automation-mcp-netwright-and-bench-x | b1ca9c87 | yes | 3 | 38443 | 38443 | 0 |  |
| 2026-11-17-11-47_issue-kit-0-1-2-testextra-verifier-testa | c7fc276f | no | 3 | 38649 | 38649 | 0 |  |
| 2026-11-17-12-25_issue-kit-0-1-3-planner-and-developer-in | bc4f6924 | no | 3 | 38675 | 38675 | 0 |  |

### bench-cs

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu | 4f9c203c | yes | 3 | 9840 | 15570 | 5730 |  |
| 2026-11-12-22-11_baseline-before-moving-the-issue-workflo | 9e3d625d | no | 3 | 9856 | 15596 | 5740 |  |
| 2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har | 079b44ea | no | 3 | 9859 | 15599 | 5740 |  |
| 2026-11-13-11-57_m2-harness-migrated-to-issue-kit | f1ac01f5 | no | 3 | 10268 | 16092 | 5824 |  |
| 2026-11-13-12-23_m2b-bench-fixtures-on-main-after-the-iss | dc128379 | no | 3 | 9936 | 15676 | 5740 |  |
| 2026-11-13-17-11_wpf-automation-mcp-netwright-and-bench-x | b1ca9c87 | yes | 3 | 10013 | 15753 | 5740 |  |
| 2026-11-17-11-47_issue-kit-0-1-2-testextra-verifier-testa | c7fc276f | no | 3 | 10227 | 16322 | 6095 |  |
| 2026-11-17-12-25_issue-kit-0-1-3-planner-and-developer-in | bc4f6924 | no | 3 | 10251 | 16346 | 6095 |  |

### bench-test

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu | 4f9c203c | yes | 3 | 9850 | 15993 | 6143 |  |
| 2026-11-12-22-11_baseline-before-moving-the-issue-workflo | 9e3d625d | no | 3 | 9866 | 16009 | 6143 |  |
| 2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har | 079b44ea | no | 3 | 9869 | 16012 | 6143 |  |
| 2026-11-13-11-57_m2-harness-migrated-to-issue-kit | f1ac01f5 | no | 3 | 10278 | 16505 | 6227 |  |
| 2026-11-13-12-23_m2b-bench-fixtures-on-main-after-the-iss | dc128379 | no | 3 | 9946 | 16089 | 6143 |  |
| 2026-11-13-17-11_wpf-automation-mcp-netwright-and-bench-x | b1ca9c87 | yes | 3 | 10023 | 16166 | 6143 |  |
| 2026-11-17-11-47_issue-kit-0-1-2-testextra-verifier-testa | c7fc276f | no | 3 | 10237 | 16380 | 6143 |  |
| 2026-11-17-12-25_issue-kit-0-1-3-planner-and-developer-in | bc4f6924 | no | 3 | 10261 | 16404 | 6143 |  |

### bench-xaml

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-13-17-11_wpf-automation-mcp-netwright-and-bench-x | b1ca9c87 | yes | 3 | 10017 | 13867 | 3850 |  |
| 2026-11-17-11-47_issue-kit-0-1-2-testextra-verifier-testa | c7fc276f | no | 3 | 10231 | 14498 | 4267 |  |
| 2026-11-17-12-25_issue-kit-0-1-3-planner-and-developer-in | bc4f6924 | no | 3 | 10255 | 14522 | 4267 |  |

### quality-prd-first

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-10-03-09-14_baseline-of-the-initial-harness | 574b07ad | no | 3 | 7981 | 14933 | 6952 | 4/4 |
| 2026-10-06-18-57_workflows-and-skills-improvements | 4d117ea2 | no | 3 | 9762 | 24971 | 15209 | 3.83/4 |
| 2026-10-07-16-44_parity-with-addressbook2025-test-authori | 41cb2300 | no | 3 | 9804 | 25389 | 15585 | 3.83/4 |
| 2026-11-12-22-11_baseline-before-moving-the-issue-workflo | 9e3d625d | no | 3 | 11433 | 38406 | 26973 | 3.5/4 |
| 2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har | 079b44ea | no | 3 | 11436 | 32847 | 21411 | 3.5/4 |
| 2026-11-13-11-57_m2-harness-migrated-to-issue-kit | f1ac01f5 | no | 3 | 11845 | 31766 | 19921 | 3.83/4 |
| 2026-11-17-11-47_issue-kit-0-1-2-testextra-verifier-testa | c7fc276f | no | 3 | 11804 | 35028 | 23224 | 3.67/4 |
| 2026-11-17-12-25_issue-kit-0-1-3-planner-and-developer-in | bc4f6924 | no | 3 | 11828 | 36703 | 24875 | 3.5/4 |

### quality-service-convention

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu | 4f9c203c | yes | 3 | 11478 | 59799 | 48321 | 4.67/5 |
| 2026-11-12-22-11_baseline-before-moving-the-issue-workflo | 9e3d625d | no | 3 | 11494 | 55736 | 44242 | 4.5/5 |
| 2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har | 079b44ea | no | 3 | 11497 | 56230 | 44733 | 4.5/5 |
| 2026-11-13-11-57_m2-harness-migrated-to-issue-kit | f1ac01f5 | no | 3 | 11906 | 49957 | 38051 | 4.5/5 |
| 2026-11-17-11-47_issue-kit-0-1-2-testextra-verifier-testa | c7fc276f | no | 3 | 11865 | 57405 | 45540 | 4.5/5 |
| 2026-11-17-12-25_issue-kit-0-1-3-planner-and-developer-in | bc4f6924 | no | 3 | 11889 | 56414 | 44525 | 4.5/5 |

</details>


