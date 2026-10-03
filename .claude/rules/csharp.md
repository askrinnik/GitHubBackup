---
paths:
  - "**/*.cs"
---

# C# conventions

## GitHubBackup patterns

This section describes how **this** codebase is built. Where it conflicts with general .NET advice, this section wins.

### Layers and dependencies

- `GitHubBackup.Core` holds the domain model, the decision logic (change detection, discovery classification, path resolution, exclusion masks, archive naming, exit codes) and the abstractions it needs (`IGitClient`, `IArchiver`, `IGitHubApi`, `IProcessRunner`, stores). It references no other project of the solution and no library that does I/O.
- `GitHubBackup.Infrastructure` implements those abstractions: git and 7-Zip processes, Octokit, EF Core SQLite, Windows Credential Manager, the file system. `Cli` and `App` are hosts: composition root, user interaction, nothing else.
- Put a type in the lowest layer that can own it. Logic that both hosts need lives in `Core` or `Infrastructure`, never duplicated in a host (FR-12.7).

### Dependency injection and options

- Constructor injection only; no service locator, no `static` mutable state. Primary constructors are fine for services.
- Every dependency is an interface registered in the composition root. Register each layer's services in one extension method per layer (`services.AddInfrastructure(configuration)`), not in the host's `Program`.
- Settings bind to option classes through `IOptions<T>` with data annotations or an `IValidateOptions<T>`, and `ValidateOnStart()`. A missing or invalid setting fails at startup, not on first use.
- Lifetimes: stateless services `Singleton`; a `DbContext` through `IDbContextFactory<T>`; nothing `Scoped` unless a scope is created explicitly.

### Time, files and processes

- The current instant comes from an injected `TimeProvider` — never `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now` or `DateTime.Today` in production code. Local wall-clock time (archive names, `createdAt` with offset) uses `GetLocalNow()`; durations use `GetTimestamp()`/`GetElapsedTime()`.
- The file system goes through `System.IO.Abstractions` (`IFileSystem`) — never static `File`, `Directory`, `Path.GetTempPath()` file creation or `FileInfo` construction in production code. `Path` string helpers (`Combine`, `GetFileName`) are fine.
- External tools run only through `IProcessRunner` (see `external-processes.md`).
- Files the application owns (`backup-config.json`, `.backup-state.json`, archives) are written atomically: write to a temporary file in the target folder, flush, then replace or move over the final name. A failure leaves the previous file intact and no partial file under the final name (NFR-2).

### Async and cancellation

- Every I/O-bound operation is `async` and accepts a `CancellationToken`, passed down to every awaited call. The token is the last parameter, named `cancellationToken`.
- Never block on a task (`.Result`, `.Wait()`, `GetAwaiter().GetResult()`), never `async void` except UI event handlers.
- Library code (`Core`, `Infrastructure`) does not use `ConfigureAwait(false)` selectively; either a project uses it everywhere or nowhere — follow what the project already does.
- Cancellation is an expected outcome: let `OperationCanceledException` propagate to the orchestrator, which records the run as cancelled and cleans temporary files (NFR-4).

### Errors

- A failure of one repository is isolated: the orchestrator catches per-repository exceptions, records `Failed` with the message and continues (NFR-3). Do not catch-and-swallow below that level.
- Throw specific exception types with a message that names the repository, path or command involved; never throw `Exception` itself.
- Exit codes and item statuses are domain values (FR-9.8, FR-9.10), defined once in `Core`.

### Logging

- Log through `ILogger<T>` with message templates and named properties (`"Cloning {Repository} into {Path}"`), never string interpolation.
- Use `LoggerMessage` source generation (`[LoggerMessage]`) for messages logged per repository or per output line.
- Scope context (`RunId`, `Repository`, `Operation`) with `BeginScope` at the orchestrator level instead of repeating it in every message (FR-10.3).
- Never log the token, an `Authorization` header or a URL that carries credentials (FR-10.6).

## Comment hygiene

A comment explains what the code **does** and **why** — the invariant, constraint or trade-off it exists to protect — in present tense. It must be **self-contained**: comprehensible to a reader with no access to the issue tracker and no knowledge of any earlier version of the file. Three habits break that and are prohibited; a fourth — repeating what is already written — buries the one sentence that matters and is prohibited too.

This governs comments **in code** (`//` and `///` in `.cs`, `<!-- -->` in `.xaml`). Markdown — plans, the PRD, these rules, READMEs — keeps its cross-references.

### Always document types and members with XML doc comments

Give **every type and every method** — public or not — a `/// <summary>`, plus `<param>`, `<returns>`, `<exception>` and `<remarks>` where they add something. Document properties, fields and enum members unless the name already says it all. Reference code with `<see cref="…"/>`, keywords with `<see langword="null"/>`, inline identifiers with `<c>…</c>`. A summary starts with a present-tense verb in the third person ("Resolves…", "Returns…").

An interface implementation or override whose contract is the base member's carries `/// <inheritdoc />`. Write a summary of its own only for what the implementation adds.

**Never write a plain `//` block above a type or member to describe it** — that is what `///` is for. `//` comments inside a method body stay `//`. Whatever the form, the content obeys the four rules below.

### 1. Do not narrate the change

A comment describing what a change did, relative to a previous state, is a commit message stranded in the source. It parses only for someone who saw the prior version and decays silently.

Tell-tale phrases: *no longer*, *used to*, *previously*, *instead of the old*, *replaced X*, *now uses*, *has been moved to*. They are a signal to re-read, not a blanket ban: *"repositories already in the list are unaffected"* describes present behaviour and is fine; *"no longer fetches before the quick check"* describes an edit and is not.

❌ BAD:

```csharp
// Now uses ls-remote first instead of always fetching, which was too slow for large accounts.
```

✅ GOOD:

```csharp
// ls-remote compares the server refs with the last archive's snapshot without touching the clone,
// so an unchanged repository costs one round trip instead of a fetch.
```

### 2. Do not reference issues

An `(#12)` tag or prose naming a task (*see #12*, *per F1.9*) says nothing to a reader who does not open the tracker, lets the pointer replace the reasoning, and duplicates what `git blame` → commit → issue already provides.

❌ BAD:

```csharp
// Tags are compared too (#16).
```

✅ GOOD:

```csharp
// Tags are part of the snapshot: moving or deleting a tag changes what a restore would contain,
// so it must produce a new archive just like a new commit.
```

**Last-resort exception:** a non-obvious **external** constraint whose analysis exists only in an issue (a third-party bug). The comment states the constraint first and in full; the reference is supplementary.

### 3. Cite symbols, not line numbers

A comment that points at other code names the **symbol** — class, method, property — never `File.cs:42` or `File.cs:42-60`; the number points at the wrong line after the next edit. The bare file name is fine.

❌ BAD: `// Same rule as RepositoryPathResolver.cs:58.`
✅ GOOD: `// Same rule as RepositoryPathResolver.ResolveArchiveFolder.`

### 4. Do not repeat what is already written

- An implementation does not re-document its interface — use `/// <inheritdoc />`.
- Once a comment names a member with `<see cref="…"/>`, it does not paraphrase that member's documentation.
- `<remarks>` carries the non-obvious part of the contract only, stated once; the rationale for a design choice lives where the choice is made.
- A comment that retells the next line adds nothing; one that says why a value was chosen does.
- A summary running past two or three lines usually repeats something — cut before adding.

### Where references belong

Issue references are correct in commit messages, PR descriptions, issue comments and Markdown documents. Enforcement is review-side: before committing, read back only the comment lines the change adds (`git diff -U0 -- '*.cs' '*.xaml'`, lines starting with `+` and containing `//`, `///` or `<!--`) and check each against the four rules — including comments a subagent wrote.

## Style

- Formatting follows `.editorconfig`; `dotnet format` is the arbiter.
- File-scoped namespaces, one top-level type per file, file named after the type.
- PascalCase for types, members and constants; `_camelCase` for private fields; camelCase for locals and parameters; interfaces start with `I`; async methods end with `Async`.
- Use the latest C# (14): pattern matching and switch expressions, collection expressions (`[]`), `required` and `init` members, records for immutable data, `nameof` instead of member-name strings.
- `var` for every local, including built-in types and when the type is not apparent from the right-hand side (enforced as a build error).
- Target-typed `new()` for fields, properties and arguments when the type is apparent from the target; keep the explicit type in `throw` and when the target is a base type or interface.
- New extension members use the C# 14 `extension(TReceiver receiver) { … }` block form.
- Prefer a named type over an anonymous object or `dynamic` whenever one models the shape.
- `is null` / `is not null` instead of `== null`; trust nullable annotations and validate only at entry points (`ArgumentNullException.ThrowIfNull`, `ArgumentException.ThrowIfNullOrEmpty`).
- `sealed` for classes not designed for inheritance; `internal` for types not used outside their project (expose to tests with `InternalsVisibleTo`).

## When generating code

- Generate code that compiles against the current interfaces and project references; do not assume members that do not exist. If a referenced type is missing, say so instead of inventing it.
- Keep changes focused; propose large refactors separately.
