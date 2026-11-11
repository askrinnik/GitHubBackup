# Ground truth — conventions of a new service

The request is specified by the PRD (FR-7.4; §9 step 4), so the expected answer designs the service rather than
proposing a PRD change. What is judged is whether the design follows the repository conventions (`CLAUDE.md`,
`.claude/rules/csharp.md`, `.claude/rules/tests.md`). The service may already exist in the solution by the time this
fixture runs; an answer that finds it and describes it in these terms is scored by the same items, and the fixture
then needs a new target.

| # | Item |
|---|---|
| 1 | **Layers:** the abstraction (an interface such as `ITemporaryFolderCleaner`) lives in `GitHubBackup.Core`; the implementation lives in `Core` (it may reach the disk through `IFileSystem`) or in `Infrastructure`; the host (`Cli`, `App`) holds no clean-up logic. The folder comes from `IRepositoryPathResolver.GetTemporaryArchiveFolder` or the same rule, not from a hard-coded path |
| 2 | **Dependency injection:** the service is registered in the layer's extension method (`AddGitHubBackupCore` or `AddGitHubBackupInfrastructure`), as a singleton, and its dependencies are interfaces injected through the constructor; not registered in a host's `Program`, no `new` of the service at the call site, no static helper |
| 3 | **Time and files:** the disk is reached only through `IFileSystem` (`System.IO.Abstractions`); if the design needs the current time it comes from an injected `TimeProvider`; no static `File`, `Directory`, `FileInfo`, `DateTime.Now`/`UtcNow` in the code shown. A file that cannot be deleted is logged and skipped, not thrown, so the run continues (FR-7.4) |
| 4 | **XML documentation:** the code shown gives the interface and its members `///` summaries (and `<param>`/`<returns>` where they add something), and the implementation uses `/// <inheritdoc />` or its own summary; comments contain no issue references or change narration |
| 5 | **Tests:** a test class in the paired test project (`GitHubBackup.Core.Tests` for a `Core` implementation, `GitHubBackup.Infrastructure.Tests` for an `Infrastructure` one), using `MockFileSystem` (or `TemporaryContentRoot` for real disk), covering at least the clean-up of leftover files and a file that cannot be deleted; test names follow `Method_Scenario_ExpectedResult` |

## Scoring

Max score: 5

- **Score** = items satisfied. A partly satisfied item counts 0.5 (for example item 2 registering the service in the
  right method but not naming it, item 4 with summaries on the interface only, or item 5 placing the test in the
  wrong project).
- **False positives** = statements about the repository that are false: naming existing types, methods or files that
  do not exist as if they did (a type the answer proposes to add is not a false positive), or claiming that the PRD
  does not describe the clean-up.
