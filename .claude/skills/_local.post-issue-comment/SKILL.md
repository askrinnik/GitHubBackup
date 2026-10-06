---
name: post-issue-comment
description: 'Post an English summary comment on a GitHub issue for the work just completed — a root-cause analysis for a bug, or an implementation summary for a feature/chore/test issue. Use when recording an RCA or implementation comment on an issue after a fix, or as the comment step of /implement-issue, immediately before the pull request.'
argument-hint: '[issue number] (optional — inferred from the current branch <n>-<slug>)'
---

# post-issue-comment

Add a comment to a GitHub issue summarizing the work just done. The comment is the durable record on the issue; the pull-request description is built from it.

## Target issue

- Use the number given by the argument or the calling workflow; otherwise take it from the current branch name `<n>-<slug>`. If still ambiguous, ask.
- Read the issue title and labels only if you do not already have them (`gh issue view <n> --json title,labels`).
- The lane is **Bug** when the issue has the `bug` label, otherwise **Feature** (covers `type:feature`, `type:chore`, `type:test`).

## Format

English (NFR-7), GitHub Markdown. Base it strictly on what was actually changed and verified — never invent details. No screenshots, no local absolute paths, no secrets.

- Take every number, name and list from the facts you work from, verbatim: do not recount, merge, reattribute or extend them. If something looks inconsistent, keep it as given and point it out instead of correcting it.
- The *Acceptance Criteria* table has one row per acceptance item of the issue — exactly those items, in their order; never add rows.

**Bug lane:**

```markdown
## Root Cause Analysis
<one or two paragraphs: the underlying defect, not the symptom, naming the types/methods involved>

## Resolution
<one intro sentence>

**Changes**
- `path/File.cs` — <what changed there>

**Key note:** <non-obvious decision or gotcha, if any>

## Verification
<table or bullets: test(s) reproducing the bug now passing, other tests, build>
```

**Feature lane:**

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

## Posting

Write the body to a scratch file outside the repository (the session's scratch directory, or the system temp folder) and post it after the user's go-ahead:

```
gh issue comment <n> --body-file <file>
```

The command prints the comment URL — report it; that is the confirmation. Do not re-read the issue to verify.

## Acceptance boxes

After the comment, and after the user's go-ahead, tick the verified items of `## Критерии приёмки` in the issue body with the script — one call, no reading or rewriting of the body by hand:

```
pwsh -NoProfile -File .claude/skills/_local.post-issue-comment/scripts/Set-AcceptanceChecks.ps1 -Issue <n> -Items 1,2,4
```

- `-Items` are the positions of the verified checkboxes within that section, 1-based, in document order, as one comma-separated value.
- The script changes only those `- [ ]` marks and refuses to write if anything else in the body would differ; it never unticks.
- Its output lists every checkbox with `ticked now`, `already ticked` or `left unticked` — name the unticked ones to the user. `-DryRun` shows the result without editing.

## Inline or delegated

Run this skill inline when the user asks for a comment directly. Inside `/implement-issue` and `/implement-issues`, after the user's go-ahead, the whole skill — composing and posting — runs in the `skill-runner` agent: the caller hands it this skill's name, the compact facts (issue number and title, lane, changed files with one line each, acceptance items with how each was verified, build and test results, the PR URL if it exists) and a scratch file path for the body; it posts the comment and returns its URL. If you *are* the skill-runner, do not delegate again.
