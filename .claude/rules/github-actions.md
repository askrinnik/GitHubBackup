---
paths:
  - ".github/workflows/**"
  - ".github/actions/**"
---

<!-- Condensed from github/awesome-copilot github-actions-ci-cd-best-practices (MIT), adapted to this repository. -->

# GitHub Actions workflows

## Workflows of this repository (PRD §11)

- **CI** — on every push and pull request: restore, build, unit, contract, architecture and integration tests on `windows-latest` (git and 7-Zip are present on the runner).
- **UI tests and smoke tests** — a separate workflow, `workflow_dispatch` only. Smoke tests use a repository secret and never run on pull requests from forks.

## Rules

- **Pin actions** to a full commit SHA with the version in a trailing comment (`uses: actions/checkout@<sha> # v5`); first-party `actions/*` included. Dependabot or a manual update moves the pins.
- **Least privilege:** set `permissions:` at the workflow level to `contents: read` and widen per job only where needed.
- **Secrets** only through `secrets.*`, passed as environment variables to the step that needs them; never echoed, never in `run:` command text, never available to `pull_request` runs from forks.
- **Untrusted input** (`github.event.pull_request.title`, branch names, issue bodies) is never interpolated with `${{ }}` directly into a `run:` script; pass it through `env:` and quote it.
- **.NET setup:** `actions/setup-dotnet` with `global-json-file` so CI uses the same SDK as developers; cache NuGet with the lock files or `Directory.Packages.props` as the key.
- **Build once, test with `--no-build`;** run the solution's tests with the same commands developers use, so CI and local results agree. Upload test results (TRX or JUnit) as an artifact and publish a summary.
- **Concurrency:** `concurrency: group: ${{ github.workflow }}-${{ github.ref }}` with `cancel-in-progress: true` for pull-request workflows.
- **Timeouts:** every job has `timeout-minutes`.
- **Shell:** on Windows runners set `defaults: run: shell: pwsh` explicitly.
- Keep workflows small and readable; extract repeated steps into a composite action under `.github/actions/` only when two workflows share them.
