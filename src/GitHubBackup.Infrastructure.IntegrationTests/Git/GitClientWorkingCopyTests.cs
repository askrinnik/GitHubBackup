using GitHubBackup.Core.Git;

namespace GitHubBackup.Infrastructure.IntegrationTests.Git;

/// <summary>
/// Tests how <see cref="GitHubBackup.Infrastructure.Git.GitClient"/> reads the local state of a clone: its origin,
/// its changes, its branch, its unpushed commits and its ignored files.
/// </summary>
public sealed class GitClientWorkingCopyTests : GitClientTestBase
{
    [Fact]
    public async Task GetWorkingCopyStatusAsync_FreshClone_ReportsCleanBranch()
    {
        await CloneAsync();

        var status = await CreateClient().GetWorkingCopyStatusAsync(Request(), null, CancellationToken);

        status.ShouldBe(new WorkingCopyStatus("main", true, false, false, 0));
    }

    [Fact]
    public async Task GetWorkingCopyStatusAsync_TrackedFileChanged_ReportsUncommittedChanges()
    {
        await CloneAsync();
        File.WriteAllText(Path.Combine(_clonePath, "README.md"), "changed");

        var status = await CreateClient().GetWorkingCopyStatusAsync(Request(), null, CancellationToken);

        status.HasUncommittedChanges.ShouldBeTrue();
        status.HasUntrackedFiles.ShouldBeFalse();
    }

    [Fact]
    public async Task GetWorkingCopyStatusAsync_UntrackedAndIgnoredFiles_ReportsOnlyUntrackedOnes()
    {
        await CloneAsync(".gitignore", "*.log\n");
        File.WriteAllText(Path.Combine(_clonePath, "build.log"), "ignored");

        var ignoredOnly = await CreateClient().GetWorkingCopyStatusAsync(Request(), null, CancellationToken);
        File.WriteAllText(Path.Combine(_clonePath, "notes.txt"), "untracked");
        var withUntracked = await CreateClient().GetWorkingCopyStatusAsync(Request(), null, CancellationToken);

        ignoredOnly.HasUntrackedFiles.ShouldBeFalse();
        withUntracked.HasUntrackedFiles.ShouldBeTrue();
        withUntracked.HasUncommittedChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task GetWorkingCopyStatusAsync_OtherBranchAndDetachedHead_ReportsThem()
    {
        await CloneAsync();
        await GitAsync(_clonePath, "checkout", "-b", "feature");
        var onFeature = await CreateClient().GetWorkingCopyStatusAsync(Request(), null, CancellationToken);
        await GitAsync(_clonePath, "checkout", "--detach");
        var detached = await CreateClient().GetWorkingCopyStatusAsync(Request(), null, CancellationToken);

        onFeature.Branch.ShouldBe("feature");
        onFeature.UnpushedCommits.ShouldBe(0);
        detached.IsDetached.ShouldBeTrue();
    }

    [Fact]
    public async Task GetWorkingCopyStatusAsync_LocalCommits_CountsThemWithoutFetching()
    {
        var remote = await CloneAsync();
        File.WriteAllText(Path.Combine(_clonePath, "a.txt"), "a");
        await GitAsync(_clonePath, "add", "a.txt");
        await GitAsync(_clonePath, "commit", "-m", "Local one");
        await GitAsync(_clonePath, "checkout", "-b", "feature");
        File.WriteAllText(Path.Combine(_clonePath, "b.txt"), "b");
        await GitAsync(_clonePath, "add", "b.txt");
        await GitAsync(_clonePath, "commit", "-m", "Local two");
        await remote.CommitAsync("server.txt", "server", "Server commit", CancellationToken);
        var refsBefore = await RefsAsync(_clonePath);

        var onFeature = await CreateClient().GetWorkingCopyStatusAsync(Request(), null, CancellationToken);
        await GitAsync(_clonePath, "checkout", "main");
        var onMain = await CreateClient().GetWorkingCopyStatusAsync(Request(), null, CancellationToken);

        onFeature.UnpushedCommits.ShouldBe(2);
        onMain.UnpushedCommits.ShouldBe(1);
        (await RefsAsync(_clonePath)).ShouldBe(refsBefore);
    }

    [Fact]
    public async Task GetWorkingCopyStatusAsync_Read_DoesNotChangeIndexOrRefs()
    {
        await CloneAsync();
        File.WriteAllText(Path.Combine(_clonePath, "README.md"), "changed");
        var index = Path.Combine(_clonePath, ".git", "index");
        var indexBefore = File.ReadAllBytes(index);
        var refsBefore = await GitAsync(_clonePath, "for-each-ref");

        await CreateClient().GetWorkingCopyStatusAsync(Request(), null, CancellationToken);

        File.ReadAllBytes(index).ShouldBe(indexBefore);
        (await GitAsync(_clonePath, "for-each-ref")).ShouldBe(refsBefore);
    }

    [Fact]
    public async Task GetWorkingCopyStatusAsync_SubmoduleChanged_ReportsUncommittedChanges()
    {
        var library = await _sandbox.CreateRemoteAsync("library", CancellationToken);
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.AddSubmoduleAsync(library, "libs/library", CancellationToken);
        await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);
        File.WriteAllText(Path.Combine(_clonePath, "libs", "library", "README.md"), "changed in submodule");

        var status = await CreateClient().GetWorkingCopyStatusAsync(Request(), null, CancellationToken);

        status.HasUncommittedChanges.ShouldBeTrue();
    }

    [Fact]
    public async Task GetOriginUrlAsync_CloneAndRemovedOrigin_ReturnsUrlOrNull()
    {
        var remote = await CloneAsync();

        var url = await CreateClient().GetOriginUrlAsync(Request(), CancellationToken);
        await GitAsync(_clonePath, "remote", "remove", "origin");
        var removed = await CreateClient().GetOriginUrlAsync(Request(), CancellationToken);

        url.ShouldBe(remote.Url);
        removed.ShouldBeNull();
    }

    [Fact]
    public async Task GetIgnoredPathsAsync_CloneWithSubmodule_ListsIgnoredPathsOfBoth()
    {
        var library = await _sandbox.CreateRemoteAsync("library", CancellationToken);
        await library.CommitAsync(".gitignore", "out/\n", "Ignore output", CancellationToken);
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CommitAsync(".gitignore", "*.log\nbin/\n", "Ignore logs", CancellationToken);
        await remote.AddSubmoduleAsync(library, "libs/library", CancellationToken);
        await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);
        File.WriteAllText(Path.Combine(_clonePath, "build.log"), "ignored");
        Directory.CreateDirectory(Path.Combine(_clonePath, "bin", "Debug"));
        File.WriteAllText(Path.Combine(_clonePath, "bin", "Debug", "app.dll"), "ignored");
        Directory.CreateDirectory(Path.Combine(_clonePath, "libs", "library", "out"));
        File.WriteAllText(Path.Combine(_clonePath, "libs", "library", "out", "lib.dll"), "ignored");
        File.WriteAllText(Path.Combine(_clonePath, "notes.txt"), "untracked, not ignored");

        var paths = await CreateClient().GetIgnoredPathsAsync(Request(), CancellationToken);

        paths.Order(StringComparer.Ordinal).ShouldBe(["bin", "build.log", @"libs\library\out"]);
    }

    /// <summary>
    /// Creates a remote, optionally with one more committed file, and clones it into the test clone folder.
    /// </summary>
    /// <param name="fileName">The file to commit before cloning, or <see langword="null"/>.</param>
    /// <param name="content">The content of <paramref name="fileName"/>.</param>
    /// <returns>The remote.</returns>
    private async Task<TestUtilities.Git.LocalGitRemote> CloneAsync(string? fileName = null, string content = "")
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        if (fileName is not null)
        {
            await remote.CommitAsync(fileName, content, "Add " + fileName, CancellationToken);
        }

        await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);
        return remote;
    }
}
