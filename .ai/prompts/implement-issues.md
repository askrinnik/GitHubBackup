Take a list of small GitHub issues of `askrinnik/GitHubBackup` one after another on **one branch**, each with the full `implement-issue` workflow and its own commit, then verify the whole branch once and ship it as **one pull request**. The batch runs without review stops: it halts only on a question it cannot settle itself, and it reports every debatable decision at the end.

The arguments are issue numbers in execution order (`/implement-issues 5 6 7`), plus optional flags:

- `--review-plans` — stop for plan review per issue, as `implement-issue` does.
- `--ship` — pre-authorise push, pull request, issue comments and acceptance ticks; without it each of them waits for the user's go-ahead.

No issue numbers → recommend a batch with the `next-issue` skill (ready issues in order) and ask which to take.

## Relation to `implement-issue`

For every issue run the workflow in [.ai/prompts/implement-issue.md](implement-issue.md) — lane, understanding, plan, implementation, build, verification — **with the differences below**. Everything not mentioned there applies unchanged: the context budget, the delegation to agents, the comment-hygiene review, the documentation rules, the PRD-first rule, the plan file `docs/plans/<type>-<n>-<slug>.md`. Do not copy the workflow here; when it changes, the batch inherits the change.

| `implement-issue` | In a batch |
|---|---|
| Step 0, per issue, including the start reading of the plan usage | Once, before the first issue |
| Branch created at the commit in step 12 | Created at the first issue's commit, then reused (see *Branch*) |
| Steps 5–7, plan review stop and compact milestone | Save the plan and continue; stop only with `--review-plans` |
| Step 9, `build-runner` with scope `full` and security review per issue | `build-runner` with scope `projects` (the touched test projects) per issue; scope `full` and the security review once at the end |
| Step 10, verification per issue | Per issue as written; FlaUI UI tests once at the end |
| Step 11, result confirmation per issue | One confirmation for the whole batch |
| Steps 12–13, commit, push, comment, PR per issue | A commit per issue; one comment per issue, one push and one PR at the end. The task tick in a multi-issue plan goes into each issue's commit |
| Step 12, session profile after the commit, amended into it | Once, after the batch confirmation and before the push; the record goes in as a separate commit (see *Ship*) |
| Step 14 | Once, at the end |

## 0. Preflight (once)

1. Working tree and branch as in `implement-issue` step 0: uncommitted changes stop the batch; clean tree → `git switch main` and `git pull --ff-only`.
2. Read every issue (`gh issue view`): body, comments, labels, state. A closed issue stops the batch. Pick each lane from its labels.
3. **Dependencies.** A blocker that is open and **earlier in the list** counts as met; any other open blocker stops the batch and is named. If the list order contradicts a dependency, say so and stop.
4. **Size.** Batches are for small issues: at most 5, each of complexity S or M. A plan that comes back `L` stops the batch at that issue: report it, leave the finished commits, and suggest running that issue alone with `/implement-issue`.
5. Assign the issues to the user (`gh issue edit <n> --add-assignee @me`).
6. Read the plan usage once, silently, as in `implement-issue` step 0: the start reading of the session profile.

## Branch

Do not create the branch during preflight or planning; as in `implement-issue`, a branch comes into being only at a commit. Create it from the fresh `main` at the **first issue's commit**, together with that commit: `<first>-<last>-<short-slug>` (for example `5-7-cli-options`); a single issue would use `<n>-<slug>`. The slug names what the batch has in common. Later commits reuse the branch. Never commit on `main`.

## Per issue (in list order)

1. Run `implement-issue` steps 1–10 with the differences above. Plan with `issue-planner`, implement with `issue-developer`, review the added comments, keep the plan file in the working tree.
2. **Questions.** A question the issue, the PRD, the code and sensible defaults do not settle stops the batch: ask the user, then continue from the same issue. A needed PRD change always stops the batch (PRD first: propose the text, the version bump and the history row, wait for approval). A choice that is yours to make but could be questioned (a UI presentation, a behaviour change beyond the acceptance list, a skipped refactor, a deviation from a convention) is **not** a stop: make it, record it in the plan's *Решения* section and in a running list for the final report.
3. Call `build-runner` with scope `projects` and the test projects the change touches (full rebuild, then those tests). Failures go back to implementation; do not commit red.
4. **Commit** — starting the batch authorises the commits, one per issue; the first one also creates the branch (see *Branch*). Make one `skill-runner` call with the `git-commit` skill (Case 1: `#<n> <exact issue title>`, a blank line, dash-prefixed actions): it creates or checks the branch, stages exactly the files you list, commits, checks the message and reports the short SHA. List only that issue's changes, including the plan file with its checklist ticked, the documentation the change updates and the issue's task line in a multi-issue plan: `Grep` `docs/plans/` for the issue's link (`issues/<n>)`) and change the hit `- [ ] **<id>** ([#<n>](…))` in `docs/plans/<topic>-plan.md` to `- [x]` before the commit; no hit → nothing to tick.
5. The next issue starts from the committed state. Between issues give the user the ready `/compact` command from `implement-issue` (focus: issue numbers and titles, acceptance lists, branch name, commits so far, the running decision list, changed files) once the conversation passes about 40 tool calls.

## Final verification (once, after the last commit)

Run on the branch as a whole, against `git diff main...HEAD`, in this order:

1. `build-runner` with scope `full`: full rebuild, the whole solution's tests, `dotnet format --verify-no-changes`; it returns the summary lines verbatim and the first failures.
2. **UI tests** — included in the solution test run; when `GitHubBackup.App` changed, make sure the `build-runner` summary shows the `GitHubBackup.App.UITests` run.
3. **Security review** — `security-reviewer` on the branch diff when any issue touched `Infrastructure`, `Cli`, the token path, process or archive handling, CI workflows or packages; fix CRITICAL and IMPORTANT findings.
4. Re-verify the acceptance items of every issue that the final run could affect.
5. Fix what fails or what the review finds as **additional commits**, each under the `#<n>` of the issue it belongs to (Case 1), then re-run the affected checks.

## Report and confirmation

Present, in one message:

- a table: issue → commit → acceptance items → how verified → result;
- the verification results (build, tests, format, security review);
- **the list of debatable decisions**, each with the alternative considered;
- anything not covered or left out.

Ask whether the result is acceptable. If not, iterate on the affected issue and re-verify.

## Ship

Without `--ship` each step below needs the user's go-ahead; with it, run them in order. The question whether to save the session profile (step 2) is asked even with `--ship`.

1. **Re-sync the base:** `git fetch origin`; if `origin/main` moved, `git pull --ff-only` onto the branch (rebase never) and re-run the final verification.
2. **Profile the session** once, as in `implement-issue` step 12 (*Profile the session*), with `-WorkItem <first issue>` — the same work item the branch name gives — and without `-NoCommit`. A batch never amends, so on the user's yes the record goes in as a separate commit: one `skill-runner` call with the `git-commit` skill (Case 1 under the last issue of the batch, `#<last> <exact title>` with the action `- Save the session profile record`), staging only the record.
3. **Push** the branch (`git push -u origin <branch>`).
4. **One pull request** into `main` — one `skill-runner` call with the `open-pr` skill: title `#<a> #<b> #<c> <shared summary>`, a description that starts with one `Closes #<n>` line per issue, then what changed, the debatable decisions and how it was verified. Do not repeat the per-issue acceptance tables.
5. **Issue comments:** one comment per issue for its lane, each linking the PR — one `skill-runner` call per issue with the `github-issue` skill, which posts it. Post them after the PR is open so the link is real. Tick the verified `- [ ]` boxes in each issue body yourself, with one `Set-AcceptanceChecks.ps1` call per issue (`github-issue` skill, *Acceptance boxes*).
6. **CI:** check once with `gh pr checks <pr>`; never poll. Merging stays with the user.
7. Run the `next-issue` skill with `-AssumeClosed` for every issue of the batch and end with the same short message as `implement-issue` step 14 (merge the PR, start a new session, the next `/implement-issue` or `/implement-issues`).

## Stops

The batch stops and reports, leaving all finished commits in place, on: an unsettled question, a PRD change that needs approval, an open foreign blocker, a closed issue, a plan of complexity `L`, a reproduction that fails (Bug lane), a build or test failure that two fix attempts do not resolve, or a change that would need to touch an earlier issue's commit in a way that is not a plain follow-up. Never amend or rewrite a commit already made in the batch; fix forward. The one exception is the message check `skill-runner` runs on the commit it has just made (`git-commit` skill, *Procedure*).
