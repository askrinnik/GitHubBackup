using System.IO.Abstractions;
using GitHubBackup.Core.Changes;
using GitHubBackup.Core.Clones;
using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Git;
using GitHubBackup.Infrastructure.Processes;
using GitHubBackup.TestUtilities;
using GitHubBackup.TestUtilities.Git;
using GitHubBackup.TestUtilities.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;

namespace GitHubBackup.Infrastructure.IntegrationTests.Discovery;

/// <summary>
/// Tests <see cref="CloneRelocator"/> with the real <see cref="GitClient"/>, the real file system and the installed
/// git against local bare repositories.
/// </summary>
public sealed class CloneRelocatorTests : IDisposable
{
    private readonly LocalGitSandbox _sandbox = new();
    private readonly TestTelemetry _telemetry = new();
    private readonly SecretMasker _masker = new();
    private readonly FakeLogger<ProcessRunner> _runnerLogger = new();
    private readonly FileSystem _fileSystem = new();
    private readonly string _sourcesRoot;

    /// <summary>
    /// Creates the sandbox and the clone root.
    /// </summary>
    public CloneRelocatorTests() => _sourcesRoot = _sandbox.CreateFolder("sources");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RelocateAsync_CloneExists_MovesCloneAndSetsNewRemoteUrl()
    {
        var remote = await _sandbox.CreateRemoteAsync("app", CancellationToken);
        var head = await remote.CommitAsync("a.txt", "a", "Second commit", CancellationToken);
        var renamed = await _sandbox.CreateRemoteAsync("app2", CancellationToken);
        var oldFolder = Path.Combine(_sourcesRoot, "octo", "app");
        var newFolder = Path.Combine(_sourcesRoot, "octo", "app2");
        await CreateGitClient().CloneAsync(Request(oldFolder), remote.Url, CancellationToken);

        var result = await CreateRelocator().RelocateAsync(Relocation(oldFolder, newFolder, renamed.Url, remote.Url), CancellationToken);

        result.Result.ShouldBe(CloneRelocationResult.Moved);
        Directory.Exists(oldFolder).ShouldBeFalse();
        (await GitAsync(newFolder, "remote", "get-url", "origin")).ShouldBe([renamed.Url]);
        (await GitAsync(newFolder, "rev-parse", "HEAD")).ShouldBe([head]);
    }

    [Fact]
    public async Task RelocateAsync_MoveFails_DeletesOldCloneAndChangeDetectorReclones()
    {
        var remote = await _sandbox.CreateRemoteAsync("app", CancellationToken);
        var renamed = await _sandbox.CreateRemoteAsync("app2", CancellationToken);
        var head = await renamed.CommitAsync("b.txt", "b", "Renamed commit", CancellationToken);
        var oldFolder = Path.Combine(_sourcesRoot, "octo", "app");
        var newFolder = Path.Combine(_sourcesRoot, "octo", "app2");
        await CreateGitClient().CloneAsync(Request(oldFolder), remote.Url, CancellationToken);

        var result = await new CloneRelocator(
                new MoveFailingFileSystem(), CreateGitClient(), CreateGuard(), NullLogger<CloneRelocator>.Instance)
            .RelocateAsync(Relocation(oldFolder, newFolder, renamed.Url, remote.Url), CancellationToken);

        result.Result.ShouldBe(CloneRelocationResult.DeletedForReclone);
        Directory.Exists(oldFolder).ShouldBeFalse();
        Directory.Exists(newFolder).ShouldBeFalse();

        var detection = await new ChangeDetector(CreateGitClient(), CreateGuard(), _fileSystem, _telemetry.Telemetry, NullLogger<ChangeDetector>.Instance)
            .DetectAsync(
                new ChangeDetectionRequest
                {
                    Git = Request(newFolder),
                    RemoteUrl = renamed.Url,
                    ArchiveFolder = _sandbox.CreateFolder("archives"),
                },
                CancellationToken);

        detection.Decision.ShouldBe(ChangeDecision.ArchiveNeeded);
        detection.Reason.ShouldBe(ChangeReason.NoArchive);
        (await GitAsync(newFolder, "remote", "get-url", "origin")).ShouldBe([renamed.Url]);
        (await GitAsync(newFolder, "rev-parse", "HEAD")).ShouldBe([head]);
    }

    [Fact]
    public async Task RelocateAsync_TargetOccupied_KeepsBothFolders()
    {
        var remote = await _sandbox.CreateRemoteAsync("app", CancellationToken);
        var other = await _sandbox.CreateRemoteAsync("app2", CancellationToken);
        var oldFolder = Path.Combine(_sourcesRoot, "octo", "app");
        var newFolder = Path.Combine(_sourcesRoot, "octo", "app2");
        await CreateGitClient().CloneAsync(Request(oldFolder), remote.Url, CancellationToken);
        await CreateGitClient().CloneAsync(Request(newFolder), other.Url, CancellationToken);

        var result = await CreateRelocator().RelocateAsync(Relocation(oldFolder, newFolder, other.Url, remote.Url), CancellationToken);

        result.Result.ShouldBe(CloneRelocationResult.TargetOccupied);
        (await GitAsync(oldFolder, "remote", "get-url", "origin")).ShouldBe([remote.Url]);
        (await GitAsync(newFolder, "remote", "get-url", "origin")).ShouldBe([other.Url]);
    }

    [Fact]
    public async Task RelocateAsync_WikiClone_MovesWikiFolder()
    {
        var wiki = await _sandbox.CreateRemoteAsync("app.wiki", CancellationToken);
        var renamedWiki = await _sandbox.CreateRemoteAsync("app2.wiki", CancellationToken);
        var oldFolder = Path.Combine(_sourcesRoot, "octo", "app.wiki");
        var newFolder = Path.Combine(_sourcesRoot, "octo", "app2.wiki");
        await CreateGitClient().CloneAsync(Request(oldFolder), wiki.Url, CancellationToken);

        var result = await CreateRelocator().RelocateAsync(Relocation(oldFolder, newFolder, renamedWiki.Url, wiki.Url), CancellationToken);

        result.Result.ShouldBe(CloneRelocationResult.Moved);
        Directory.Exists(oldFolder).ShouldBeFalse();
        (await GitAsync(newFolder, "remote", "get-url", "origin")).ShouldBe([renamedWiki.Url]);
    }

    [Fact]
    public async Task RelocateAsync_CloneHasUncommittedChange_LeavesCloneUntouched()
    {
        var remote = await _sandbox.CreateRemoteAsync("app", CancellationToken);
        var renamed = await _sandbox.CreateRemoteAsync("app2", CancellationToken);
        var oldFolder = Path.Combine(_sourcesRoot, "octo", "app");
        var newFolder = Path.Combine(_sourcesRoot, "octo", "app2");
        await CreateGitClient().CloneAsync(Request(oldFolder), remote.Url, CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(oldFolder, "README.md"), "edited", CancellationToken);

        var result = await CreateRelocator().RelocateAsync(Relocation(oldFolder, newFolder, renamed.Url, remote.Url), CancellationToken);

        result.Result.ShouldBe(CloneRelocationResult.LocalChanges);
        result.Reason.ShouldBe(
            $"The clone '{oldFolder}' has local changes: uncommitted changes. Commit and push them or switch the branch, then run the backup again.");
        Directory.Exists(newFolder).ShouldBeFalse();
        (await File.ReadAllTextAsync(Path.Combine(oldFolder, "README.md"), CancellationToken)).ShouldBe("edited");
        (await GitAsync(oldFolder, "remote", "get-url", "origin")).ShouldBe([remote.Url]);
    }

    [Fact]
    public async Task RelocateAsync_WorkingCopyOfOtherRepository_NeitherMovesNorChangesOrigin()
    {
        var remote = await _sandbox.CreateRemoteAsync("app", CancellationToken);
        var foreign = await _sandbox.CreateRemoteAsync("foreign", CancellationToken);
        var oldFolder = Path.Combine(_sourcesRoot, "octo", "app");
        var newFolder = Path.Combine(_sourcesRoot, "octo", "app2");
        await CreateGitClient().CloneAsync(Request(oldFolder), foreign.Url, CancellationToken);

        var result = await CreateRelocator().RelocateAsync(
            Relocation(oldFolder, newFolder, remote.Url, remote.Url), CancellationToken);

        result.Result.ShouldBe(CloneRelocationResult.ForeignOrigin);
        Directory.Exists(newFolder).ShouldBeFalse();
        (await GitAsync(oldFolder, "remote", "get-url", "origin")).ShouldBe([foreign.Url]);
    }

    public void Dispose()
    {
        _telemetry.Dispose();
        _sandbox.Dispose();
    }

    /// <summary>
    /// Creates a git client over the real file system and the real process runner.
    /// </summary>
    /// <returns>The git client.</returns>
    private GitClient CreateGitClient() =>
        new(
            new ProcessRunner(_masker, _fileSystem, TimeProvider.System, _runnerLogger),
            _fileSystem,
            _masker,
            NullLogger<GitClient>.Instance);

    /// <summary>
    /// Creates the relocator under test.
    /// </summary>
    /// <returns>The relocator.</returns>
    private CloneRelocator CreateRelocator() =>
        new(_fileSystem, CreateGitClient(), CreateGuard(), NullLogger<CloneRelocator>.Instance);

    /// <summary>
    /// Creates the guard over a real git client.
    /// </summary>
    /// <returns>The guard.</returns>
    private CloneGuard CreateGuard() => new(CreateGitClient(), NullLogger<CloneGuard>.Instance);

    /// <summary>
    /// Returns the relocation of a clone on <c>main</c> under the clone root, deleted when it cannot be moved.
    /// </summary>
    /// <param name="oldFolder">The folder the clone is in.</param>
    /// <param name="newFolder">The folder it moves to.</param>
    /// <param name="url">The URL origin points at after the move.</param>
    /// <param name="previousUrl">The URL origin points at before the move.</param>
    /// <returns>The request.</returns>
    private CloneRelocationRequest Relocation(string oldFolder, string newFolder, string url, string previousUrl) => new()
    {
        GitPath = _sandbox.GitPath,
        OldFolder = oldFolder,
        NewFolder = newFolder,
        RemoteUrl = url,
        PreviousRemoteUrl = previousUrl,
        Branch = "main",
        DeleteOnFailure = true,
    };

    /// <summary>
    /// Returns a request for a clone folder on the default branch.
    /// </summary>
    /// <param name="clonePath">The clone folder.</param>
    /// <returns>The request.</returns>
    private GitRepositoryRequest Request(string clonePath) => new()
    {
        GitPath = _sandbox.GitPath,
        ClonePath = clonePath,
        Branch = "main",
    };

    /// <summary>
    /// Runs git in the isolated environment of the sandbox to inspect a clone.
    /// </summary>
    /// <param name="workingDirectory">The folder git runs in.</param>
    /// <param name="arguments">The git arguments.</param>
    /// <returns>The non-empty lines of standard output.</returns>
    private Task<IReadOnlyList<string>> GitAsync(string workingDirectory, params string[] arguments) =>
        _sandbox.RunGitAsync(workingDirectory, CancellationToken, arguments);
}
