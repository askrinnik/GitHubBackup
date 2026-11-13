# Harness report: 2026-11-13-17-11_wpf-automation-mcp-netwright-and-bench-x

**Overall: worse in bench-base** (see Summary for what moved and why).

- **Note:** WPF automation MCP netwright and bench-xaml
- **Setup:** commit b1ca9c87, uncommitted changes at start: yes; Claude Code 2.1.319, model Sonnet 5.5, effort medium, harness 3, MCP servers: none.
- **Fixtures** (one test each): bench-base, bench-cs, bench-test, bench-xaml.
- **Compared with:** the most recent earlier run, `2026-11-13-14-36_fixture-quality-docs-sync` (commit ff6733fb); each fixture is compared with the previous run that contains it.

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
| bench-base (1) | 38443 (Δ +85) | 38443 (Δ +85) | 0 (Δ 0) | 1 | 6 | 26704 | all built-in<br>(15 tools, 18914 tokens) | none |
| bench-cs (1) | 10013 (Δ +77) | 15753 (Δ +77) | 5740 (Δ 0) | 2 | 90 | 19223 | Read | csharp=2797<br>security=1085 |
| bench-test (1) | 10023 (Δ +77) | 16166 (Δ +77) | 6143 (Δ 0) | 2 | 101 | 18216 | Read | csharp=2797<br>tests=1351 |
| bench-xaml (1) | 10017 | 13867 | 3850 | 2 | 93 | 15302 | Read | security=1085<br>wpf-mvvm=1279 |

<details><summary>How the cost units were calculated</summary>

Tokens by kind (averages over the repeats) multiplied by their weight. Weights: new input 1, cache read 0.1, cache write 1.25, output 5 per token.

| Fixture | New input (x 1) | Cache read (x 0.1) | Cache write (x 1.25) | Output (x 5) | Cost units (recomputed) | Cost units (stored) |
|---|---|---|---|---|---|---|
| bench-base | 2 | 18591 | 19850 | 6 | 26704 | 26704 |
| bench-cs | 6 | 11681 | 14079 | 90 | 19223 | 19223 |
| bench-test | 6 | 13064 | 13119 | 101 | 18216 | 18216 |
| bench-xaml | 6 | 13058 | 10820 | 93 | 15302 | 15302 |

Example, bench-base (first repeat): 2 x 1 + 18591 x 0.1 + 19850 x 1.25 + 6 x 5 = 26704.

</details>

## Summary

One line per fixture: what was measured, the comparison with the previous run, and the conclusion after the arrow.

- **bench-base**: **start context**: 38443 tokens in the first model call, before any work (tools, system prompt, CLAUDE.md, memory index, skill list); every later call re-reads it. Previous run (`2026-11-13-12-23_m2b-bench-fixtures-on-main-after-the-iss`): 38358, so +85. Where it moved: this run had uncommitted changes, whose file list is part of the start context and part of the unattributed difference (about 700 tokens per 30 files). **-> tokens worse (more tokens).**
- **bench-cs**: reading one file adds 5740 tokens, which is the file plus the rules loaded for that file type (csharp, security) and the answer. Previous run (`2026-11-13-12-23_m2b-bench-fixtures-on-main-after-the-iss`): 5740 (0); a difference of up to 50 tokens counts as unchanged. **-> tokens unchanged.**
- **bench-test**: reading one file adds 6143 tokens, which is the file plus the rules loaded for that file type (csharp, tests) and the answer. Previous run (`2026-11-13-12-23_m2b-bench-fixtures-on-main-after-the-iss`): 6143 (0); a difference of up to 50 tokens counts as unchanged. **-> tokens unchanged.**
- **bench-xaml**: reading one file adds 3850 tokens, which is the file plus the rules loaded for that file type (security, wpf-mvvm) and the answer. No earlier run to compare with.

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
| CLAUDE.md | 2977 | 8 % | 0 |
| Memory index | 181 | 0 % | 0 |
| Skill list | 7766 | 20 % | 0 |
| Agent list | 1971 | 5 % | 0 |
| Deferred tool names | 48 | 0 % | +4 |
| MCP server instructions | 0 | 0 % | 0 |
| Not accounted for (reminders, environment, git status, estimate error) | 4793 | 12 % | |
| **FirstCtx (API)** | **38443** | 100% | +85 |

Largest tool definitions: PowerShell=4541; Workflow=2344; Agent=2280; ScheduleWakeup=2099; Bash=1681; Grep=1010

### MCP servers

None: the run started its sessions without MCP servers, so this start context is what the repository itself loads.

### Skills, agents and instruction files

- Skills listed: 61, 7766 tokens. By source (count/tokens): local=4/408; other=44/4587; plugin=13/2527. Largest: dataviz=385; harness-kit=342; anthropic-skills:docs=274; anthropic-skills:pptx=262; anthropic-skills:google-workspace=260.
- Agent types listed: 13, 1971 tokens.
- Instruction files loaded at start (tokens, est.): CLAUDE.md=796; CLAUDE.md=2977; MEMORY.md=181 (CLAUDE.md 112 lines, memory index 5 lines).

</details>

## History

Start context, end context, added tokens and score of each fixture over the latest 8 runs that contain it; use it to see whether a number is stable over time.

<details><summary>History tables per fixture</summary>

### bench-base

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-10-06-18-57_workflows-and-skills-improvements | 4d117ea2 | no | 3 | 44579 | 44579 | 0 |  |
| 2026-10-07-16-44_parity-with-addressbook2025-test-authori | 41cb2300 | no | 3 | 33794 | 33794 | 0 |  |
| 2026-11-10-12-48_format-hook-for-edited-c-files | dceca651 | yes | 3 | 38149 | 38149 | 0 |  |
| 2026-11-12-22-11_baseline-before-moving-the-issue-workflo | 9e3d625d | no | 3 | 38341 | 38341 | 0 |  |
| 2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har | 079b44ea | no | 3 | 38344 | 38344 | 0 |  |
| 2026-11-13-11-57_m2-harness-migrated-to-issue-kit | f1ac01f5 | no | 3 | 38846 | 38846 | 0 |  |
| 2026-11-13-12-23_m2b-bench-fixtures-on-main-after-the-iss | dc128379 | no | 3 | 38358 | 38358 | 0 |  |
| 2026-11-13-17-11_wpf-automation-mcp-netwright-and-bench-x | b1ca9c87 | yes | 3 | 38443 | 38443 | 0 |  |

### bench-cs

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu | 4f9c203c | yes | 3 | 9840 | 15570 | 5730 |  |
| 2026-11-12-22-11_baseline-before-moving-the-issue-workflo | 9e3d625d | no | 3 | 9856 | 15596 | 5740 |  |
| 2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har | 079b44ea | no | 3 | 9859 | 15599 | 5740 |  |
| 2026-11-13-11-57_m2-harness-migrated-to-issue-kit | f1ac01f5 | no | 3 | 10268 | 16092 | 5824 |  |
| 2026-11-13-12-23_m2b-bench-fixtures-on-main-after-the-iss | dc128379 | no | 3 | 9936 | 15676 | 5740 |  |
| 2026-11-13-17-11_wpf-automation-mcp-netwright-and-bench-x | b1ca9c87 | yes | 3 | 10013 | 15753 | 5740 |  |

### bench-test

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-11-21-15_fixtures-for-code-bench-cs-bench-test-qu | 4f9c203c | yes | 3 | 9850 | 15993 | 6143 |  |
| 2026-11-12-22-11_baseline-before-moving-the-issue-workflo | 9e3d625d | no | 3 | 9866 | 16009 | 6143 |  |
| 2026-11-13-10-38_m1-harness-kit-copy-of-the-benchmark-har | 079b44ea | no | 3 | 9869 | 16012 | 6143 |  |
| 2026-11-13-11-57_m2-harness-migrated-to-issue-kit | f1ac01f5 | no | 3 | 10278 | 16505 | 6227 |  |
| 2026-11-13-12-23_m2b-bench-fixtures-on-main-after-the-iss | dc128379 | no | 3 | 9946 | 16089 | 6143 |  |
| 2026-11-13-17-11_wpf-automation-mcp-netwright-and-bench-x | b1ca9c87 | yes | 3 | 10023 | 16166 | 6143 |  |

### bench-xaml

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-13-17-11_wpf-automation-mcp-netwright-and-bench-x | b1ca9c87 | yes | 3 | 10017 | 13867 | 3850 |  |

</details>

## Caveats

- The run had uncommitted changes: their file list is part of the start context and the commit id alone does not identify the instructions that were tested. Applies to: all fixtures.

