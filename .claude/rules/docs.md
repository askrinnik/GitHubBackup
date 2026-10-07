---
paths:
  - "docs/**/*.md"
  - "README.md"
---

# Documentation

- Documentation under `docs/` and `README.md` is written in **Russian** (NFR-7). Identifiers, commands, paths, file names and code stay as they are in the code (English).
- `docs/PRD.md` is the source of truth. Every edit to it:
  - bumps `Версия документа` in the header table and sets `Дата`;
  - adds a row to *История изменений документа* that says what changed;
  - keeps requirement ids stable — a removed requirement keeps its id retired, a new one gets the next free id in its section.
- Requirements are referenced by id (`FR-6.2`, `NFR-1`, `П-8`) and sections by number (`§8.1`); links to sections use GitHub anchors.
- `docs/plans/<type>-<issue>-<slug>.md` (`feature-`, `bug-`, `chore-`, `test-`) holds the implementation plan of one issue: goal and acceptance list, affected projects and types, approach, tests, out of scope, open questions, and a task checklist that is ticked as work completes and committed with the work. A plan is not rewritten after the issue is closed.
- `docs/plans/<topic>-plan.md` holds a multi-issue plan (task list and design of a larger effort); it marks each of its tasks done when the task's issue closes.
- `docs/ai/README.md` describes the AI harness for people who maintain or reuse it; `docs/ai/<command>.md` holds the step tree and flowchart of one workflow command.
- Style: short sentences, active voice, one idea per paragraph; tables for structured comparisons; no emoji, except the markers 💻 (main session), 🤖 (agent call), 🧩 (skill) and 🙋 (wait for the user) in the workflow documents under `docs/ai/`, each explained in the document's legend. Do not repeat what another document already says — link to it.
