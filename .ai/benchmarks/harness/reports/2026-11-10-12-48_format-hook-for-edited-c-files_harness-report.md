# Harness report: 2026-11-10-12-48_format-hook-for-edited-c-files

**Overall: worse in bench-base** (see Summary for what moved and why).

- **Note:** Format hook for edited C# files
- **Setup:** commit dceca651, uncommitted changes at start: yes; Claude Code 2.1.319, model Sonnet 5.5, effort medium, harness 3, MCP servers: none.
- **Fixtures** (one test each): bench-base.
- **Compared with:** the most recent earlier run, `2026-10-07-16-44_parity-with-addressbook2025-test-authori` (commit 41cb2300); each fixture is compared with the previous run that contains it.

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
| bench-base (1) | 38149 (Δ +4355) | 38149 (Δ +4355) | 0 (Δ 0) | 1 | 6 | 28138 | all built-in<br>(15 tools, 18914 tokens) | none |

<details><summary>How the cost units were calculated</summary>

Tokens by kind (averages over the repeats) multiplied by their weight. Weights: new input 1, cache read 0.1, cache write 1.25, output 5 per token.

| Fixture | New input (x 1) | Cache read (x 0.1) | Cache write (x 1.25) | Output (x 5) | Cost units (recomputed) | Cost units (stored) |
|---|---|---|---|---|---|---|
| bench-base | 2 | 17024 | 21123 | 6 | 28138 | 28138 |

Example, bench-base (first repeat): 2 x 1 + 17024 x 0.1 + 21123 x 1.25 + 6 x 5 = 28138.

</details>

## Summary

One line per fixture: what was measured, the comparison with the previous run, and the conclusion after the arrow.

- **bench-base**: **start context**: 38149 tokens in the first model call, before any work (tools, system prompt, CLAUDE.md, memory index, skill list); every later call re-reads it. Previous run: 33794, so +4355. Where it moved: built-in tool definitions +1294; system prompt +73; claude.md +223; memory index +67; skill list +1109; agent list +526; not attributable to any area (reminders, environment, git status) +1062; this run had uncommitted changes, whose file list is part of the start context and part of the unattributed difference (about 700 tokens per 30 files). **-> tokens worse (more tokens).**

## Instruction files that differ from the most recent earlier run

Changes in rules, agents, skills, `CLAUDE.md` and settings from `41cb2300` to the working tree at report time (this run had uncommitted changes) :

```
 .ai/prompts/implement-issue.md                     | 12 +++++++++-
 .ai/prompts/implement-issues.md                    | 19 +++++++++-------
 .claude/commands/implement-issue.md                |  2 +-
 .claude/commands/implement-issues.md               |  2 +-
 .claude/rules/cli.md                               |  2 +-
 .claude/rules/github-actions.md                    |  2 +-
 .claude/rules/msbuild.md                           |  2 +-
 .claude/rules/tests.md                             |  4 +++-
 .claude/settings.json                              | 26 ++++++++++++++++++++++
 .claude/skills/_local.git-commit/SKILL.md          | 10 ++++-----
 .../skills/_local.nuget-package-update/SKILL.md    |  2 +-
 .claude/skills/_local.write-tests/SKILL.md         |  4 ++--
 CLAUDE.md                                          | 10 +++++----
 13 files changed, 70 insertions(+), 27 deletions(-)
```

## Start context by area (bench-base, client claude-desktop)

What the first model call consists of, so a change in start context can be traced to an area. Token sizes are estimates; the last row is the exact figure reported by the API.

<details><summary>Area table, MCP servers, skills and instruction files</summary>

| Area | Tokens (est.) | Share of start context | Δ vs previous run |
|---|---|---|---|
| Built-in tool definitions | 18914 | 50 % | +1294 |
| MCP tool definitions (loaded) | 0 | 0 % | 0 |
| System prompt | 1793 | 5 % | +73 |
| CLAUDE.md | 2819 | 7 % | +223 |
| Memory index | 135 | 0 % | +67 |
| Skill list | 7635 | 20 % | +1109 |
| Agent list | 2037 | 5 % | +526 |
| Deferred tool names | 48 | 0 % | +1 |
| MCP server instructions | 0 | 0 % | 0 |
| Not accounted for (reminders, environment, git status, estimate error) | 4768 | 12 % | |
| **FirstCtx (API)** | **38149** | 100% | +4355 |

Largest tool definitions: PowerShell=4541; Workflow=2344; Agent=2280; ScheduleWakeup=2099; Bash=1681; Grep=1010

### MCP servers

None: the run started its sessions without MCP servers, so this start context is what the repository itself loads.

### Skills, agents and instruction files

- Skills listed: 63, 7635 tokens. By source (count/tokens): local=8/839; other=42/4026; plugin=13/2527. Largest: dataviz=385; anthropic-skills:docs=274; harness-kit=268; anthropic-skills:pptx=262; anthropic-skills:google-workspace=260.
- Agent types listed: 13, 2037 tokens.
- Instruction files loaded at start (tokens, est.): CLAUDE.md=796; CLAUDE.md=2819; MEMORY.md=135 (CLAUDE.md 108 lines, memory index 4 lines).

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

</details>

## Caveats

- The run had uncommitted changes: their file list is part of the start context and the commit id alone does not identify the instructions that were tested. Applies to: all fixtures.

