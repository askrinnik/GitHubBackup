# CLAUDE.md — GitHubBackup

Windows 11 application (.NET 10, C# 14) that keeps local clones of GitHub repositories and ZIP-archives them when they change. Two hosts — a console app (`GitHubBackup.Cli`) and a WPF app (`GitHubBackup.App`) — share one core through DI.

## Source of truth

- **`docs/PRD.md` is the single source of requirements.** Every issue links the PRD sections and requirement ids (`FR-x.y`, `NFR-x`) it implements. Read those sections, not the whole document.
- **PRD first.** If a task needs behaviour the PRD does not describe, or contradicts it, stop and propose the PRD change (bump the version in the header, add a row to the change history). Code follows only after the user approves the change; the issue is updated after the PRD.
- The AI harness itself (agents, rules, skills, workflow, benchmark) is described in `docs/ai-harness.md`.

## Language (NFR-7)

- **English:** code, comments, logs, CLI and UI messages, issue titles, commit messages, PR titles and descriptions, issue and PR comments, and every file of the AI harness (`CLAUDE.md`, `.claude/**`, `.ai/**`).
- **Russian:** documentation under `docs/` (PRD, plans, `ai-harness.md`), `README.md`, issue bodies.

## Repository layout

```
docs/
  PRD.md                 requirements (source of truth)
  plans/                 one implementation plan per issue: <type>-<issue>-<slug>.md
  ai-harness.md          how the AI harness works
src/
  GitHubBackup.slnx
  Directory.Build.props, Directory.Packages.props, .editorconfig
  GitHubBackup.Core (+ .Tests)                 domain logic and abstractions, no I/O libraries
  GitHubBackup.Infrastructure (+ .Tests, .IntegrationTests)   git, 7-Zip, Octokit, EF Core SQLite, Credential Manager, files, processes
  GitHubBackup.Cli (+ .Tests)                  console host
  GitHubBackup.App (+ .Tests, .UITests)        WPF host (MVVM)
  GitHubBackup.ArchitectureTests               layer dependency rules
.ai/
  prompts/               workflow bodies shared by commands
  benchmarks/harness/    harness benchmark: fixtures, history, reports
```

- Dependencies point inward only: `Cli`, `App` → `Infrastructure` → `Core`. `Core` references none of the others and no I/O library; `GitHubBackup.ArchitectureTests` enforces it.
- Every production project has its test project next to it (`<Project>.Tests`).
- The solution does not exist yet; it is created by #2 (F0.1), which also fills in the build section below.

## Build and test

Commands are added by #2 (F0.1). Until then there is nothing to build.

Terminal hygiene: run build and test as separate commands, never chained with a short timeout; keep output small (`-clp:ErrorsOnly`, or capture to a file and read only the summary).

## Git and GitHub

- All GitHub work goes through the `gh` CLI (repository `askrinnik/GitHubBackup`). Use `--json` with `--jq` to fetch only the fields you need.
- `main` is the base branch. Never commit directly to it. Work for issue `<n>` happens on branch `<n>-<slug>`, created immediately before the first commit.
- Commit messages and PR text follow the `git-commit`, `pull-request` and `post-issue-comment` skills. A PR targets `main`, its description contains `Closes #<n>`, and the user merges it with a merge commit.
- Never commit, push, open or merge a PR, comment on, create or edit an issue without the user's explicit go-ahead for that action.
- Issue order is expressed twice: GitHub "blocked by" relations (machine-readable) and the `## Зависимости` section of the issue body (human-readable). When creating an issue, set both.
- Entry points: `/implement-issue <n>` takes one issue end to end; `/next-issue` recommends what to take next.

## Code conventions (summary)

Full rules load automatically by path from `.claude/rules/`. The essentials:

- DI everywhere; dependencies are interfaces injected through the constructor; options via `IOptions<T>` with `ValidateOnStart`.
- Time comes from an injected `TimeProvider` and the file system from `System.IO.Abstractions` (`IFileSystem`) — never `DateTime.Now` or static `File`/`Directory` calls in production code.
- External tools (git, git-lfs, 7-Zip) run only through `IProcessRunner`. The GitHub token never reaches process arguments, remote URLs, `.git/config`, archives or logs (NFR-1, FR-11.3).
- `TreatWarningsAsErrors`, nullable reference types enabled, .NET analyzers on. A warning is a build failure, not a follow-up.
- New behaviour ships with tests in the paired test project; integration tests use local bare repositories in a temp folder and no network.

## Comment hygiene

A code comment states what the code does and why, in the present tense, and makes sense without the issue tracker or the file's history. Never write:

1. change narration (*no longer*, *used to*, *previously*, *now uses*, *replaced X*);
2. issue references (`#12`, *see F1.3*) — the rationale itself belongs in the comment;
3. line-number citations (`File.cs:42`) — name the symbol instead;
4. repetition of what the code, the interface doc or a `<see cref>` already says.

Every C# type and member gets a `///` XML doc comment; implementations and overrides use `/// <inheritdoc />`. Issue references are fine in commits, PRs, issue comments and Markdown. Full rule: `.claude/rules/csharp.md`.

## Working style and context economy

- Make focused, reviewable changes; reuse existing abstractions before adding new ones.
- Read narrowly: grep first, then read around the match. Do not re-read a file you just edited.
- Keep tool output small at the source; a successful write (commit, `gh` edit) is its own confirmation.
- Delegate read-heavy or noisy work: broad research → `Explore`; planning an issue → `issue-planner`; implementing an approved plan → `issue-developer`; commit/PR/comment text → `skill-runner`.

## Custom agents (`.claude/agents/`)

- **issue-planner** — read-only research; returns a review-ready plan for one issue.
- **issue-developer** — implements an approved plan with tests; never commits, pushes or posts.
- **skill-runner** — composes commit messages, PR descriptions and issue comments by following a named skill; returns text only.
- **architect** — designs changes that keep the layer boundaries; surfaces trade-offs.
- **security-reviewer** — reviews changes for the risks of this application (token leakage, argument injection into git, path escape, unsafe archives).

Code review uses the built-in `/code-review` and `/security-review`, which read these rules.
