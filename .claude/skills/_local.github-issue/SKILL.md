---
name: github-issue
description: 'Read a GitHub issue of GitHubBackup (body, comments, related issues, PRD links) and post an English summary comment for the work just completed — a root-cause analysis for a bug, or an implementation summary for a feature, chore or test issue. Use whenever a workflow starts from a GitHub issue number or needs to record the result of the work on that issue, including the comment step of /implement-issue.'
argument-hint: '[issue number] (optional — inferred from the current branch <n>-<slug>)'
---

# github-issue

Repository: **`askrinnik/GitHubBackup`**. Issue reads and writes go through the `gh` CLI.

## When to Use

- A workflow is driven by an issue number (for example `/implement-issue 12`).
- You need to record what was done back on the issue as a comment. The comment is the durable record on the issue; the pull-request description is built from it.

## Reading an issue

Given an issue number `<n>` (from the argument or the calling workflow; otherwise from the current branch name `<n>-<slug>`; if still ambiguous, ask):

1. Fetch the issue in one call: `gh issue view <n> --json number,title,state,labels,milestone,body,comments,assignees`.
2. Read the **comments** — requirements are frequently refined there, not in the body.
3. If the issue links a parent or has sub-issues, read them too so the scope boundary is clear: work on **this** issue only, not the whole parent.
4. If the issue is **closed**, stop and confirm with the user before doing anything.
5. Note the PRD sections and requirement ids the issue links (`## Источник требований`), its acceptance criteria (`## Критерии приёмки`) and its dependencies (`## Зависимости`).

### Determining the lane by label

The labels select the lane of the `implement-issue` workflow:

- **Bug lane** — labelled `bug`: a defect to reproduce and fix at its root cause.
- **Test-authoring lane** — the issue asks only for tests of behaviour that already exists, typically labelled `type:test`. A change to CI workflows, configuration or documentation is not test-authoring, even when labelled `type:test`: it takes the Feature lane.
- **Feature lane** — everything else (`type:feature`, `type:chore`, or an unlabelled task).

If the labels and the issue text disagree, stop and confirm the lane with the user.

### Establishing the acceptance criteria

- Restate the requirement in a sentence or two: what is true when this issue is done.
- Turn it into an explicit, checkable list — one line per observable behaviour: the issue's `## Критерии приёмки` verbatim, plus any observable behaviour the PRD requires and the issue omits (marked as added).
- **Flag gaps rather than inventing them.** If something material is undefined in both the issue and the PRD, ask the user before proceeding. Make routine naming and structure calls yourself.

## Posting the result comment

English (NFR-7), GitHub Markdown. Base it strictly on what was actually changed and verified — never invent details. No screenshots, no local absolute paths, no secrets. Do not restate the full issue; report only the outcome.

- Take every number, name and list from the facts you work from, verbatim: do not recount, merge, reattribute or extend them. If something looks inconsistent, keep it as given and point it out instead of correcting it.
- The *Acceptance Criteria* table has one row per acceptance item of the issue — exactly those items, in their order; never add rows.

**Bug lane:**

```markdown
## Root Cause
<one or two paragraphs: the underlying defect, not the symptom, naming the types/methods involved>

## Resolution
<one intro sentence>

**Changes**
- `path/File.cs` — <what changed there>

**Key note:** <non-obvious decision or gotcha, if any>

## Verification
<table or bullets: test(s) reproducing the bug now passing, other tests, build>
```

**Feature lane and Test-authoring lane:**

```markdown
## Implementation
<one intro sentence>

**Changes**
- `path/File.cs` — <what changed there>

**Key note:** <non-obvious decision or gotcha, if any>

## Acceptance Criteria
| Criterion | How it is satisfied |
|---|---|
| <criterion from the issue> | <test name, command or behaviour> |

## Verification
<tests run and results, `dotnet format` check, build>
```

Add `**PRD:** updated to <version> — <what changed>` under *Key note* when the work changed `docs/PRD.md`. Mention the plan file (`docs/plans/…`) when there is one.

Write the body to a scratch file outside the repository (the session's scratch directory, or the system temp folder) and post it after the user's go-ahead:

```
gh issue comment <n> --body-file <file>
```

The command prints the comment URL — report it; that is the confirmation. Do not re-read the issue to verify.

## Acceptance boxes

After the comment, and after the user's go-ahead, tick the verified items of the issue's acceptance section in the issue body with the script — one call, no reading or rewriting of the body by hand:

```
pwsh -NoProfile -File .claude/skills/_local.github-issue/scripts/Set-AcceptanceChecks.ps1 -Issue <n> -Items 1,2,4
```

- The acceptance section is the first `## Критерии приёмки`, `## Acceptance criteria` or `## Acceptance` heading; a note in parentheses after the heading is allowed.
- `-Items` are the positions of the verified checkboxes within that section, 1-based, in document order, as one comma-separated value.
- The script changes only those `- [ ]` marks and refuses to write if anything else in the body would differ; it never unticks.
- Its output lists every checkbox with `ticked now`, `already ticked` or `left unticked` — name the unticked ones to the user. `-DryRun` shows the result without editing.
- Skip this when the body has no checklist.

## Inline or delegated

Run this skill inline when the user asks for a comment directly. Inside `/implement-issue` and `/implement-issues`, after the user's go-ahead, *Posting the result comment* — composing and posting — runs in the `skill-runner` agent: the caller hands it this skill's name, the compact facts (issue number and title, lane, changed files with one line each, the acceptance items with how each was verified or the root cause, build and test results, the PR URL if it exists) and a scratch file path for the body; it posts the comment and returns its URL. Reading the issue and ticking the acceptance boxes stay with the caller. If you *are* the skill-runner, do not delegate again.
