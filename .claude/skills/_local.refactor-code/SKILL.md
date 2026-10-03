---
name: refactor-code
description: Refactor GitHubBackup source surgically without changing behaviour — improve structure, naming or duplication within the existing architecture, verified by the unchanged test suite. Use when asked to refactor, clean up or restructure code.
---

# Refactor Code

Improve maintainability while preserving behaviour and the repository's conventions.

Ask for the target area and the reason for the refactoring if they are not already clear.

## Requirements

- Behaviour stays identical: the existing tests pass unchanged. If a test must change, the change is structural (a moved type, a renamed member), never a weakened assertion — and say so.
- Stay within the architecture: the layer rule, the PRD component model (§8.2), DI through interfaces. A refactoring that moves a responsibility across layers is a design change — consult the `architect` agent first.
- Small, reviewable steps; build and run the affected tests after each one.
- Do not mix feature work or bug fixes into a refactoring.
- Comments and XML docs touched by the refactoring obey the comment-hygiene rules; leftover comments that narrate history are rewritten, not carried along.
- Finish with `dotnet format --verify-no-changes` and a full solution test run.
