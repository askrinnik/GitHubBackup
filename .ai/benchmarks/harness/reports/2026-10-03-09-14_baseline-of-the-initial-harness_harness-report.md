# Harness report: 2026-10-03-09-14_baseline-of-the-initial-harness

First run in the history: nothing to compare with.

- **Note:** baseline of the initial harness
- **Setup:** commit 574b07ad, uncommitted changes at start: no; Claude Code 2.1.284, model Sonnet 5.5, effort medium, harness 3, MCP servers: none.
- **Fixtures** (one test each): bench-base, quality-prd-first.
- **Compared with:** nothing (first run).

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
| bench-base (1) | 42407 | 42407 | 0 | 1 | 4 | 53028 | all built-in<br>(16 tools, 31792 tokens) | none |
| quality-prd-first (3) | 7981 | 14933 | 6952 (6413-7731) | 3.67 (3-4) | 2612 (2452-2696) | 29874 (27189-31355) | Read<br>Grep<br>Glob | docs=336 |

<details><summary>How the cost units were calculated</summary>

Tokens by kind (averages over the repeats) multiplied by their weight. Weights: new input 1, cache read 0.1, cache write 1.25, output 5 per token.

| Fixture | New input (x 1) | Cache read (x 0.1) | Cache write (x 1.25) | Output (x 5) | Cost units (recomputed) | Cost units (stored) |
|---|---|---|---|---|---|---|
| bench-base | 2 | 0 | 42405 | 4 | 53028 | 53028 |
| quality-prd-first | 7.33 | 29751 | 11064 | 2612 | 29874 | 29874 |

Example, bench-base (first repeat): 2 x 1 + 0 x 0.1 + 42405 x 1.25 + 4 x 5 = 53028.

</details>

## Summary

One line per fixture: what was measured, the comparison with the previous run, and the conclusion after the arrow.

- **bench-base**: **start context**: 42407 tokens in the first model call, before any work (tools, system prompt, CLAUDE.md, memory index, skill list); every later call re-reads it. No earlier run to compare with.
- **quality-prd-first**: one session adds 6952 (6413-7731) tokens on average (range over 3 repeats) in 3.67 model calls on average; this covers the files the model chose to read, the rules loaded for them and its answer. No earlier run to compare with. Judge: 4 of 4 checklist items found, 0 false positive(s).

## Quality (judged fixtures)

Look at Verdict: `within noise` means the review quality did not change. Score is the average number of checklist items the judge found, with the lowest and highest repeat; false positives are summed over the repeats.

| Fixture | Score (mean, min-max) | Maximum | False positives (previous run) | Previous mean score | Δ | Verdict |
|---|---|---|---|---|---|---|
| quality-prd-first | 4 (4-4) | 4 | 0 | | | no previous run |

### Repeats that need a look

None: every repeat scored the maximum with no false positives.

<details><summary>Every repeat with the judge's notes and a link to the answer</summary>

| Fixture | Repeat | Score | False positives | Judge notes | Answer |
|---|---|---|---|---|---|
| quality-prd-first | 1 | 4/4 | 0 | Item 1: states --since is not in the PRD and the PRD must change before code. Item 2: names FR-9.2, the version bump and the change-history row. Item 3: raises several open decisions (pushed_at as the compared date, --repo interaction, Skipped status, date format and time zone). Item 4: ends by asking the user to approve the PRD change. No false positives; the invented behaviour is marked as a proposal. | [answer](../runs/2026-10-03-09-14_baseline-of-the-initial-harness_quality-prd-first-1.md) |
| quality-prd-first | 2 | 4/4 | 0 | States up front that the PRD must change first and that --since is not in FR-9.2; names FR-9.2 plus the version bump and change-history row; raises several decisions (date format/time zone, Skipped status and exit code, --repo interaction, wiki); ends by asking the user to answer the questions before drafting the PRD change. The long implementation section is clearly labeled as post-approval, and I found no false claims. | [answer](../runs/2026-10-03-09-14_baseline-of-the-initial-harness_quality-prd-first-2.md) |
| quality-prd-first | 3 | 4/4 | 0 | Item 1: states --since isn't in the PRD and the PRD changes first, with no code until approval. Item 2: names FR-9.2, the version bump and the change-history row. Item 3: raises many decisions (pushed_at, --repo/--account, Skipped status and FR-9.8, time zone, discovery). Item 4: ends by asking the user to confirm or accept suggestions. The implementation plan is clearly marked as post-approval, and no false claims were found. | [answer](../runs/2026-10-03-09-14_baseline-of-the-initial-harness_quality-prd-first-3.md) |

</details>

## Instruction files that differ from the most recent earlier run

Not applicable (first run).

## Start context by area (bench-base, client claude-desktop)

What the first model call consists of, so a change in start context can be traced to an area. Token sizes are estimates; the last row is the exact figure reported by the API.

<details><summary>Area table, MCP servers, skills and instruction files</summary>

| Area | Tokens (est.) | Share of start context | Δ vs previous run |
|---|---|---|---|
| Built-in tool definitions | 31792 | 75 % |  |
| MCP tool definitions (loaded) | 0 | 0 % |  |
| System prompt | 1608 | 4 % |  |
| CLAUDE.md | 1722 | 4 % |  |
| Memory index | 68 | 0 % |  |
| Skill list | 6104 | 14 % |  |
| Agent list | 1327 | 3 % |  |
| Deferred tool names | 55 | 0 % |  |
| MCP server instructions | 0 | 0 % |  |
| Not accounted for (reminders, environment, git status, estimate error) | -269 | -1 % | |
| **FirstCtx (API)** | **42407** | 100% |  |

Largest tool definitions: Artifact=14472; PowerShell=4378; Workflow=2344; ScheduleWakeup=2099; Agent=1330; Bash=1310

### MCP servers

None: the run started its sessions without MCP servers, so this start context is what the repository itself loads.

### Skills, agents and instruction files

- Skills listed: 49, 6104 tokens. By source (count/tokens): local=8/785; other=32/3411; plugin=9/1665. Largest: dataviz=385; anthropic-skills:docs=267; anthropic-skills:pptx=262; anthropic-skills:google-workspace=260; anthropic-skills:xlsx=259.
- Agent types listed: 11, 1327 tokens.
- Instruction files loaded at start (tokens, est.): CLAUDE.md=1722; MEMORY.md=68 (CLAUDE.md 91 lines, memory index 2 lines).

</details>

## History

Start context, end context, added tokens and score of each fixture over the latest 8 runs that contain it; use it to see whether a number is stable over time.

<details><summary>History tables per fixture</summary>

### bench-base

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-10-03-09-14_baseline-of-the-initial-harness | 574b07ad | no | 3 | 42407 | 42407 | 0 |  |

### quality-prd-first

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-10-03-09-14_baseline-of-the-initial-harness | 574b07ad | no | 3 | 7981 | 14933 | 6952 | 4/4 |

</details>


