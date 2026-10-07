# CLAUDE.md — GitHubBackup

Windows 11 application (.NET 10, C# 14) that keeps local clones of GitHub repositories and ZIP-archives them when they change. Two hosts — a console app (`GitHubBackup.Cli`) and a WPF app (`GitHubBackup.App`) — share one core through DI.

## Source of truth

- **`docs/PRD.md` is the single source of requirements.** Every issue links the PRD sections and requirement ids (`FR-x.y`, `NFR-x`) it implements. Read those sections, not the whole document.
- **PRD first.** If a task needs behaviour the PRD does not describe, or contradicts it, stop and propose the PRD change (bump the version in the header, add a row to the change history). Code follows only after the user approves the change; the issue is updated after the PRD.
- The AI harness itself (agents, rules, skills, workflow, benchmark) is described in `docs/ai/README.md`; each workflow command has its own document next to it in `docs/ai/`.

## Language (NFR-7)

- **English:** code, comments, logs, CLI and UI messages, issue titles, commit messages, PR titles and descriptions, issue and PR comments, and every file of the AI harness (`CLAUDE.md`, `.claude/**`, `.ai/**`).
- **Russian:** documentation under `docs/` (PRD, plans, `ai/`), `README.md`, issue bodies.

## Repository layout

```
docs/
  PRD.md                 requirements (source of truth)
  plans/                 one implementation plan per issue: <type>-<issue>-<slug>.md
  ai/                    AI harness hub (README.md) and one document per workflow command (step tree and flowchart)
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
- `GitHubBackup.Core` and `GitHubBackup.Core.Tests` target `net10.0`; every other project targets `net10.0-windows`. Shared settings live in `src/Directory.Build.props`, package versions only in `src/Directory.Packages.props`.
- `global.json` (repository root) pins the SDK band (`10.0.100`, `rollForward: latestFeature`) and selects Microsoft.Testing.Platform as the `dotnet test` runner.

## Build and test

Run from the repository root, each as a separate command:

| Purpose | Command |
|---|---|
| Build (full rebuild, errors only) | `dotnet build src/GitHubBackup.slnx -t:Rebuild -clp:ErrorsOnly` |
| Test the solution | `dotnet test --solution src/GitHubBackup.slnx --no-build` |
| Test one project | `dotnet test --project src/<Project>/<Project>.csproj --no-build` |
| Test one class | `dotnet test --project src/<Project>/<Project>.csproj --no-build --filter-class "*<Class>"` |
| Test without UI and smoke (as CI does) | `dotnet test --solution src/GitHubBackup.slnx --no-build --filter-not-trait "Category=UI" --filter-not-trait "Category=Smoke" --ignore-exit-code 8` |
| Format check | `dotnet format src/GitHubBackup.slnx --verify-no-changes` |

Tests run on Microsoft.Testing.Platform (`xunit.v3.mtp-v2`): the solution or project goes after `--solution`/`--project`, never as a positional argument, and filters are the xUnit MTP options (`--filter-class`, `--filter-method`, `--filter-trait`).

Terminal hygiene: run build and test as separate commands, never chained with a short timeout; keep output small (`-clp:ErrorsOnly`, or capture to a file and read only the summary).

## Git and GitHub

- All GitHub work goes through the `gh` CLI (repository `askrinnik/GitHubBackup`). Use `--json` with `--jq` to fetch only the fields you need.
- `main` is the base branch. Never commit directly to it. Work for issue `<n>` happens on branch `<n>-<slug>`, created immediately before the first commit.
- Commit messages and PR text follow the `git-commit`, `pull-request` and `post-issue-comment` skills. A PR targets `main`, its description contains `Closes #<n>`, and the user merges it with a merge commit.
- Never commit, push, open or merge a PR, comment on, create or edit an issue without the user's explicit go-ahead for that action. The one standing exception: starting `/implement-issues` authorises its commits (one per issue); push, PR and comments still wait for a go-ahead unless the command carries `--ship`.
- Never create, switch, rename or delete a branch, and never stash, on your own initiative — not to prepare work, not to tidy up after a mistake. Do it only when the user's current message asks for it. The one built-in exception is a task branch created together with a commit the user has authorised; `.ai/prompts/implement-issue.md` (step 0) and `.ai/prompts/implement-issues.md` say when. Work stays on the branch you were started on; if that is a problem, say so and ask.
- Issue order is expressed twice: GitHub "blocked by" relations (machine-readable) and the `## Зависимости` section of the issue body (human-readable). When creating an issue, set both.
- Entry points: `/implement-issue <n>` takes one issue end to end; `/implement-issues <n> <n> …` takes several small issues in order on one branch, one commit each, and ships one PR; `/next-issue` recommends what to take next.

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
- **Run simple commands.** The working directory is already the repository root: do not prefix commands with `cd`, and avoid `&&` chains, pipes, loops and heredocs where one plain command does the job. Plain commands match the permission rules in `.claude/settings.json`; compound ones cannot be allowed permanently and interrupt the user with a one-time prompt. When text must reach a command (commit message, comment, PR body), write it to a file in the scratchpad and pass the file (`-F`, `--body-file`).
- Delegate read-heavy or noisy work: broad research → `Explore`; planning an issue → `issue-planner`; implementing an approved plan → `issue-developer`; the build-and-test gate inside `/implement-issue(s)` → `build-runner`; an approved commit, issue comment or PR inside `/implement-issue(s)` → `skill-runner`.

## Custom agents (`.claude/agents/`)

- **issue-planner** — read-only research; returns a review-ready plan for one issue.
- **issue-developer** — implements an approved plan with tests; never commits, pushes or posts.
- **build-runner** — runs the build, tests and format check on a cheap model and returns the tools' summary lines verbatim; never edits or fixes anything.
- **skill-runner** — carries out one approved commit, issue comment or pull request end to end by following the matching skill (`git-commit`, `post-issue-comment`, `pull-request`) on a cheap model; never pushes and never acts beyond that one action. The caller gets the user's go-ahead first.
- **architect** — designs changes that keep the layer boundaries; surfaces trade-offs.
- **security-reviewer** — reviews changes for the risks of this application (token leakage, argument injection into git, path escape, unsafe archives).

Code review uses the built-in `/code-review` and `/security-review`, which read these rules.
