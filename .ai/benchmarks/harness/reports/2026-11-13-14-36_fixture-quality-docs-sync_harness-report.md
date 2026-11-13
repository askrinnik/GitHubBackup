# Harness report: 2026-11-13-14-36_fixture-quality-docs-sync

**Overall: no regressions.** 1 of 1 fixtures were compared with `2026-11-13-12-23_m2b-bench-fixtures-on-main-after-the-iss` and none changed beyond the noise.

- **Note:** Fixture quality-docs-sync
- **Setup:** commit ff6733fb, uncommitted changes at start: yes; Claude Code 2.1.319, model Sonnet 5.5, effort medium, harness 3, MCP servers: none.
- **Fixtures** (one test each): quality-docs-sync.
- **Compared with:** the most recent earlier run, `2026-11-13-12-23_m2b-bench-fixtures-on-main-after-the-iss` (commit dc128379); each fixture is compared with the previous run that contains it.

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
| quality-docs-sync (3) | 11775 | 31937 | 20162 (13313-25065) | 4.67 (4-5) | 3268 (2565-4019) | 53855 (35914-63044) | Read<br>Grep<br>Glob | cli=614<br>csharp=2797<br>docs=484<br>security=1085<br>update-docs-on-code-change=460 |

<details><summary>How the cost units were calculated</summary>

Tokens by kind (averages over the repeats) multiplied by their weight. Weights: new input 1, cache read 0.1, cache write 1.25, output 5 per token.

| Fixture | New input (x 1) | Cache read (x 0.1) | Cache write (x 1.25) | Output (x 5) | Cost units (recomputed) | Cost units (stored) |
|---|---|---|---|---|---|---|
| quality-docs-sync | 12.67 | 87404 | 23011 | 3268 | 53855 | 53855 |

Example, quality-docs-sync (first repeat): 14 x 1 + 86151 x 0.1 + 30656 x 1.25 + 3219 x 5 = 63044.

</details>

## Summary

One line per fixture: what was measured, the comparison with the previous run, and the conclusion after the arrow.

- **quality-docs-sync**: one session adds 20162 (13313-25065) tokens on average (range over 3 repeats) in 4.67 model calls on average; this covers the files the model chose to read, the rules loaded for them and its answer. No earlier run to compare with. Judge: 4 of 4 checklist items found, 0 false positive(s).

## Quality (judged fixtures)

Look at Verdict: `within noise` means the review quality did not change. Score is the average number of checklist items the judge found, with the lowest and highest repeat; false positives are summed over the repeats.

| Fixture | Score (mean, min-max) | Maximum | False positives (previous run) | Previous mean score | Δ | Verdict |
|---|---|---|---|---|---|---|
| quality-docs-sync | 4 (4-4) | 4 | 0 | | | no previous run |

### Repeats that need a look

None: every repeat scored the maximum with no false positives.

<details><summary>Every repeat with the judge's notes and a link to the answer</summary>

| Fixture | Repeat | Score | False positives | Judge notes | Answer |
|---|---|---|---|---|---|
| quality-docs-sync | 1 | 4/4 | 0 | All four items found: FR-9.2 row for --no-color, §7.2 example and validation rule for CompressionLevel, version header plus change history, and README status line plus command table. The documents named as not needing updates match the ground truth, and the answer makes no false statements about the documents. | [answer](../runs/2026-11-13-14-36_fixture-quality-docs-sync_quality-docs-sync-1.md) |
| quality-docs-sync | 2 | 4/4 | 0 | All four items found: FR-9.2 row for --no-color, §7.2 example and validation rule (0–9), version bump with change history row (PRD first noted), README status line and command table. No false positives: the other documents were correctly excluded and the README was correctly said not to list the Backup keys. | [answer](../runs/2026-11-13-14-36_fixture-quality-docs-sync_quality-docs-sync-2.md) |
| quality-docs-sync | 3 | 4/4 | 0 | All four items found: FR-9.2 row for --no-color, §7.2 example plus validation rule for CompressionLevel, PRD version bump with history row, and README command table plus status line. The 'do not update' list (CLAUDE.md, docs/ai/README.md, docs/plans) is correct, and the README claim about Backup settings is accurate. | [answer](../runs/2026-11-13-14-36_fixture-quality-docs-sync_quality-docs-sync-3.md) |

</details>

## Instruction files that differ from the most recent earlier run

No difference in rules, agents, skills, `CLAUDE.md` or settings between `dc128379` to the working tree at report time (this run had uncommitted changes).

## History

Start context, end context, added tokens and score of each fixture over the latest 8 runs that contain it; use it to see whether a number is stable over time.

<details><summary>History tables per fixture</summary>

### quality-docs-sync

| Run | Commit | Uncommitted | Harness | Start ctx | End ctx | Added by task | Score |
|---|---|---|---|---|---|---|---|
| 2026-11-13-14-36_fixture-quality-docs-sync | ff6733fb | yes | 3 | 11775 | 31937 | 20162 | 4/4 |

</details>

## Caveats

- The run had uncommitted changes: their file list is part of the start context and the commit id alone does not identify the instructions that were tested. Applies to: all fixtures.

