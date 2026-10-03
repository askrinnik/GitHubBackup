---
paths:
  - "**/*.csproj"
  - "**/*.slnx"
  - "**/Directory.Build.props"
  - "**/Directory.Build.targets"
  - "**/Directory.Packages.props"
  - "**/global.json"
  - "**/.editorconfig"
---

# Projects, solution and packages

- The solution is `src/GitHubBackup.slnx`. Every project, including test projects, is in it; projects are added with `dotnet sln src/GitHubBackup.slnx add`, not by hand-editing.
- Shared settings live in `src/Directory.Build.props`: `TargetFramework` (`net10.0` for `Core` and `Core.Tests` only; `net10.0-windows` for every other project, because a `net10.0` project cannot reference `Infrastructure`), `Nullable` enabled, `TreatWarningsAsErrors`, `AnalysisLevel`/analyzers, `ImplicitUsings`, `LangVersion`. A `.csproj` repeats none of them; it overrides one only with a comment explaining why.
- **Central Package Management.** Every version is a `<PackageVersion>` in `src/Directory.Packages.props`; a `<PackageReference>` in a `.csproj` has no `Version`. A package that must not move on routine updates carries `Pinned="true"` and a comment with the reason. Version changes go through the `nuget-package-update` skill.
- Test-only packages (xUnit v3, NSubstitute, Shouldly, Verify, WireMock.Net, FlaUI, NetArchTest, `Microsoft.Extensions.TimeProvider.Testing`, `System.IO.Abstractions.TestingHelpers`) are referenced only from test projects; the test-wide set (xUnit v3, Shouldly, NSubstitute and their global usings) is added in `src/Directory.Build.props` under a condition on a project name ending in `Tests`, because `IsTestProject` is not yet set when that file is evaluated.
- Project references follow the layer rule: `Cli`, `App` → `Infrastructure` → `Core`; each test project references its paired project only (plus test helpers). A reference that breaks this is a design change, not a fix.
- `InternalsVisibleTo` for the paired test project is declared in the project file (`<InternalsVisibleTo Include="…Tests" />`), not with an assembly attribute in code.
- Publishing settings (single-file, framework-dependent, one output folder for both executables — NFR-8) belong to the host projects and are added by the publishing issue, not earlier.
- A warning is an error. Do not suppress one with `NoWarn` or `#pragma` without a comment that states why the rule does not apply; never suppress project-wide what applies to one line.
