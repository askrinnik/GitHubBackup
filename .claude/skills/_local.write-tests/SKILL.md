---
name: write-tests
description: Add focused tests for a behaviour or a bug fix in the correct GitHubBackup test layer (unit, integration with local git repositories, contract with WireMock, snapshot with Verify, CLI end-to-end, ViewModel, FlaUI, architecture). Use when asked to write, add or extend tests, or to cover a fix with a regression test.
---

# Write Tests

Add or update tests in the correct layer for the requested behaviour or bug fix.

Ask for the target behaviour or the failing scenario if it is not already clear.

## Procedure

1. **Find the requirement.** Locate the PRD requirement the behaviour implements (`FR-x.y`); its wording defines the cases, including boundaries (empty repository, missing branch, deleted tag, exhausted rate limit…).
2. **Pick the layer** using the table in `.claude/rules/tests.md`. The lowest layer that can observe the behaviour wins: a decision in `Core` gets a unit test, not an integration test.
3. **Find the home.** The nearest existing test class for the type; otherwise a new `<Type>Tests` in the mirrored folder of the paired test project. Match the surrounding naming, fixtures and helpers.
4. **Write the cases:** the success path, each failure path the code handles, and the PRD boundary cases. One behaviour per test; a `[Theory]` instead of copies.
5. **Keep them deterministic:** `FakeTimeProvider`, `MockFileSystem`, temp folders and local bare repositories, no network, no machine culture.
6. **Run** the affected test project after a separate build (`dotnet build …` then `dotnet test … --no-build`, filtered to the class while iterating), then the whole solution's tests once at the end. Every existing test still passes.

## For a bug fix

Write the regression test **first**, run it, and confirm it fails for the reason in the bug report. Only then fix the code and watch it pass. A test that never failed proves nothing.

## Checklist

- Assertions with Shouldly; doubles with NSubstitute; snapshots with Verify (commit `.verified.*` only).
- No `// Arrange / Act / Assert` comments.
- Test names `Method_Scenario_ExpectedResult`.
- No test depends on another test, on order, on the clock or on the network.
