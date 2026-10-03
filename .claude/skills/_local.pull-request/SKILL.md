---
name: pull-request
description: 'Open a GitHub pull request following this repository''s conventions — target branch, title, the Closes keyword and what the description should contain. Use whenever creating or updating a PR, on its own or as the PR step of /implement-issue. Covers pull requests only — commit mechanics live in the git-commit skill.'
argument-hint: 'Optional: the issue number the PR resolves'
---

# pull-request

How to open a pull request in this repository. **Pull-request mechanics only.**

Never create, update or merge a PR without the user's explicit go-ahead.

## Tooling

Use the `gh` CLI (`gh pr create`, `gh pr view`, `gh pr checks`). Write the description to a file and pass it with `--body-file`; do not inline long text in the command line.

## Target, title and linkage

- **Source:** the issue branch `<n>-<slug>`, pushed (`git push -u origin <branch>`) after the user's go-ahead.
- **Target:** `main`.
- **Title:** `#<n> <exact issue title>` — the same first line as the commits.
- **Linkage:** the description contains `Closes #<n>` on its own line, so GitHub links the issue and closes it when the PR is merged. One PR resolves one issue.
- **Merge:** the user merges with a merge commit (not squash, not rebase). Do not merge unless the user explicitly asks.
- After creating, report the PR URL. A successful `gh pr create` is its own confirmation — do not re-read the PR to verify the title.

## Description

English (NFR-7), Markdown. In priority order, when space is tight drop from the end:

1. **What and why** — for a bug: symptom, root cause, why this fix; for a feature: what it does and the non-obvious decisions a reviewer cannot infer from the diff.
2. **Reviewer attention** — risky paths, behaviour changes, residual risks, deviations from the plan, PRD changes made.
3. **Verification** — tests, build, manual checks, in two or three lines.
4. `Closes #<n>` and a link to the plan file (`docs/plans/…`) if there is one.

Do **not** repeat what the issue comment already holds (the full RCA or acceptance table and file list) — the description is built *from* that comment, condensed, and links to it. Keep it under about 3000 characters; GitHub's own limit is far higher, but reviewers are not.

## After opening

- Check CI once with `gh pr checks <pr>`; if it is still running, say so and stop — the desktop app can watch CI; do not poll in a loop.
- If CI fails, report the failing job and the first error lines (`gh run view <run> --log-failed`, captured to a file and grepped), and fix it on the same branch after the user agrees.

## Composing — inline or delegated

Compose inline by default. Inside `/implement-issue` delegate to the `skill-runner` agent with this skill's name, the issue number and exact title, and the posted issue comment; it returns the title and description. You run the `gh` commands yourself after the user's go-ahead.
