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
6. **Delegate noisy work** to the agents named in this workflow (`issue-planner`, `issue-developer`, `skill-runner`, `Explore`) so their raw output stays out of this conversation.

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

Run these yourself, as separate commands, even if `issue-developer` reported success:

- `dotnet build src/GitHubBackup.slnx -t:Rebuild -clp:ErrorsOnly` — a full rebuild so every diagnostic is current. Warnings are errors; the build must be clean.
- `dotnet test src/GitHubBackup.slnx --no-build` — every test passes, including the architecture tests; nothing regresses. Capture the output to a file and read the summary.
- `dotnet format src/GitHubBackup.slnx --verify-no-changes` — no formatting drift.
- **Review the comments this change adds**, including those the developer agent wrote: `git diff -U0 -- '*.cs' '*.xaml'` lines starting with `+` and containing `//`, `///` or `<!--`. Check each against the comment-hygiene rules (no change narration, no issue references, no line numbers, no repetition) and fix what fails.
- For a change in `Infrastructure`, `Cli` or the token path, run the `security-reviewer` agent on the working tree and fix CRITICAL and IMPORTANT findings.
- Until #2 (F0.1) creates the solution, steps that need it do not apply; say so in the summary.
- **Milestone:** hand the user the step-9 `/compact` command.

## 10. Verify

- **Bug lane:** the reproducing test passes; re-run the original reproduction (CLI run or scenario) and confirm the failure is gone.
- **Feature lane:** verify **every** acceptance item and record how: a named test, a command with its observed output, or a CLI run against a temp folder with local repositories (exit code, summary, files on disk, log entries). Exercise the obvious negatives the PRD defines: invalid configuration, missing tool, failing repository, cancellation, `--dry-run` changing nothing.
- UI issues: ViewModel tests and FlaUI tests per the plan; manual inspection of the running app only when the user asks for it.
- If anything fails, go back to step 8 (or step 4 if the approach must change), then re-run steps 9–10.

## 11. Ask for confirmation

- **Bug lane:** what was wrong (root cause), what was fixed, how it was verified.
- **Feature lane:** what was implemented, and the acceptance list as a table: item → how verified → result. State explicitly anything not covered or left out.
- Ask whether the result is acceptable. If not, return to step 8 (or 4) and iterate.
- **Milestone:** once confirmed, hand the user the step-11 `/compact` command.

## 12. Commit and push

**Delegate the text, keep the action.** The commit message, the issue comment and the PR text are composed by the `skill-runner` agent (cheap model, isolated context). Give it the skill name (`git-commit`, `post-issue-comment`, `pull-request`) and the compact facts you already hold — issue number and exact title, lane, `git diff --stat` with one line per changed file, the acceptance table, build and test results, and for the PR the posted comment. You run every `git`/`gh` action yourself, after the user's go-ahead.

- **Re-sync the base.** `git fetch origin`; if `origin/main` moved since step 0, pull it (`git pull --ff-only`; the uncommitted changes travel with you) and re-run step 9 so nothing regressed against the newer base. Otherwise say it is unchanged.
- **Create the branch** `<n>-<slug>` from the up-to-date `main`: `git switch -c <n>-<slug>`. If you stayed on an existing branch at the user's request (step 0), commit there.
- **Tick the plan** checklist in `docs/plans/…` to match the work; the plan file goes into the same commit.
- **Commit** with the message from `skill-runner` (`git-commit` skill) after the user's go-ahead.
- **Push** (`git push -u origin <branch>`) after the user's go-ahead.

## 13. Issue comment, acceptance boxes, pull request

- **Issue comment.** Compose with `skill-runner` (`post-issue-comment` skill: Bug → *Root Cause Analysis / Resolution / Verification*; Feature → *Implementation / Acceptance Criteria / Verification*), then post it after the user's go-ahead: `gh issue comment <n> --body-file <file>`.
- **Acceptance boxes.** After the user's go-ahead, tick in the issue body the `- [ ]` items of `## Критерии приёмки` that were verified (`gh issue view <n> --json body` → replace `- [ ]` with `- [x]` for those items only → `gh issue edit <n> --body-file <file>`). Leave unverified items unticked and name them.
- **Pull request.** Compose with `skill-runner` (`pull-request` skill) from the posted comment, then after the user's go-ahead: `gh pr create --base main --head <branch> --title "#<n> <exact title>" --body-file <file>`. The body contains `Closes #<n>`. Report the PR URL.
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
