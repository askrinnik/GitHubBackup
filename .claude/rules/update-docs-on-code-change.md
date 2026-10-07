---
paths:
  - "src/GitHubBackup.Cli/**"
  - "src/**/appsettings*.json"
  - "src/**/Options/**"
  - "CLAUDE.md"
  - ".claude/**"
  - ".ai/**"
  - ".mcp.json"
  - ".github/workflows/**"
---

# Keep documentation in step with code

The PRD comes **before** the code: a change of behaviour, command, option, setting, file format, exit code or status that the PRD does not already describe is proposed as a PRD change first and implemented after approval (see `CLAUDE.md`, *Source of truth*). Everything else in the table is updated **in the same change** as the code.

| Change | Update |
|---|---|
| Behaviour, CLI command or option, setting, file format, exit code, item status | `docs/PRD.md` first (version and history row), then `README.md` where it describes the same thing |
| Build, run, test or format commands; project layout | `CLAUDE.md` (*Build and test*, *Repository layout*) and `README.md` |
| CI workflow added or changed | `README.md` (how CI runs) if the developer-visible behaviour changes |
| AI harness: `CLAUDE.md`, `.claude/**`, `.ai/**`, `.mcp.json`, hooks | `docs/ai/README.md`; a change to a workflow command's body (`.ai/prompts/<command>.md`) or the agents it calls also updates `docs/ai/<command>.md` |
| Work that is deferred to later | a new issue with `## Зависимости` and a GitHub "blocked by" relation — not a TODO in a document or in code |

Rules:

- Documentation states what is true now and is self-contained. Do not document a feature that does not exist yet; remove or correct any statement the change made false.
- Requirements belong in `docs/PRD.md`, task breakdowns in `docs/plans/`; do not mix them.
- Code examples in documents match the current signatures and run as written.
- There is no `CHANGELOG.md`; history lives in the PRD change table, commits and pull requests.
