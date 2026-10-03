---
name: next-issue
description: 'Recommend the next GitHub issue to implement: the first open issue (by phase and F-number) whose blocking issues are all closed and that has no open pull request yet, plus the issues that become ready once pending pull requests merge. Use when the user asks what to do next, which issue to take, at the end of /implement-issue, and when /implement-issue is started without an issue number.'
argument-hint: 'Optional: issue numbers to treat as closed (for example the one just shipped)'
---

# next-issue

Recommends what to implement next, from the issue dependencies recorded on GitHub.

## How the order is defined

- **Dependencies** are GitHub "blocked by" relations between issues (mirrored in the `## Зависимости` section of each issue body). An issue closes only when its pull request is merged, so "closed" means "its code is in `main`".
- **Order** among ready issues follows the F-code in the title: phase first, then number (`F0.1`, `F0.2`, … `F1.1`, …). Issues without an F-code go last.
- An issue with an **open pull request** is in progress and is never recommended.

## Procedure

1. Run, from the repository root:

   ```
   pwsh -NoProfile -File .claude/skills/_local.next-issue/scripts/Get-NextIssue.ps1 [-AssumeClosed <n>[,<n>…]] [-Top 3]
   ```

   Pass `-AssumeClosed <n>` for an issue whose pull request was just opened but is not merged yet — `/implement-issue` does this for the issue it has just shipped.

2. Present the result in a few lines:
   - the recommended issue: number, title, milestone, and the ready-to-paste command `/implement-issue <n>`;
   - up to two alternatives that are also ready;
   - issues that become ready after a pending PR merges, naming the PR;
   - if the recommendation depends on an assumed-closed issue, say that its PR must be merged first.

3. Recommend starting the next issue in a **new session** (`/clear` or a new chat), after merging the pending PR, so the next issue starts from an up-to-date `main` and a small context.

## Notes

- The script makes one GraphQL query through `gh`; it needs no other tool.
- If an issue body lists a dependency that has no "blocked by" relation (or the reverse), report the mismatch — the two must agree. Fixing it (`gh api -X POST repos/<owner>/<repo>/issues/<n>/dependencies/blocked_by -F issue_id=<id of the blocker>`) needs the user's go-ahead.
- When the user creates a new issue, set both the `## Зависимости` section and the "blocked by" relations, or the order breaks silently.
