---
name: git-commit
description: 'Create Git commits following this repository''s branch and commit-message conventions. Use ALWAYS when the user asks to commit, commit changes, make a commit or write a commit message ("commit", "закоммить"), and for the commit step of /implement-issue. Chooses the format by whether the work belongs to a GitHub issue. Covers committing only — pull requests live in the open-pr skill.'
argument-hint: 'Optional: issue number, or leave empty for a commit not tied to an issue'
---

# git-commit

Use this skill whenever a commit is made in this repository. **Commit mechanics only — nothing about pull requests.**

Only commit when the user has asked, or when a calling workflow already holds the user's go-ahead. Never push unless explicitly asked.

## Choose the branch first

Work for issue `<n>` happens on branch `<n>-<slug>` — the issue number, a hyphen and a short kebab-case slug of the English title (`2-solution-structure`).

- **Never commit directly to `main`.**
- `git branch --show-current` — if it already starts with `<n>-`, commit there.
- `git branch --list '<n>-*'` — if the branch exists but is not checked out, switch to it.
- If you are on `main` and the commit belongs to an issue, create the branch from an up-to-date `main`: run `git pull --ff-only` first (stop and report if it fails — diverged history or local conflicts), then `git switch -c <n>-<slug>`. Uncommitted changes travel with you. Ask the user first unless a calling workflow already decided it.

## Commit message

All commit text is **English** (NFR-7).

### Case 1 — the commit belongs to an issue

```
#<n> <exact issue title>

- <first action>
- <second action>
```

- **Line 1:** `#`, the issue number, one space, the exact issue title as on GitHub (`gh issue view <n> --json title --jq .title`). It is identical on every commit of the branch. In a batch (`/implement-issues`) the branch holds one commit per issue, each with its own issue's `#<n> <title>`, on a branch named `<first>-<last>-<short-slug>`.
- **Line 2:** blank.
- **From line 3:** one action per line, each starting with `- `, imperative mood ("Add", "Fix", "Update", "Remove"), contiguous — no blank lines between them. A single-action commit has one bullet.

Example:

```
#2 F0.1: Solution and project structure

- Add GitHubBackup.slnx with the eleven projects from PRD 8.1
- Add Directory.Build.props with net10.0, nullable and warnings as errors
- Add Directory.Packages.props with the test package versions
```

### Case 2 — the commit is not tied to an issue

All lines are actions starting with `- `, contiguous, no header line:

```
- Fix typo in README build section
- Update .gitignore for Rider files
```

### Rules for both cases

- No trailers or signatures (`Co-Authored-By`, "Generated with…"); attribution is disabled for this repository.
- Pass the message with a single `-m` containing real newlines (or `-F <file>`), never several `-m` flags — git inserts blank lines between them and breaks the list.

## Stage the changes

- Review first: `git status --short` and `git diff --stat`; read per-file diffs only where you need them to name the actions.
- Stage the intended files explicitly with `git add <paths>`. Never stage unrelated or in-progress files without confirmation; never `git add -A` blindly.
- If a plan for the issue exists under `docs/plans/` (`*-<n>-*.md`), stage it in the **same** commit, with its checklist ticked to match the work.

## Inline or delegated

Run this skill **inline** when the user asks for a commit directly. Inside `/implement-issue` and `/implement-issues` the whole skill — branch, staging, message, commit and the check — runs in the `skill-runner` agent: the caller hands it this skill's name, the compact facts (issue number and exact title, one line per changed file), the exact files to stage, the branch and a scratch file path for the message. If you *are* the skill-runner, do not delegate again.

## Procedure

1. Determine the case (branch name, workflow context, or ask for the issue number).
2. Check or create the branch as above.
3. Review and stage.
4. Compose the message and write it to a scratch file outside the repository (the session's scratch directory, or the system temp folder).
5. `git commit -F <file>` — a successful exit is the confirmation. Do not push.
6. Check `git log -1 --format=%B` against the format above. On a deviation fix the commit just made with `git commit --amend -F <file>`; never amend an earlier or a pushed commit.
7. Report the short SHA and the first line.
