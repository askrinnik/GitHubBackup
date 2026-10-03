---
paths:
  - "src/**/*.Tests/**"
  - "src/**/*.IntegrationTests/**"
  - "src/**/*.UITests/**"
  - "src/GitHubBackup.ArchitectureTests/**"
---

# Tests

## Where a test goes

| Project | What it tests | Rules |
|---|---|---|
| `<Project>.Tests` | Unit tests of the paired project | No real file system, processes, network or clock |
| `GitHubBackup.Infrastructure.IntegrationTests` | Real git and 7-Zip against local repositories | Temp folder per test, `file://` remotes, no network |
| `GitHubBackup.Cli.Tests` (end-to-end category) | The CLI executable against local repositories | Asserts exit codes, summary and files on disk |
| `GitHubBackup.App.Tests` | ViewModels | No WPF window, no dispatcher dependency |
| `GitHubBackup.App.UITests` | The running WPF app through FlaUI | Separate workflow, run manually |
| `GitHubBackup.ArchitectureTests` | Layer dependencies (NetArchTest) | One rule per test |

Smoke tests against real GitHub carry a separate trait/category and do not run by default (PRD §11). Choose the nearest existing test class before creating a new one; a new production type gets a test class named `<Type>Tests` in the mirrored folder of the paired project.

## Stack

- **xUnit v3.** `[Fact]`, `[Theory]` with `[InlineData]`/`[MemberData]`/`TheoryData<…>`. Pass `TestContext.Current.CancellationToken` to async calls under test. Shared expensive setup through `IClassFixture<T>` / collection fixtures; per-test setup in the constructor, cleanup in `IAsyncDisposable`/`IDisposable`.
- **Shouldly** for assertions (`result.ShouldBe(…)`, `Should.ThrowAsync<T>(…)`). No `Assert.*` mixed in, no FluentAssertions.
- **NSubstitute** for test doubles of interfaces. Substitute only what the subject depends on; prefer a small hand-written fake when it makes the test clearer than a chain of `Returns`.
- **Verify** for snapshots of large outputs (summary table, dry-run output, saved `backup-config.json`, `.backup-state.json`). Commit the `.verified.*` files; never commit `.received.*`.
- **WireMock.Net** for GitHub API contract tests: pagination, 401/403/404, exhausted rate limit, a rename (same id, different name).
- **FlaUI** for UI tests; **NetArchTest** for architecture tests.

## Determinism

- Time: `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) pinned to a fixed instant, with `LocalTimeZone` set explicitly when local time matters. Never `DateTime.Now` in a test or its fixtures.
- File system in unit tests: `MockFileSystem` (`System.IO.Abstractions.TestingHelpers`) with absolute Windows paths (`C:\backup\…`).
- Integration tests: a unique temp folder per test, deleted in `Dispose` (clear read-only attributes first — git object files are read-only). Build the scenario with real git commands on a local bare repository: commits, branches, tags, force-push, deleted branch, submodule, empty repository.
- No test depends on another test's state, on the order tests run in, on the machine culture (set `CultureInfo` explicitly when formatting matters) or on the network.

## Style

- Test names: `Method_Scenario_ExpectedResult` (`Resolve_AccountPathSet_ReplacesOwnerFolder`).
- No `// Arrange`, `// Act`, `// Assert` comments; separate the three parts with blank lines.
- One behaviour per test; a `[Theory]` instead of copy-pasted facts.
- Build request objects and options from their real types, not anonymous objects.
- Cover the success path, each failure path the code handles, and the boundary cases named in the PRD requirement being implemented.
- Comment hygiene and XML-doc rules from `csharp.md` apply to test code too, except that test methods need no XML doc comment — the name is the documentation.

## Commands

Run tests with `dotnet test` on the solution or a single test project after a separate `dotnet build`, with `--no-build`. Filter with `--filter` (MTP) when iterating on one class. Capture long output to a file and read only the summary and the first failures.
