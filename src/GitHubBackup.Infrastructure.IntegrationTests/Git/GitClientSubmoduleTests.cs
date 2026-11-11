namespace GitHubBackup.Infrastructure.IntegrationTests.Git;

/// <summary>
/// Tests <see cref="GitHubBackup.Infrastructure.Git.GitClient"/> on cloning and updating repositories with submodules.
/// </summary>
public sealed class GitClientSubmoduleTests : GitClientTestBase
{
    [Fact]
    public async Task CloneAsync_NestedSubmodules_ChecksOutEveryLevel()
    {
        var leaf = await _sandbox.CreateRemoteAsync("leaf", CancellationToken);
        var middle = await _sandbox.CreateRemoteAsync("middle", CancellationToken);
        await middle.AddSubmoduleAsync(leaf, "leaf", CancellationToken);
        var top = await _sandbox.CreateRemoteAsync("top", CancellationToken);
        await top.AddSubmoduleAsync(middle, "middle", CancellationToken);

        await CreateClient().CloneAsync(Request(), top.Url, CancellationToken);

        File.ReadAllText(Path.Combine(_clonePath, "middle", "README.md")).ShouldBe("middle");
        File.ReadAllText(Path.Combine(_clonePath, "middle", "leaf", "README.md")).ShouldBe("leaf");
    }

    [Fact]
    public async Task UpdateAsync_SubmoduleAdvanced_ChecksOutNewSubmoduleCommit()
    {
        var submodule = await _sandbox.CreateRemoteAsync("sub", CancellationToken);
        var top = await _sandbox.CreateRemoteAsync("top", CancellationToken);
        await top.AddSubmoduleAsync(submodule, "sub", CancellationToken);
        await CreateClient().CloneAsync(Request(), top.Url, CancellationToken);
        var submoduleHead = await submodule.CommitAsync("new.txt", "new", "Submodule commit", CancellationToken);
        await _sandbox.RunGitAsync(top.WorkingPath, CancellationToken, "submodule", "update", "--remote", "sub");
        await _sandbox.RunGitAsync(top.WorkingPath, CancellationToken, "commit", "-am", "Advance submodule");
        await _sandbox.RunGitAsync(top.WorkingPath, CancellationToken, "push", "origin", "HEAD");

        await CreateClient().UpdateAsync(Request(), CancellationToken);

        (await HeadAsync(Path.Combine(_clonePath, "sub"))).ShouldBe(submoduleHead);
        File.Exists(Path.Combine(_clonePath, "sub", "new.txt")).ShouldBeTrue();
    }
}
