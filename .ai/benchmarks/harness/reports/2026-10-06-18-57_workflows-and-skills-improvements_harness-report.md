# Harness report: 2026-10-06-18-57_workflows-and-skills-improvements

**Overall: worse in bench-base, quality-prd-first** (see Summary for what moved and why).

- **Note:** workflows and skills improvements
- **Setup:** commit 4d117ea2, uncommitted changes at start: no; Claude Code 2.1.284, model Sonnet 5.5, effort medium, harness 3, MCP servers: none.
- **Fixtures** (one test each): bench-base, quality-prd-first.
- **Compared with:** the most recent earlier run, `2026-10-03-09-14_baseline-of-the-initial-harness` (commit 574b07ad); each fixture is compared with the previous run that contains it.

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
| bench-base (1) | 44579 (Δ +2172) | 44579 (Δ +2172) | 0 (Δ 0) | 1 | 3 | 55738 | all built-in<br>(16 tools, 31792 tokens) | none | unchanged |
| quality-prd-first (3) | 9762 (Δ +1781) | 24971 (Δ +10038) | 15209 (13356-16356) (Δ +8257) | 3.67 (3-4) | 2723 (2408-3240) | 44358 (40095-48517) | Read<br>Grep<br>Glob | cli=605<br>csharp=2744<br>docs=376<br>security=1085<br>update-docs-on-code-change=404 | +cli (605), +csharp (2744), +security (1085), +update-docs-on-code-change (404) |

<details><summary>How the cost units were calculated</summary>

Tokens by kind (averages over the repeats) multiplied by their weight. Weights: new input 1, cache read 0.1, cache write 1.25, output 5 per token.

| Fixture | New input (x 1) | Cache read (x 0.1) | Cache write (x 1.25) | Output (x 5) | Cost units (recomputed) | Cost units (stored) |
|---|---|---|---|---|---|---|
| bench-base | 2 | 0 | 44577 | 3 | 55738 | 55738 |
| quality-prd-first | 7.33 | 43594 | 21102 | 2723 | 44358 | 44358 |

Example, bench-base (first repeat): 2 x 1 + 0 x 0.1 + 44577 x 1.25 + 3 x 5 = 55738.

</details>

## Summary

One line per fixture: what was measured, the comparison with the previous run, and the conclusion after the arrow.

- **bench-base**: **start context**: 44579 tokens in the first model call, before any work (tools, system prompt, CLAUDE.md, memory index, skill list); every later call re-reads it. Previous run: 42407, so +2172. Where it moved: system prompt +112; claude.md +807; skill list +59; agent list +162; not attributable to any area (reminders, environment, git status) +1032. **-> tokens worse (more tokens).**
- **quality-prd-first**: one session adds 15209 (13356-16356) tokens on average (range over 3 repeats) in 3.67 model calls on average; this covers the files the model chose to read, the rules loaded for them and its answer. Previous run: 6952 (6413-7731). Allowed difference: 1318 (the larger of that run's own range and 1000); actual difference: +8257. Rules loaded changed: +cli (605), +csharp (2744), +security (1085), +update-docs-on-code-change (404). Judge: 3.83 of 4 checklist items found (lowest repeat 3.5) (previous run 4), 0 false positive(s). **-> tokens worse (more tokens); quality unchanged (within noise).**

## Quality (judged fixtures)

Look at Verdict: `within noise` means the review quality did not change. Score is the average number of checklist items the judge found, with the lowest and highest repeat; false positives are summed over the repeats.

| Fixture | Score (mean, min-max) | Maximum | False positives (previous run) | Previous mean score | Δ | Verdict |
|---|---|---|---|---|---|---|
| quality-prd-first | 3.83 (3.5-4) | 4 | 0 (0) | 4 | -0.17 | within noise |

### Repeats that need a look

Repeats that scored below the maximum or raised a false positive; read the answer before repeating a judge verdict as fact.

- quality-prd-first repeat 3: 3.5/4, 0 false positive(s). Item 1: states --since is not in FR-9.2 and the PRD must change first (found). Item 2: names FR-9.2, a new FR-9.11, version bump and change-history row (found). Item 3: raises pushed_at vs refs, --repo/--account interaction, Skipped status, date format and dry-run (found). Item 4: asks questions before the PRD edit, but includes a long implementation plan and does not explicitly ask to confirm the PRD change (partial). No clear false positives; the proposals are marked as proposals.

<details><summary>Every repeat with the judge's notes and a link to the answer</summary>

| Fixture | Repeat | Score | False positives | Judge notes | Answer |
|---|---|---|---|---|---|
| quality-prd-first | 1 | 4/4 | 0 | Item 1: opens by saying --since isn't in the PRD, the PRD changes first, and it waits for approval before touching code. Item 2: names FR-9.2, the version bump and the change-history row. Item 3: raises several decisions (date format/time zone, --repo interaction, Skipped status and exit code, empty repos, wikis). Item 4: ends by asking the user to agree with the recommendations before drafting the PRD edit. The long implementation plan is explicitly gated on approval, and the proposals are marked as proposals, so no false positives. | [answer](../runs/2026-10-06-18-57_workflows-and-skills-improvements_quality-prd-first-1.md) |
| quality-prd-first | 2 | 4/4 | 0 | Item 1: states --since is not in the PRD and the PRD changes first, no code until approval. Item 2: names FR-9.2, version bump and change-history row. Item 3: raises many decisions (pushed_at date, --repo interaction, skipped status/FR-9.8, date format/UTC, exit code). Item 4: ends by asking the user to confirm the questions before drafting the PRD edit. No false claims found; the proposals are marked as proposals. | [answer](../runs/2026-10-06-18-57_workflows-and-skills-improvements_quality-prd-first-2.md) |
| quality-prd-first | 3 | 3.5/4 | 0 | Item 1: states --since is not in FR-9.2 and the PRD must change first (found). Item 2: names FR-9.2, a new FR-9.11, version bump and change-history row (found). Item 3: raises pushed_at vs refs, --repo/--account interaction, Skipped status, date format and dry-run (found). Item 4: asks questions before the PRD edit, but includes a long implementation plan and does not explicitly ask to confirm the PRD change (partial). No clear false positives; the proposals are marked as proposals. | [answer](../runs/2026-10-06-18-57_workflows-and-skills-improvements_quality-prd-first-3.md) |

</details>

## Instruction files that differ from the most recent earlier run

Changes in rules, agents, skills, `CLAUDE.md` and settings from `574b07ad` to `4d117ea2` :

```
 .claude/agents/build-runner.md                     |  53 +++++++++
 .claude/agents/skill-runner.md                     |  36 +++---
 .claude/rules/csharp.md                            |   3 +-
 .claude/rules/docs.md                              |   2 +-
 .claude/rules/msbuild.md                           |   4 +-
 .claude/rules/tests.md                             |   2 +-
 .claude/settings.json                              |  43 +++++--
 .claude/skills/_local.git-commit/SKILL.md          |  12 +-
 .../scripts/lib/Read-Session.ps1                   |   2 +-
 .../skills/_local.nuget-package-update/SKILL.md    |   2 +-
 .claude/skills/_local.post-issue-comment/SKILL.md  |  16 ++-
 .../scripts/Set-AcceptanceChecks.ps1               | 131 +++++++++++++++++++++
 .claude/skills/_local.pull-request/SKILL.md        |   6 +-
 CLAUDE.md                                          |  27 ++++-
 14 files changed, 288 insertions(+), 51 deletions(-)
```

## Start context by area (bench-base, client claude-desktop)

What the first model call consists of, so a change in start context can be traced to an area. Token sizes are estimates; the last row is the exact figure reported by the API.

<details><summary>Area table, MCP servers, skills and instruction files</summary>

| Area | Tokens (est.) | Share of start context | Δ vs previous run |
|---|---|---|---|
| Built-in tool definitions | 31792 | 71 % | 0 |
| MCP tool definitions (loaded) | 0 | 0 % | 0 |
| System prompt | 1720 | 4 % | +112 |
| CLAUDE.md | 2529 | 6 % | +807 |
| Memory index | 68 | 0 % | 0 |
| Skill list | 6163 | 14 % | +59 |
| Agent list | 1489 | 3 % | +162 |
| Deferred tool names | 55 | 0 % | 0 |
| MCP server instructions | 0 | 0 % | 0 |
| Not accounted for (reminders, environment, git status, estimate error) | 763 | 2 % | |
| **FirstCtx (API)** | **44579** | 100% | +2172 |

Largest tool definitions: Artifact=14472; PowerShell=4378; Workflow=2344; ScheduleWakeup=2099; Agent=1330; Bash=1310

### MCP servers

None: the run started its sessions without MCP servers, so this start context is what the repository itself loads.

### Skills, agents and instruction files

- Skills listed: 50, 6163 tokens. By source (count/tokens): local=8/785; other=33/3462; plugin=9/1672. Largest: dataviz=385; anthropic-skills:docs=274; anthropic-skills:pptx=262; anthropic-skills:google-workspace=260; anthropic-skills:xlsx=259.
- Agent types listed: 12, 1489 tokens.
- Instruction files loaded at start (tokens, est.): CLAUDE.md=2529; MEMORY.md=68 (CLAUDE.md 106 lines, memory index 2 lines).

</details>

## History

Start context, end context, added tokens and score of each fixture over the latest 8 runs that contain it; use it to see whether a number is stable over time.

<details><summary>History tables per fixture</summary>

### bench-base

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-10-03-09-14_baseline-of-the-initial-harness | 574b07ad | no | 3 | 42407 | 42407 | 0 |  |
| 2026-10-06-18-57_workflows-and-skills-improvements | 4d117ea2 | no | 3 | 44579 | 44579 | 0 |  |

### quality-prd-first

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-10-03-09-14_baseline-of-the-initial-harness | 574b07ad | no | 3 | 7981 | 14933 | 6952 | 4/4 |
| 2026-10-06-18-57_workflows-and-skills-improvements | 4d117ea2 | no | 3 | 9762 | 24971 | 15209 | 3.83/4 |

</details>


