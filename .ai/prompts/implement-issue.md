Take a GitHub issue of `askrinnik/GitHubBackup` end to end: read it, check its dependencies and the PRD, plan, implement with tests, verify, record the outcome on the issue, open the pull request, and recommend the next issue. There are two checkpoints with the user — the plan review and the result confirmation — and every outward action (commit, push, comment, PR) needs the user's go-ahead.

The issue's labels select the **lane** at step 2. Steps marked **Bug lane** or **Feature lane** apply only to that lane; unmarked steps apply to both.

## Context budget

Every model call re-reads the whole conversation, so cost is roughly calls × context size. In measured runs of this workflow on another project, about 88% of the spend was cache reads of a context that only grew. These rules keep it small and apply to every step.

1. **Compact at milestones.** You cannot run slash commands yourself. At each milestone, give the user the ready-to-paste `/compact` command below and continue once they have run it or told you to go on without it. Whatever a later step needs must already be on disk or in the focus text.
   - **After the plan is approved and saved (end of step 7):** `/compact keep: issue number and exact title, lane, acceptance list, plan file path, decisions made during plan review, complexity`
   - **After build and tests pass (end of step 9):** `/compact keep: issue number and exact title, lane, acceptance list, plan file path, list of changed files, build and test results`
   - **After the user confirms the result (end of step 11):** `/compact keep: issue number and exact title, lane, branch, git diff --stat, one line per changed file, acceptance table with results, verification summary`
   - Skip a milestone when the conversation up to it is still short (under about 40 tool calls).
2. **Runaway guard.** If one turn passes about 100 tool calls without reaching a milestone (repeated build-and-fix loops), stop at the next natural pause and offer `/compact` with a focus text of the same kind. Never let a single turn run past about 150 tool calls.
3. **One issue per session.** After step 14, recommend a new session for the next issue instead of continuing in this one.
4. **Read narrowly.** Grep first, then read around the match. Read a whole file only when you will change most of it or it is under about 200 lines. Never re-read a file you just edited. Read only the PRD sections the issue links. For broad exploration use the `Explore` agent and ask for a summary of at most 30 lines.
5. **Keep tool output small at the source.** `-clp:ErrorsOnly` for builds; test output captured to a file and only the summary and first failures read; `git diff --stat` before any full diff, then per file; `gh … --json <fields> --jq …`.
6. **Delegate noisy work** to the agents named in this workflow (`issue-planner`, `issue-developer`, `build-runner`, `skill-runner`, `Explore`) so their raw output stays out of this conversation.
7. **Scratch paths stay outside the repository.** Every scratch directory or file you hand to `build-runner` or `skill-runner` lies in your session's scratch directory (or, without one, the system temp folder) — never under the repository root: files there show up in `git status`, can be staged by mistake, and the `rm -rf` you would need to clean them up is denied.

## 0. Start from an up-to-date `main`

The previous issue usually leaves you on its own branch, and its pull request may have been merged since. Unless the user said otherwise:

- Check the working tree (`git status --short`) and the current branch (`git branch --show-current`).
- **Uncommitted changes** → stop and report them; ask whether to commit, stash or discard them. Never switch branches with a dirty tree, never discard changes on your own.
- **Clean tree** → `git switch main` and `git pull --ff-only`, even when you are already on `main`. A stale base means building on files that have since changed.
  - If the branch you left has commits that are not in `main` (`git log main..<branch> --oneline` after the pull), mention it: its PR is probably not merged yet. That is fine — the commits stay on that branch.
  - If the fast-forward pull fails (diverged `main`), stop and report; never force.
- **Stay on the current branch only when the user explicitly says so** for this issue (for example "continue on the current branch", "don't switch", or a follow-up change to the same issue). Then skip the switch, still pull the current branch if it tracks a remote, and note that the base may be stale.
- Do all work in the working tree on `main`; do **not** create the issue branch now. It is created immediately before the first commit (step 12), from the latest `main`.

## 1. Read the issue

- If no issue number was given, run the `next-issue` skill, present its recommendation and ask which issue to take. Do nothing else until the user picks one.
- `gh issue view <n> --json number,title,state,labels,milestone,body,comments,assignees`. Requirements are sometimes refined in comments — read them.
- If the issue is closed, stop and say so.
- Note the PRD sections and requirement ids the issue links (`## Источник требований`), its task, its acceptance criteria (`## Критерии приёмки`, a `- [ ]` list) and its dependencies.

## 2. Check dependencies, determine the lane, take ownership

- **Dependencies.** Read the blocking issues: `gh api graphql` for `blockedBy`, or the numbers in `## Зависимости`. If any blocker is still open, stop: name it, say whether it has an open PR, and ask how to proceed. Do not start on top of unmerged work unless the user explicitly says so.
- **Lane.** Label `bug` → **Bug lane**: reproduce, fix the root cause, RCA comment. Any other type label (`type:feature`, `type:chore`, `type:test`) → **Feature lane**: acceptance list, plan, build, implementation comment.
- **Ownership.** Assign the issue to the user: `gh issue edit <n> --add-assignee @me` (this is the one issue edit that needs no separate confirmation; it changes no content).

## 3. Understand the problem

**Both lanes:** read the linked PRD sections (and only those). The PRD is the scope authority.

**Bug lane — reproduce:**
- Reproduce before planning a fix, preferably with a **failing test** in the layer that owns the decision (unit test; integration test on a local bare repository in a temp folder; or a CLI run against a temp folder with `file://` remotes, capturing exit code, summary and log). Follow the `debug-issue` skill.
- If it cannot be reproduced as described, report what was tried and ask how to proceed.

**Feature lane — acceptance and current state:**
- Restate the requirement in a sentence or two: what is true when this issue is done.
- Build the acceptance list: the issue's criteria verbatim, plus any observable behaviour the PRD requires and the issue omits (marked as added).
- **Flag gaps, don't invent.** If something material is undefined in both the issue and the PRD, ask before planning. Routine naming and structure calls are yours.

**PRD first.** If the issue cannot be done without behaviour the PRD does not describe, or contradicts it, stop and propose the PRD change: the text, the version bump and the history row. Implement only after the user approves; the PRD edit is part of this issue's change. If the issue text itself is out of date against the PRD, propose updating the issue after the PRD.

## 4. Draft the plan

- Delegate the research to the `issue-planner` agent. Hand it: issue number and exact title, lane, the PRD sections and requirement ids, the acceptance list (or the reproduced failure and the failing test), and any decisions already made with the user. It returns the plan text in Russian in the shape it defines, including a **complexity** of `S`, `M` or `L`.
- For a trivial change you may draft the plan inline instead, in the same shape.
- If the plan lists *Изменения PRD* or *Открытые вопросы*, resolve them with the user at the review in step 5.

## 5. Save the plan and get it reviewed

- Save the plan as `docs/plans/<type>-<n>-<slug>.md`, where `<type>` is `bug`, `feature`, `chore` or `test` from the label and `<slug>` is a short kebab-case slug of the English title. This repository file is the deliverable; a plan-mode scratch file elsewhere does not replace it. If the environment blocks writes while planning, write the file the moment writes are allowed and say so.
- Header of the file: issue link, title, lane, complexity, date.
- Show the user the path and a short summary (goal, approach, complexity, open questions) and ask for review. Do not touch application code yet.

## 6. Revise on feedback

- Apply feedback to the same plan file and present it again. Repeat until the user approves.

## 7. Approved

- Record decisions made during review in the plan file (*Решения*).
- **Milestone:** hand the user the step-7 `/compact` command.

## 8. Implement

- Delegate non-trivial work to the `issue-developer` agent. **Choose its model from the plan's complexity:** `S` or `M` → the agent's default (Sonnet); `L` → pass `model: "opus"` in the Agent call. Hand it the plan file path, the acceptance list and any decisions from the review. It implements and tests, and returns a change summary; it never commits, pushes or posts.
- For a trivial change, implement inline.
- **You keep every gate:** build, tests, verification (steps 9–10) and all confirmation and shipping steps.
- **Feature lane:** wire the whole slice the issue covers — `Core` logic, `Infrastructure` implementation, DI registration, the host (CLI command or WPF view) when the issue includes it. A half-wired feature is not done.
- **New behaviour ships with tests** in the paired test project (`write-tests` skill, `.claude/rules/tests.md`). **Bug lane:** the reproducing test from step 3 is part of the change.
- If the plan turns out wrong once in the code, say so, update the plan file, and confirm before diverging materially.
- Update documentation the change affects in the same change (`.claude/rules/update-docs-on-code-change.md`): `CLAUDE.md` build commands, `README.md`, `docs/ai-harness.md`.

## 9. Build and test

Run the gate independently of `issue-developer`, even if it reported success:

- **Build, tests, format** — one call to the `build-runner` agent (cheap model) with scope `full` and a scratch directory outside the repository. It runs a full rebuild (`-t:Rebuild`, warnings are errors), the whole solution's tests including the architecture tests, and `dotnet format --verify-no-changes`, and returns the test runner's summary lines verbatim with the first errors. Everything must be green. On a failure, hand the reported errors back to `issue-developer` (or fix a trivial cause inline) and call `build-runner` again; do not re-run the commands yourself to see the same output. Give `build-runner` a subfolder of its own inside your scratch directory (for example `<scratch>/build`), the same one on every call, so its logs stay apart from the other scratch files and a rerun overwrites them.
- **Review the comments this change adds**, including those the developer agent wrote: `git diff -U0 -- '*.cs' '*.xaml'` lines starting with `+` and containing `//`, `///` or `<!--`. Check each against the comment-hygiene rules (no change narration, no issue references, no line numbers, no repetition) and fix what fails.
- For a change in `Infrastructure`, `Cli` or the token path, run the `security-reviewer` agent on the working tree and fix CRITICAL and IMPORTANT findings.
- **Milestone:** hand the user the step-9 `/compact` command.

## 10. Verify

- **Bug lane:** the reproducing test passes; re-run the original reproduction (CLI run or scenario) and confirm the failure is gone.
- **Feature lane:** verify **every** acceptance item and record how: a named test, a command with its observed output, or a CLI run against a temp folder with local repositories (exit code, summary, files on disk, log entries). Exercise the obvious negatives the PRD defines: invalid configuration, missing tool, failing repository, cancellation, `--dry-run` changing nothing.
- UI issues: ViewModel tests and FlaUI tests per the plan; manual inspection of the running app only when the user asks for it.
- **This step is not optional.** The user's go-ahead to skip the plan review or to go straight through the workflow does not cover it, and a green test run alone does not replace the per-item record above. Skip an item only when the user explicitly says so for this issue, and name it in step 11 as not verified.
- If anything fails, go back to step 8 (or step 4 if the approach must change), then re-run steps 9–10.

## 11. Ask for confirmation

- **Bug lane:** what was wrong (root cause), what was fixed, how it was verified.
- **Feature lane:** what was implemented, and the acceptance list as a table: item → how verified → result. State explicitly anything not covered or left out.
- Ask whether the result is acceptable. If not, return to step 8 (or 4) and iterate.
- **Milestone:** once confirmed, hand the user the step-11 `/compact` command.

## 12. Commit and push

**Get the go-ahead, delegate the action.** The commit, the issue comment and the pull request are each carried out end to end by the `skill-runner` agent (cheap model, isolated context), which follows the whole skill — composes the text, runs the `git`/`gh` command and checks the result. Ask the user first; only after the go-ahead make one `skill-runner` call for that one action. Give it the skill name (`git-commit`, `post-issue-comment`, `pull-request`), the compact facts you already hold — issue number and exact title, lane, one line per changed file, the acceptance table, build and test results — the action-specific inputs named in the skill's *Inline or delegated* section, and a scratch file path for the text outside the repository — for every action, the commit included. Push, acceptance ticks and the CI check stay with you.

- **Re-sync the base.** `git fetch origin`; if `origin/main` moved since step 0, pull it (`git pull --ff-only`; the uncommitted changes travel with you) and re-run step 9 (a `build-runner` call with scope `full`) so nothing regressed against the newer base. Otherwise say it is unchanged.
- **Tick the plan** checklist in `docs/plans/…` to match the work; the plan file goes into the same commit.
- **Commit** after the user's go-ahead: one `skill-runner` call with the `git-commit` skill, the exact files to stage (including the plan file) and the branch — `<n>-<slug>`, which it creates from the up-to-date `main`, or the existing branch you stayed on at the user's request (step 0). It reports the short SHA and the first line.
- **Push** (`git push -u origin <branch>`) after the user's go-ahead.

## 13. Issue comment, acceptance boxes, pull request

- **Issue comment.** After the user's go-ahead, one `skill-runner` call with the `post-issue-comment` skill (Bug → *Root Cause Analysis / Resolution / Verification*; Feature → *Implementation / Acceptance Criteria / Verification*); it posts the comment and reports its URL.
- **Acceptance boxes.** After the user's go-ahead, tick the verified items of `## Критерии приёмки` with one call of the `Set-AcceptanceChecks.ps1` script (`post-issue-comment` skill, *Acceptance boxes*), passing their positions. Leave unverified items unticked and name them from the script's output.
- **Pull request.** After the user's go-ahead, one `skill-runner` call with the `pull-request` skill, the head branch and the posted comment; it opens the PR into `main` (title `#<n> <exact title>`, body with `Closes #<n>`) and reports the URL. Pass the URL on to the user.
- **CI.** Check once with `gh pr checks <pr>`. If checks are still running, say so — the desktop app can watch CI; do not poll in a loop. If a check fails, report the failing job and first error, and fix it on the same branch after the user agrees.
- The user merges the PR (merge commit). Do not merge unless asked.

## 14. Recommend the next issue

- Run the `next-issue` skill with `-AssumeClosed <n>` (this issue closes when its PR merges).
- End with a short, ready-to-act message:
  1. Merge PR #<pr> (after CI is green).
  2. Start a new session (`/clear` or a new chat).
  3. Run `/implement-issue <next>` — <title>. Step 0 of that run switches to `main` and pulls the merge.
- Mention up to two alternatives and anything that waits for this PR.

> Note: if the build tooling or `gh` is unavailable in the session, produce the plan, the change and the comment text so they can be applied manually, and say which steps were skipped.
