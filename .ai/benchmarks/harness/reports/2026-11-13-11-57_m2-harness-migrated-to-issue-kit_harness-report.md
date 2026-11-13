# Harness report: 2026-11-13-11-57_m2-harness-migrated-to-issue-kit

**Overall: worse in bench-base, bench-cs, bench-test, quality-security** (see Summary for what moved and why).

- **Note:** M2 harness migrated to issue-kit
- **Setup:** commit f1ac01f5, uncommitted changes at start: no; Claude Code 2.1.319, model Sonnet 5.5, effort medium, harness 3, MCP servers: none.
- **Fixtures** (one test each): bench-base, bench-cs, bench-test, quality-prd-first, quality-security, quality-service-convention.
- **Compared with:** the most recent earlier run, `2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har` (commit 079b44ea); each fixture is compared with the previous run that contains it.

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
| bench-base (1) | 38846 (Δ +502) | 38846 (Δ +502) | 0 (Δ 0) | 1 | 6 | 48587 | all built-in<br>(15 tools, 18914 tokens) | none | unchanged |
| bench-cs (1) | 10268 (Δ +409) | 16092 (Δ +493) | 5824 (Δ +84) | 2 | 118 | 19812 | Read | csharp=2797<br>security=1085 | unchanged |
| bench-test (1) | 10278 (Δ +409) | 16505 (Δ +493) | 6227 (Δ +84) | 2 | 132 | 18820 | Read | csharp=2797<br>tests=1351 | unchanged |
| quality-prd-first (3) | 11845 (Δ +409) | 31766 (Δ -1081) | 19921 (17867-23259) (Δ -1490) | 5.67 (5-6) | 4195 (3931-4559) | 60165 (53365-64510) | Read<br>Grep<br>Glob | cli=614<br>csharp=2797<br>docs=481<br>security=1085<br>update-docs-on-code-change=455 | -external-processes (971) |
| quality-security (3) | 12076 (Δ +409) | 41190 (Δ +5938) | 29114 (23643-34853) (Δ +5529) | 2.67 (2-3) | 9427 (4973-12175) | 91203 (66938-105225) | Read<br>Grep<br>Glob | csharp=2797<br>external-processes=971<br>security=1085 | unchanged |
| quality-service-convention (3) | 11906 (Δ +409) | 49957 (Δ -6273) | 38051 (30441-46984) (Δ -6682) | 5.33 (5-6) | 10325 (8781-12416) | 115572 (93038-133907) | Read<br>Grep<br>Glob | csharp=2797<br>docs=481<br>external-processes=971<br>security=1085<br>tests=1351 | +csharp (2797), +tests (1351), -update-docs-on-code-change (451) |

<details><summary>How the cost units were calculated</summary>

Tokens by kind (averages over the repeats) multiplied by their weight. Weights: new input 1, cache read 0.1, cache write 1.25, output 5 per token.

| Fixture | New input (x 1) | Cache read (x 0.1) | Cache write (x 1.25) | Output (x 5) | Cost units (recomputed) | Cost units (stored) |
|---|---|---|---|---|---|---|
| bench-base | 2 | 0 | 38844 | 6 | 48587 | 48587 |
| bench-cs | 6 | 11936 | 14418 | 118 | 19812 | 19812 |
| bench-test | 6 | 13319 | 13458 | 132 | 18820 | 18820 |
| quality-prd-first | 14 | 106831 | 22794 | 4195 | 60165 | 60165 |
| quality-security | 7.33 | 45531 | 31606 | 9427 | 91203 | 91203 |
| quality-service-convention | 14.67 | 133262 | 40485 | 10325 | 115572 | 115572 |

Example, bench-base (first repeat): 2 x 1 + 0 x 0.1 + 38844 x 1.25 + 6 x 5 = 48587.

</details>

## Summary

One line per fixture: what was measured, the comparison with the previous run, and the conclusion after the arrow.

- **bench-base**: **start context**: 38846 tokens in the first model call, before any work (tools, system prompt, CLAUDE.md, memory index, skill list); every later call re-reads it. Previous run: 38344, so +502. Where it moved: claude.md -44; skill list +191; agent list -66; not attributable to any area (reminders, environment, git status) +421. **-> tokens worse (more tokens).**
- **bench-cs**: reading one file adds 5824 tokens, which is the file plus the rules loaded for that file type (csharp, security) and the answer. Previous run: 5740, so +84. Why: the same rules were loaded and no cause is known; check the answer and the file that was read. **-> tokens worse (more tokens).**
- **bench-test**: reading one file adds 6227 tokens, which is the file plus the rules loaded for that file type (csharp, tests) and the answer. Previous run: 6143, so +84. Why: the same rules were loaded and no cause is known; check the answer and the file that was read. **-> tokens worse (more tokens).**
- **quality-prd-first**: one session adds 19921 (17867-23259) tokens on average (range over 3 repeats) in 5.67 model calls on average; this covers the files the model chose to read, the rules loaded for them and its answer. Previous run: 21411 (18381-23346). Allowed difference: 4965 (the larger of that run's own range and 1000); actual difference: -1490. Rules loaded changed: -external-processes (971). Judge: 3.83 of 4 checklist items found (lowest repeat 3.5) (previous run 3.5), 0 false positive(s). **-> tokens unchanged; quality unchanged (within noise).**
- **quality-security**: one session adds 29114 (23643-34853) tokens on average (range over 3 repeats) in 2.67 model calls on average; this covers the files the model chose to read, the rules loaded for them and its answer. Previous run: 23585 (23560-23618). Allowed difference: 1000 (the larger of that run's own range and 1000); actual difference: +5529. Judge: 6.83 of 7 checklist items found (lowest repeat 6.5) (previous run 6.67), 0 false positive(s). **-> tokens worse (more tokens); quality unchanged (within noise).**
- **quality-service-convention**: one session adds 38051 (30441-46984) tokens on average (range over 3 repeats) in 5.33 model calls on average; this covers the files the model chose to read, the rules loaded for them and its answer. Previous run: 44733 (40015-47388). Allowed difference: 7373 (the larger of that run's own range and 1000); actual difference: -6682. Rules loaded changed: +csharp (2797), +tests (1351), -update-docs-on-code-change (451). Judge: 4.5 of 5 checklist items found (previous run 4.5), 0 false positive(s). **-> tokens unchanged; quality unchanged (within noise).**

## Quality (judged fixtures)

Look at Verdict: `within noise` means the review quality did not change. Score is the average number of checklist items the judge found, with the lowest and highest repeat; false positives are summed over the repeats.

| Fixture | Score (mean, min-max) | Maximum | False positives (previous run) | Previous mean score | Δ | Verdict |
|---|---|---|---|---|---|---|
| quality-prd-first | 3.83 (3.5-4) | 4 | 0 (0) | 3.5 | +0.33 | within noise |
| quality-security | 6.83 (6.5-7) | 7 | 0 (0) | 6.67 | +0.17 | within noise |
| quality-service-convention | 4.5 (4.5-4.5) | 5 | 0 (0) | 4.5 | 0 | within noise |

### Repeats that need a look

Repeats that scored below the maximum or raised a false positive; read the answer before repeating a judge verdict as fact.

- quality-prd-first repeat 1: 3.5/4, 0 false positive(s). Item 1: stated up front that the PRD must change first and be approved, but a full implementation plan follows (mostly satisfied). Item 2: names FR-9.2, version bump and change-history row. Item 3: raises several decisions (pushed_at vs refs, --repo interaction, date format/UTC, dry-run/discovery FR-2.7, summary/exit code FR-9.10), though it does not settle whether the status is Skipped. Item 4: ends by asking for agreement before editing the PRD, but also proposes defaults and an implementation plan, so partly satisfied.
- quality-security repeat 3: 6.5/7, 0 false positive(s). Found 1 (as SUGGESTION, token in args via GetRemoteRefsAsync), 2 (log unmasked), 3 (URL token and .git/config), 4, 5, 6 (option injection via --upload-pack in fetch), and 7 (archive file name only checked non-empty, ..\ escapes). Item 7 is described with a rooted/.. name changing finalPath, which is accurate, so count it fully; item 1 is flagged with the right risk but low severity, still counted. Small deduction because the item 6 finding is bundled with a speculative SetRemoteUrlAsync/ext:: claim, but no outright false positive.
- quality-service-convention repeat 1: 4.5/5, 0 false positive(s). Item 1: interface and implementation in Core, host holds no logic; the folder is passed in by the orchestrator (resolver not named explicitly, but not hard-coded) - mostly satisfied. Item 2: singleton registered in AddGitHubBackupCore via constructor-injected interfaces - satisfied. Item 3: IFileSystem only, failures logged and skipped; no time use - satisfied. Item 4: XML docs on interface and implementation with inheritdoc - satisfied. Item 5: tests in Core.Tests with MockFileSystem covering leftovers and undeletable file; names follow Method_Scenario_ExpectedResult - satisfied. Minor deduction on item 1 for not naming the resolver. No false claims found.
- quality-service-convention repeat 2: 4.5/5, 0 false positive(s). Item 1: interface and implementation in Core, host holds no logic; the folder is passed in by the orchestrator, and the answer does not name IRepositoryPathResolver, but the existing orchestrator evidently derives it (about 0.5-1). Item 2: singleton registered in AddGitHubBackupCore via constructor-injected interfaces (1). Item 3: IFileSystem only, failures logged and skipped, no time needed (1). Item 4: XML docs on interface and implementation with inheritdoc (1). Item 5: tests in Core.Tests with MockFileSystem, covering leftovers and an undeletable file, with Method_Scenario_Result names (1). The answer found that the service already exists as a private orchestrator method and described it, which is allowed; no false claims spotted.
- quality-service-convention repeat 3: 4.5/5, 0 false positive(s). Item 1 satisfied: interface and implementation live in Core, disk is reached via IFileSystem, the host holds no clean-up logic, and the folder is passed in. Item 2 satisfied: the service is registered as a singleton in AddGitHubBackupCore with constructor-injected interfaces. Item 3 satisfied: only IFileSystem is used, and a file that cannot be deleted is logged and skipped. Item 4 satisfied: the interface and its members have summaries and the implementation uses inheritdoc. Item 5 partial: the tests are in Core.Tests with MockFileSystem and cover leftovers and an undeletable file, but the names are Method_Scenario_ExpectedResult only loosely, and the event IDs are placeholders. No false statements found about the repository.

<details><summary>Every repeat with the judge's notes and a link to the answer</summary>

| Fixture | Repeat | Score | False positives | Judge notes | Answer |
|---|---|---|---|---|---|
| quality-prd-first | 1 | 3.5/4 | 0 | Item 1: stated up front that the PRD must change first and be approved, but a full implementation plan follows (mostly satisfied). Item 2: names FR-9.2, version bump and change-history row. Item 3: raises several decisions (pushed_at vs refs, --repo interaction, date format/UTC, dry-run/discovery FR-2.7, summary/exit code FR-9.10), though it does not settle whether the status is Skipped. Item 4: ends by asking for agreement before editing the PRD, but also proposes defaults and an implementation plan, so partly satisfied. | [answer](../runs/2026-11-13-11-57_m2-harness-migrated-to-issue-kit_quality-prd-first-1.md) |
| quality-prd-first | 2 | 4/4 | 0 | Item 1: states --since is absent from FR-9.2 and PRD must change before code. Item 2: names FR-9.2, version, date and change-history row. Item 3: raises many decisions (pushed_at, date format/UTC, Skipped status, --repo/--account interaction, discovery). Item 4: ends by asking the user to choose options and approve PRD. The implementation plan is clearly marked as a proposal; no false claims found. | [answer](../runs/2026-11-13-11-57_m2-harness-migrated-to-issue-kit_quality-prd-first-2.md) |
| quality-prd-first | 3 | 4/4 | 0 | Item 1: states upfront that PRD must change and code is written only after approval. Item 2: names FR-9.2, version bump and change-history row. Item 3: raises several decisions (pushed_at, --repo with FR-2.7, --dry-run, Skipped status in summary, date format/UTC). Item 4: ends by asking user to decide open points and confirm before code. The implementation plan is present but clearly secondary and marked as to-do after approval; no false claims found. | [answer](../runs/2026-11-13-11-57_m2-harness-migrated-to-issue-kit_quality-prd-first-3.md) |
| quality-security | 1 | 7/7 | 0 | All seven seeded items are found. The token URL in the clone (stored in .git/config, in process args, and logged unmasked) covers items 2 and 3, and the header in `-c` of GetRemoteRefsAsync, the missing GIT_TERMINAL_PROMPT, GetCloneFolder, the branch option injection and the SevenZip FileName are each flagged accurately. The extra findings (masker registration, ceiling directories, wiki name collision) are plausible extras, not clear false positives. | [answer](../runs/2026-11-13-11-57_m2-harness-migrated-to-issue-kit_quality-security-1.md) |
| quality-security | 2 | 7/7 | 0 | All seven items found: #1 token in args (GetRemoteRefsAsync), #2 unmasked URL log and #3 token URL in .git/config (finding 1 covers both), #4 missing GIT_TERMINAL_PROMPT, #5 GetCloneFolder lacking EnsureValidNames/ContainedFolder, #6 branch '-' with fetch lacking '--', #7 SevenZipArchiver FileName not validated. No clear false positives; extra findings are plausible defense-in-depth. | [answer](../runs/2026-11-13-11-57_m2-harness-migrated-to-issue-kit_quality-security-2.md) |
| quality-security | 3 | 6.5/7 | 0 | Found 1 (as SUGGESTION, token in args via GetRemoteRefsAsync), 2 (log unmasked), 3 (URL token and .git/config), 4, 5, 6 (option injection via --upload-pack in fetch), and 7 (archive file name only checked non-empty, ..\ escapes). Item 7 is described with a rooted/.. name changing finalPath, which is accurate, so count it fully; item 1 is flagged with the right risk but low severity, still counted. Small deduction because the item 6 finding is bundled with a speculative SetRemoteUrlAsync/ext:: claim, but no outright false positive. | [answer](../runs/2026-11-13-11-57_m2-harness-migrated-to-issue-kit_quality-security-3.md) |
| quality-service-convention | 1 | 4.5/5 | 0 | Item 1: interface and implementation in Core, host holds no logic; the folder is passed in by the orchestrator (resolver not named explicitly, but not hard-coded) - mostly satisfied. Item 2: singleton registered in AddGitHubBackupCore via constructor-injected interfaces - satisfied. Item 3: IFileSystem only, failures logged and skipped; no time use - satisfied. Item 4: XML docs on interface and implementation with inheritdoc - satisfied. Item 5: tests in Core.Tests with MockFileSystem covering leftovers and undeletable file; names follow Method_Scenario_ExpectedResult - satisfied. Minor deduction on item 1 for not naming the resolver. No false claims found. | [answer](../runs/2026-11-13-11-57_m2-harness-migrated-to-issue-kit_quality-service-convention-1.md) |
| quality-service-convention | 2 | 4.5/5 | 0 | Item 1: interface and implementation in Core, host holds no logic; the folder is passed in by the orchestrator, and the answer does not name IRepositoryPathResolver, but the existing orchestrator evidently derives it (about 0.5-1). Item 2: singleton registered in AddGitHubBackupCore via constructor-injected interfaces (1). Item 3: IFileSystem only, failures logged and skipped, no time needed (1). Item 4: XML docs on interface and implementation with inheritdoc (1). Item 5: tests in Core.Tests with MockFileSystem, covering leftovers and an undeletable file, with Method_Scenario_Result names (1). The answer found that the service already exists as a private orchestrator method and described it, which is allowed; no false claims spotted. | [answer](../runs/2026-11-13-11-57_m2-harness-migrated-to-issue-kit_quality-service-convention-2.md) |
| quality-service-convention | 3 | 4.5/5 | 0 | Item 1 satisfied: interface and implementation live in Core, disk is reached via IFileSystem, the host holds no clean-up logic, and the folder is passed in. Item 2 satisfied: the service is registered as a singleton in AddGitHubBackupCore with constructor-injected interfaces. Item 3 satisfied: only IFileSystem is used, and a file that cannot be deleted is logged and skipped. Item 4 satisfied: the interface and its members have summaries and the implementation uses inheritdoc. Item 5 partial: the tests are in Core.Tests with MockFileSystem and cover leftovers and an undeletable file, but the names are Method_Scenario_ExpectedResult only loosely, and the event IDs are placeholders. No false statements found about the repository. | [answer](../runs/2026-11-13-11-57_m2-harness-migrated-to-issue-kit_quality-service-convention-3.md) |

</details>

## Instruction files that differ from the most recent earlier run

Changes in rules, agents, skills, `CLAUDE.md` and settings from `079b44ea` to `f1ac01f5` :

```
 .ai/prompts/implement-issue.md                     | 186 ---------
 .ai/prompts/implement-issues.md                    |  82 ----
 .claude/agents/build-runner.md                     |  55 ---
 .claude/agents/issue-developer.md                  |  41 --
 .claude/agents/issue-planner.md                    |  48 ---
 .claude/agents/skill-runner.md                     |  37 --
 .claude/commands/implement-issue.md                |  12 -
 .claude/commands/implement-issues.md               |  12 -
 .claude/commands/next-issue.md                     |   7 -
 .claude/rules/docs.md                              |   2 +-
 .claude/rules/update-docs-on-code-change.md        |   4 +-
 .claude/settings.json                              |  31 +-
 .claude/skills/_local.git-commit/SKILL.md          |  81 ----
 .claude/skills/_local.github-issue/SKILL.md        | 113 ------
 .../scripts/Set-AcceptanceChecks.ps1               | 132 -------
 .../skills/_local.harness-quality-check/SKILL.md   | 118 ------
 .../scripts/Collect-Harness.ps1                    |  93 -----
 .../scripts/Run-Harness.ps1                        | 156 --------
 .../scripts/lib/Judge.ps1                          |  45 ---
 .../scripts/lib/Read-Session.ps1                   | 138 -------
 .../scripts/lib/Report.ps1                         | 428 ---------------------
 .claude/skills/_local.next-issue/SKILL.md          |  39 --
 .../_local.next-issue/scripts/Get-NextIssue.ps1    | 159 --------
 .../skills/_local.nuget-package-update/SKILL.md    |   2 +-
 .claude/skills/_local.open-pr/SKILL.md             |  44 ---
 CLAUDE.md                                          |  25 +-
 26 files changed, 35 insertions(+), 2055 deletions(-)
```

## Start context by area (bench-base, client claude-desktop)

What the first model call consists of, so a change in start context can be traced to an area. Token sizes are estimates; the last row is the exact figure reported by the API.

<details><summary>Area table, MCP servers, skills and instruction files</summary>

| Area | Tokens (est.) | Share of start context | Δ vs previous run |
|---|---|---|---|
| Built-in tool definitions | 18914 | 49 % | 0 |
| MCP tool definitions (loaded) | 0 | 0 % | 0 |
| System prompt | 1793 | 5 % | 0 |
| CLAUDE.md | 2948 | 8 % | -44 |
| Memory index | 181 | 0 % | 0 |
| Skill list | 7900 | 20 % | +191 |
| Agent list | 1971 | 5 % | -66 |
| Deferred tool names | 44 | 0 % | 0 |
| MCP server instructions | 0 | 0 % | 0 |
| Not accounted for (reminders, environment, git status, estimate error) | 5095 | 13 % | |
| **FirstCtx (API)** | **38846** | 100% | +502 |

Largest tool definitions: PowerShell=4541; Workflow=2344; Agent=2280; ScheduleWakeup=2099; Bash=1681; Grep=1010

### MCP servers

None: the run started its sessions without MCP servers, so this start context is what the repository itself loads.

### Skills, agents and instruction files

- Skills listed: 64, 7900 tokens. By source (count/tokens): local=4/408; other=47/4721; plugin=13/2527. Largest: dataviz=385; harness-kit=342; anthropic-skills:docs=274; anthropic-skills:pptx=262; anthropic-skills:google-workspace=260.
- Agent types listed: 13, 1971 tokens.
- Instruction files loaded at start (tokens, est.): CLAUDE.md=796; CLAUDE.md=2948; MEMORY.md=181 (CLAUDE.md 112 lines, memory index 5 lines).

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
| 2026-11-13-11-57_m2-harness-migrated-to-issue-kit | f1ac01f5 | no | 3 | 38846 | 38846 | 0 |  |

### bench-cs

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu | 4f9c203c | yes | 3 | 9840 | 15570 | 5730 |  |
| 2026-11-12-22-11_baseline-before-moving-the-issue-workflo | 9e3d625d | no | 3 | 9856 | 15596 | 5740 |  |
| 2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har | 079b44ea | no | 3 | 9859 | 15599 | 5740 |  |
| 2026-11-13-11-57_m2-harness-migrated-to-issue-kit | f1ac01f5 | no | 3 | 10268 | 16092 | 5824 |  |

### bench-test

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu | 4f9c203c | yes | 3 | 9850 | 15993 | 6143 |  |
| 2026-11-12-22-11_baseline-before-moving-the-issue-workflo | 9e3d625d | no | 3 | 9866 | 16009 | 6143 |  |
| 2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har | 079b44ea | no | 3 | 9869 | 16012 | 6143 |  |
| 2026-11-13-11-57_m2-harness-migrated-to-issue-kit | f1ac01f5 | no | 3 | 10278 | 16505 | 6227 |  |

### quality-prd-first

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-10-03-09-14_baseline-of-the-initial-harness | 574b07ad | no | 3 | 7981 | 14933 | 6952 | 4/4 |
| 2026-10-06-18-57_workflows-and-skills-improvements | 4d117ea2 | no | 3 | 9762 | 24971 | 15209 | 3.83/4 |
| 2026-10-07-16-44_parity-with-addressbook2025-test-authori | 41cb2300 | no | 3 | 9804 | 25389 | 15585 | 3.83/4 |
| 2026-11-12-22-11_baseline-before-moving-the-issue-workflo | 9e3d625d | no | 3 | 11433 | 38406 | 26973 | 3.5/4 |
| 2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har | 079b44ea | no | 3 | 11436 | 32847 | 21411 | 3.5/4 |
| 2026-11-13-11-57_m2-harness-migrated-to-issue-kit | f1ac01f5 | no | 3 | 11845 | 31766 | 19921 | 3.83/4 |

### quality-security

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu | 4f9c203c | yes | 3 | 11654 | 39321 | 27667 | 7/7 |
| 2026-11-12-22-11_baseline-before-moving-the-issue-workflo | 9e3d625d | no | 3 | 11664 | 45817 | 34153 | 7/7 |
| 2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har | 079b44ea | no | 3 | 11667 | 35252 | 23585 | 6.67/7 |
| 2026-11-13-11-57_m2-harness-migrated-to-issue-kit | f1ac01f5 | no | 3 | 12076 | 41190 | 29114 | 6.83/7 |

### quality-service-convention

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu | 4f9c203c | yes | 3 | 11478 | 59799 | 48321 | 4.67/5 |
| 2026-11-12-22-11_baseline-before-moving-the-issue-workflo | 9e3d625d | no | 3 | 11494 | 55736 | 44242 | 4.5/5 |
| 2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har | 079b44ea | no | 3 | 11497 | 56230 | 44733 | 4.5/5 |
| 2026-11-13-11-57_m2-harness-migrated-to-issue-kit | f1ac01f5 | no | 3 | 11906 | 49957 | 38051 | 4.5/5 |

</details>


