---
name: nuget-package-update
description: 'Pin-aware NuGet dependency updates for GitHubBackup through Central Package Management (src/Directory.Packages.props). Use when the user asks to update or bump NuGet packages, find outdated or vulnerable packages, or invokes this skill. Discovers outdated packages, classifies them against the pin contract and package families, and applies safe updates on the current branch with a build and test gate after each step. Never commits, pushes or creates a branch.'
---

# nuget-package-update

## Use this skill when

- The user asks to update, bump or refresh NuGet package versions, or to check for outdated or vulnerable packages.
- A shared-framework wave (`Microsoft.Extensions.*`, `Microsoft.EntityFrameworkCore.*` at `10.0.x`) needs its next patch.

## Do not use it when

- Adding a brand-new dependency — add a `<PackageVersion>` and a `<PackageReference>` directly (see `.claude/rules/msbuild.md`).
- The working tree is dirty — the preflight refuses; ask the user to commit or stash first.

## Scope

NuGet only, apply-only, current branch only: no branch is created, nothing is committed or pushed, no CI run is triggered. The working tree is left with the applied edits for the user (or `/implement-issue`) to review and commit.

## Preflight

From the repository root:

```
pwsh -NoProfile -File .claude/skills/_local.nuget-package-update/scripts/Prepare-PackageUpdate.ps1
```

It refuses to run on a dirty tree, then removes `bin`/`obj` under `src` only — it never touches ignored user files (`backup-config.json`, logs, the history database, local settings).

## Discovery

- `dotnet list src/GitHubBackup.slnx package --outdated --format json` — capture to a file and read the summary, not the whole output. Add `--include-prerelease` only for a package already on a prerelease.
- `dotnet list src/GitHubBackup.slnx package --vulnerable --include-transitive` — a vulnerable package is an update candidate regardless of family timing.
- The `nuget` MCP server (`NuGet.Mcp.Server`) can answer version, vulnerability and release questions about a single package; use it instead of browsing nuget.org.

## Pin contract

Source of truth: `src/Directory.Packages.props`. A `<PackageVersion>` that must not move on a routine update — license change, compatibility constraint, a version CI depends on — carries `Pinned="true"` plus an XML comment with the reason. Do not bump a pinned package, not even a patch, unless the user names it. Do not add a pin without a real constraint.

## Package families

Move each family together in one step; never split it across runs. Families are defined by what `src/Directory.Packages.props` actually contains — when it gains a package that belongs to a family below, it joins that family; keep this list in step with the file.

- **Shared framework `10.0.x`:** `Microsoft.Extensions.*` that version with the runtime (Hosting, DependencyInjection, Options, Configuration, Logging, Http), `Microsoft.EntityFrameworkCore.*`. Same latest patch for all. Not in this wave: `Microsoft.Extensions.TimeProvider.Testing` (own cadence).
- **Logging:** `Serilog.*`.
- **Telemetry:** `OpenTelemetry.*`.
- **Abstractions:** `System.IO.Abstractions` with `System.IO.Abstractions.TestingHelpers` (always the same version).
- **Test infrastructure:** `xunit.v3*`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Microsoft.Testing.*`, `NSubstitute*`, `Shouldly`, `Verify.*`, `WireMock.Net*`, `FlaUI.*`, `NetArchTest.Rules`, `Microsoft.Extensions.TimeProvider.Testing`.
- **Analyzers, last:** analyzer-only packages (`*.Analyzers`, `Meziantou.Analyzer`, `Roslynator.*` if present).
- **Individually:** everything else (`Octokit`, `System.CommandLine`, `Spectre.Console`, `CommunityToolkit.Mvvm`, the Credential Manager wrapper…).

## Major versions

A major version is not by itself a reason to skip:

1. Read the target version's license and skim its release notes for breaking changes; record both in the report.
2. Commercial or paid license on the target version → do not bump; add `Pinned="true"` with the license URL in the comment, continue with the rest.
3. Otherwise apply it in its step; the build and test gate catches breakage.

## Steps — fail fast, no rollback, no commit

Report every package first as `skipped-pinned`, `update-candidate` (with its step) or `pin-candidate`. Then apply in this order:

1. Verify the pin contract (every `Pinned="true"` has a reason).
2. Individual packages.
3. Logging, telemetry and abstractions families.
4. Test infrastructure family.
5. Shared-framework `10.0.x` wave.
6. SDK +1 band — only if a framework wave requires a newer SDK or TFM: update `global.json` and the TFMs, at most one stable band, no preview unless already on preview.
7. Analyzers.

Edit the `Version` attribute of the `<PackageVersion>` element directly; `dotnet package update` does not reliably edit the central file. If the repository uses lock files (`RestorePackagesWithLockFile`), run `dotnet restore` and include the regenerated `packages.lock.json` files; never hand-edit them.

After **each** step, as separate commands:

- `dotnet build src/GitHubBackup.slnx -t:Rebuild -clp:ErrorsOnly` — must be clean (warnings are errors);
- `dotnet test src/GitHubBackup.slnx --no-build` — every test passes.

A red step stops the run; earlier green steps stay in the working tree. Report what was applied, what failed and why, and what was skipped.
