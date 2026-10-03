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

Write the body to a scratch file and post it after the user's go-ahead:

```
gh issue comment <n> --body-file <file>
```

The command prints the comment URL — report it; that is the confirmation. Do not re-read the issue to verify.

## Composing — inline or delegated

Compose inline by default. Inside `/implement-issue` delegate to the `skill-runner` agent with this skill's name and the compact facts (issue number and title, lane, changed files with one line each, acceptance items with how each was verified, build and test results); it returns the text and you post it.
