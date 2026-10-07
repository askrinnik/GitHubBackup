# Harness report: 2026-10-07-16-44_parity-with-addressbook2025-test-authori

**Overall: no regressions; better in bench-base.**

- **Note:** parity with AddressBook2025: test-authoring lane, github-issue and open-pr skills, generic skills
- **Setup:** commit 41cb2300, uncommitted changes at start: no; Claude Code 2.1.291, model Sonnet 5.5, effort medium, harness 3, MCP servers: none.
- **Fixtures** (one test each): bench-base, quality-prd-first.
- **Compared with:** the most recent earlier run, `2026-10-06-18-57_workflows-and-skills-improvements` (commit 4d117ea2); each fixture is compared with the previous run that contains it.

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
| bench-base (1) | 33794 (Δ -10785) | 33794 (Δ -10785) | 0 (Δ 0) | 1 | 3 | 42257 | all built-in<br>(15 tools, 17620 tokens) | none | unchanged |
| quality-prd-first (3) | 9804 (Δ +42) | 25389 (Δ +418) | 15585 (13966-16434) (Δ +376) | 3.33 (3-4) | 3028 (2613-3314) | 41172 (39618-42400) | Read<br>Grep<br>Glob | cli=605<br>csharp=2797<br>docs=451<br>security=1085<br>update-docs-on-code-change=451 | csharp 2744->2797, docs 376->451 |

<details><summary>How the cost units were calculated</summary>

Tokens by kind (averages over the repeats) multiplied by their weight. Weights: new input 1, cache read 0.1, cache write 1.25, output 5 per token.

| Fixture | New input (x 1) | Cache read (x 0.1) | Cache write (x 1.25) | Output (x 5) | Cost units (recomputed) | Cost units (stored) |
|---|---|---|---|---|---|---|
| bench-base | 2 | 0 | 33792 | 3 | 42257 | 42257 |
| quality-prd-first | 6.67 | 37129 | 17852 | 3028 | 41172 | 41172 |

Example, bench-base (first repeat): 2 x 1 + 0 x 0.1 + 33792 x 1.25 + 3 x 5 = 42257.

</details>

## Summary

One line per fixture: what was measured, the comparison with the previous run, and the conclusion after the arrow.

- **bench-base**: **start context**: 33794 tokens in the first model call, before any work (tools, system prompt, CLAUDE.md, memory index, skill list); every later call re-reads it. Previous run: 44579, so -10785. Where it moved: built-in tool definitions -14172; claude.md +67; skill list +363; agent list +22; not attributable to any area (reminders, environment, git status) +2943. **-> tokens better (fewer tokens).**
- **quality-prd-first**: one session adds 15585 (13966-16434) tokens on average (range over 3 repeats) in 3.33 model calls on average; this covers the files the model chose to read, the rules loaded for them and its answer. Previous run: 15209 (13356-16356). Allowed difference: 3000 (the larger of that run's own range and 1000); actual difference: +376. Rules loaded changed: csharp 2744->2797, docs 376->451. Judge: 3.83 of 4 checklist items found (lowest repeat 3.5) (previous run 3.83), 0 false positive(s). **-> tokens unchanged; quality unchanged (within noise).**

## Quality (judged fixtures)

Look at Verdict: `within noise` means the review quality did not change. Score is the average number of checklist items the judge found, with the lowest and highest repeat; false positives are summed over the repeats.

| Fixture | Score (mean, min-max) | Maximum | False positives (previous run) | Previous mean score | Δ | Verdict |
|---|---|---|---|---|---|---|
| quality-prd-first | 3.83 (3.5-4) | 4 | 0 (0) | 3.83 | 0 | within noise |

### Repeats that need a look

Repeats that scored below the maximum or raised a false positive; read the answer before repeating a judge verdict as fact.

- quality-prd-first repeat 2: 3.5/4, 0 false positive(s). Item 1: states --since is not in FR-9.2 and PRD changes first, but also lays out a full code plan (still gated on approval). Item 2: names FR-9.2, version bump and history row. Item 3: raises many decisions (pushed_at vs other source, --repo interaction, dry-run, skipped handling, date format). Item 4: ends with questions, but they do not clearly ask to confirm the PRD change. The skipped-repo handling (not a failure) is proposed as a design choice, not as already specified, so no false positive.

<details><summary>Every repeat with the judge's notes and a link to the answer</summary>

| Fixture | Repeat | Score | False positives | Judge notes | Answer |
|---|---|---|---|---|---|
| quality-prd-first | 1 | 4/4 | 0 | States up front that the PRD lacks --since and a PRD change must be approved first; names FR-9.2, version bump 0.6→0.7 and change-history row; raises several decisions (pushed_at, --repo, --dry-run, Skipped/summary, date format/UTC, discovery); ends asking the user to decide and confirm before drafting. Implementation plan is included but clearly gated on approval; the claims about repo contents are plausible and not clearly false. | [answer](../runs/2026-10-07-16-44_parity-with-addressbook2025-test-authori_quality-prd-first-1.md) |
| quality-prd-first | 2 | 3.5/4 | 0 | Item 1: states --since is not in FR-9.2 and PRD changes first, but also lays out a full code plan (still gated on approval). Item 2: names FR-9.2, version bump and history row. Item 3: raises many decisions (pushed_at vs other source, --repo interaction, dry-run, skipped handling, date format). Item 4: ends with questions, but they do not clearly ask to confirm the PRD change. The skipped-repo handling (not a failure) is proposed as a design choice, not as already specified, so no false positive. | [answer](../runs/2026-10-07-16-44_parity-with-addressbook2025-test-authori_quality-prd-first-2.md) |
| quality-prd-first | 3 | 4/4 | 0 | Item 1: states the PRD doesn't allow --since and the PRD change needs approval before any code. Item 2: names the FR-9.2 table, the version bump and the change-history row. Item 3: raises several decisions (date format and zone, --repo interaction, --silent, Skipped status and summary, exit code). Item 4: ends with questions to the user and asks for approval. The implementation plan is clearly gated behind PRD approval, and no false statements about the PRD were found. | [answer](../runs/2026-10-07-16-44_parity-with-addressbook2025-test-authori_quality-prd-first-3.md) |

</details>

## Instruction files that differ from the most recent earlier run

Changes in rules, agents, skills, `CLAUDE.md` and settings from `4d117ea2` to `41cb2300` :

```
 .ai/prompts/implement-issue.md                     |   74 +-
 .ai/prompts/implement-issues.md                    |   10 +-
 .claude/agents/build-runner.md                     |   10 +-
 .claude/agents/issue-developer.md                  |    4 +
 .claude/agents/issue-planner.md                    |   13 +-
 .claude/agents/security-reviewer.md                |    5 +-
 .claude/agents/skill-runner.md                     |   21 +-
 .claude/commands/implement-issue.md                |    2 +-
 .claude/commands/implement-issues.md               |    2 +-
 .claude/commands/next-issue.md                     |    2 +-
 .claude/rules/csharp.md                            |    2 +-
 .claude/rules/docs.md                              |    5 +-
 .claude/rules/update-docs-on-code-change.md        |    4 +-
 .claude/skills/_local.debug-issue/SKILL.md         |    2 +-
 .claude/skills/_local.git-commit/SKILL.md          |    4 +-
 .claude/skills/_local.github-issue/SKILL.md        |  113 +++
 .../scripts/Set-AcceptanceChecks.ps1               |   15 +-
 .../scripts/lib/Report.ps1                         |    2 +-
 .../SKILL.md                                       |    6 +-
 .claude/skills/_local.post-issue-comment/SKILL.md  |   85 --
 .claude/skills/code-review-checklist/SKILL.md      |  418 ++++++++
 .claude/skills/coverage-analysis/SKILL.md          |    1 +
 .claude/skills/create-implementation-plan/SKILL.md |  157 +++
 .claude/skills/create-specification/SKILL.md       |  128 +++
 .claude/skills/csharp-xunit/SKILL.md               |   68 ++
 .../skills/directory-build-organization/SKILL.md   |    1 +
 .claude/skills/document-workflow/SKILL.md          |   95 ++
 .../skills/document-workflow/references/format.md  |  160 +++
 .../document-workflow/scripts/preview-mermaid.cjs  |   75 ++
 .claude/skills/dotnet-best-practices/SKILL.md      |   85 ++
 .claude/skills/dotnet-timezone/SKILL.md            |  110 ++
 .../dotnet-timezone/references/code-patterns.md    |  153 +++
 .../dotnet-timezone/references/timezone-index.md   |   87 ++
 .claude/skills/security-owasp/SKILL.md             | 1058 ++++++++++++++++++++
 .claude/skills/test-anti-patterns/SKILL.md         |    1 +
 .claude/skills/update-docs/SKILL.md                |  550 ++++++++++
 CLAUDE.md                                          |   14 +-
 37 files changed, 3385 insertions(+), 157 deletions(-)
```

## Start context by area (bench-base, client claude-desktop)

What the first model call consists of, so a change in start context can be traced to an area. Token sizes are estimates; the last row is the exact figure reported by the API.

<details><summary>Area table, MCP servers, skills and instruction files</summary>

| Area | Tokens (est.) | Share of start context | Δ vs previous run |
|---|---|---|---|
| Built-in tool definitions | 17620 | 52 % | -14172 |
| MCP tool definitions (loaded) | 0 | 0 % | 0 |
| System prompt | 1720 | 5 % | 0 |
| CLAUDE.md | 2596 | 8 % | +67 |
| Memory index | 68 | 0 % | 0 |
| Skill list | 6526 | 19 % | +363 |
| Agent list | 1511 | 4 % | +22 |
| Deferred tool names | 47 | 0 % | -8 |
| MCP server instructions | 0 | 0 % | 0 |
| Not accounted for (reminders, environment, git status, estimate error) | 3706 | 11 % | |
| **FirstCtx (API)** | **33794** | 100% | -10785 |

Largest tool definitions: PowerShell=4542; Workflow=2344; ScheduleWakeup=2099; Bash=1446; Agent=1330; Grep=1010

### MCP servers

None: the run started its sessions without MCP servers, so this start context is what the repository itself loads.

### Skills, agents and instruction files

- Skills listed: 55, 6526 tokens. By source (count/tokens): local=8/828; other=34/2927; plugin=13/2527. Largest: dataviz=385; anthropic-skills:docs=274; anthropic-skills:pptx=262; anthropic-skills:google-workspace=260; anthropic-skills:xlsx=259.
- Agent types listed: 12, 1511 tokens.
- Instruction files loaded at start (tokens, est.): CLAUDE.md=2596; MEMORY.md=68 (CLAUDE.md 106 lines, memory index 2 lines).

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

### quality-prd-first

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-10-03-09-14_baseline-of-the-initial-harness | 574b07ad | no | 3 | 7981 | 14933 | 6952 | 4/4 |
| 2026-10-06-18-57_workflows-and-skills-improvements | 4d117ea2 | no | 3 | 9762 | 24971 | 15209 | 3.83/4 |
| 2026-10-07-16-44_parity-with-addressbook2025-test-authori | 41cb2300 | no | 3 | 9804 | 25389 | 15585 | 3.83/4 |

</details>


