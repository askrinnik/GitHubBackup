namespace GitHubBackup.Infrastructure.IntegrationTests.Git;

/// <summary>
/// Tests <see cref="GitHubBackup.Infrastructure.Git.GitClient"/> on updating an existing clone to the server state.
/// </summary>
public sealed class GitClientUpdateTests : GitClientTestBase
{
    [Fact]
    public async Task UpdateAsync_NewCommitOnServer_ChecksItOut()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);
        var head = await remote.CommitAsync("new.txt", "new", "New commit", CancellationToken);

        await CreateClient().UpdateAsync(Request(), CancellationToken);

        (await HeadAsync(_clonePath)).ShouldBe(head);
        File.ReadAllText(Path.Combine(_clonePath, "new.txt")).ShouldBe("new");
    }

    [Fact]
    public async Task UpdateAsync_ServerHistoryForcePushed_FollowsRewrittenHistory()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        var initial = (await remote.GetRemoteRefsAsync(CancellationToken))["refs/heads/main"];
        await remote.CommitAsync("dropped.txt", "dropped", "Commit to drop", CancellationToken);
        await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);
        await remote.ForcePushAsync("main", initial, CancellationToken);
        var head = await remote.CommitAsync("rewritten.txt", "rewritten", "Rewritten commit", CancellationToken);

        await CreateClient().UpdateAsync(Request(), CancellationToken);

        (await HeadAsync(_clonePath)).ShouldBe(head);
        File.Exists(Path.Combine(_clonePath, "dropped.txt")).ShouldBeFalse();
        File.Exists(Path.Combine(_clonePath, "rewritten.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateAsync_LocalChanges_DiscardsModifiedUntrackedAndIgnoredFiles()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CommitAsync(".gitignore", "*.log\n", "Ignore logs", CancellationToken);
        await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);
        File.WriteAllText(Path.Combine(_clonePath, "README.md"), "changed");
        File.WriteAllText(Path.Combine(_clonePath, "untracked.txt"), "untracked");
        Directory.CreateDirectory(Path.Combine(_clonePath, "untracked-folder"));
        File.WriteAllText(Path.Combine(_clonePath, "untracked-folder", "file.txt"), "untracked");
        File.WriteAllText(Path.Combine(_clonePath, "build.log"), "ignored");

        await CreateClient().UpdateAsync(Request(), CancellationToken);

        File.ReadAllText(Path.Combine(_clonePath, "README.md")).ShouldBe("repo");
        File.Exists(Path.Combine(_clonePath, "untracked.txt")).ShouldBeFalse();
        Directory.Exists(Path.Combine(_clonePath, "untracked-folder")).ShouldBeFalse();
        File.Exists(Path.Combine(_clonePath, "build.log")).ShouldBeFalse();
        (await GitAsync(_clonePath, "status", "--porcelain", "--ignored")).ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_KeepIgnoredFiles_KeepsIgnoredFilesAndRemovesUntrackedOnes()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CommitAsync(".gitignore", "*.log\nbin/\n", "Ignore logs", CancellationToken);
        await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);
        File.WriteAllText(Path.Combine(_clonePath, "untracked.txt"), "untracked");
        File.WriteAllText(Path.Combine(_clonePath, "build.log"), "ignored");
        Directory.CreateDirectory(Path.Combine(_clonePath, "bin"));
        File.WriteAllText(Path.Combine(_clonePath, "bin", "app.dll"), "ignored");
        var head = await remote.CommitAsync("new.txt", "new", "New commit", CancellationToken);

        await CreateClient().UpdateAsync(Request() with { KeepIgnoredFiles = true }, CancellationToken);

        (await HeadAsync(_clonePath)).ShouldBe(head);
        File.Exists(Path.Combine(_clonePath, "untracked.txt")).ShouldBeFalse();
        File.ReadAllText(Path.Combine(_clonePath, "build.log")).ShouldBe("ignored");
        File.ReadAllText(Path.Combine(_clonePath, "bin", "app.dll")).ShouldBe("ignored");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateAsync_HooksInCloneOrHooksPath_DoNotRun(bool configuredHooksPath)
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);
        var hooksFolder = configuredHooksPath ? Path.Combine(_clonePath, ".husky") : Path.Combine(_clonePath, ".git", "hooks");
        Directory.CreateDirectory(hooksFolder);
        var marker = Path.Combine(_sandbox.RootPath, "hook-ran.txt");
        var script = "#!/bin/sh\necho ran > '" + marker.Replace('\\', '/') + "'\n";
        foreach (var hook in new[] { "post-checkout", "post-merge", "reference-transaction" })
        {
            File.WriteAllText(Path.Combine(hooksFolder, hook), script);
        }

        if (configuredHooksPath)
        {
            await GitAsync(_clonePath, "config", "core.hooksPath", ".husky");
        }

        // Plain git runs the hook, so its absence below comes from the client and not from the machine.
        await GitAsync(_clonePath, "checkout", "main");
        File.Exists(marker).ShouldBeTrue();
        File.Delete(marker);
        await remote.CommitAsync("new.txt", "new", "New commit", CancellationToken);

        await CreateClient().UpdateAsync(Request() with { KeepIgnoredFiles = true }, CancellationToken);

        File.Exists(Path.Combine(_clonePath, "new.txt")).ShouldBeTrue();
        File.Exists(marker).ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateAsync_BranchDeletedAndTagsChanged_MirrorsServerRefs()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CreateBranchAsync("feature", null, CancellationToken);
        await remote.CreateTagAsync("moved", null, annotated: false, force: false, CancellationToken);
        await remote.CreateTagAsync("gone", null, annotated: false, force: false, CancellationToken);
        await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);
        var head = await remote.CommitAsync("new.txt", "new", "New commit", CancellationToken);
        await remote.CreateTagAsync("moved", null, annotated: false, force: true, CancellationToken);
        await remote.DeleteTagAsync("gone", CancellationToken);
        await remote.DeleteBranchAsync("feature", CancellationToken);

        await CreateClient().UpdateAsync(Request(), CancellationToken);

        (await RefsAsync(_clonePath)).ShouldBe(
            ["refs/remotes/origin/HEAD", "refs/remotes/origin/main", "refs/tags/moved"], ignoreOrder: true);
        (await GitAsync(_clonePath, "rev-parse", "refs/tags/moved")).ShouldBe([head]);
    }
}
