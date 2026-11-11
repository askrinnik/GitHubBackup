using GitHubBackup.Core.Git;

namespace GitHubBackup.Infrastructure.IntegrationTests.Git;

/// <summary>
/// Tests <see cref="GitHubBackup.Infrastructure.Git.GitClient"/> on cloning and updating a repository without commits.
/// </summary>
public sealed class GitClientEmptyRepositoryTests : GitClientTestBase
{
    [Fact]
    public async Task CloneAsync_EmptyRepository_ClonesWithoutCheckoutAndReportsEmpty()
    {
        var remote = await _sandbox.CreateEmptyRemoteAsync("repo", CancellationToken);

        var result = await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);

        result.IsEmpty.ShouldBeTrue();
        result.Branch.ShouldBeNull();
        result.BranchFallback.ShouldBeFalse();
        result.Refs.ShouldBe(RefSnapshot.Empty);
        Directory.Exists(Path.Combine(_clonePath, ".git")).ShouldBeTrue();
        _clientLogger.Entries.ShouldContain(entry => entry.Message.Contains("is empty on the server"));
        (await CreateClient().GetRemoteRefsAsync(Request(), CancellationToken)).ShouldBe(RefSnapshot.Empty);
        (await CreateClient().GetDefaultBranchAsync(Request(), CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task UpdateAsync_EmptyRepository_ReportsEmpty()
    {
        var remote = await _sandbox.CreateEmptyRemoteAsync("repo", CancellationToken);
        await CreateClient().CloneAsync(Request(branch: null), remote.Url, CancellationToken);

        var result = await CreateClient().UpdateAsync(Request(branch: null), CancellationToken);

        result.IsEmpty.ShouldBeTrue();
        result.Refs.ShouldBe(RefSnapshot.Empty);
        _clientLogger.Entries.Count(entry => entry.Message.Contains("is empty on the server")).ShouldBe(2);
    }

    [Fact]
    public async Task UpdateAsync_FirstCommitPushedToEmptyRepository_ChecksItOut()
    {
        var remote = await _sandbox.CreateEmptyRemoteAsync("repo", CancellationToken);
        await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);
        var head = await remote.CommitAsync("first.txt", "first", "First commit", CancellationToken);

        var result = await CreateClient().UpdateAsync(Request(), CancellationToken);

        result.IsEmpty.ShouldBeFalse();
        result.Branch.ShouldBe("main");
        (await HeadAsync(_clonePath)).ShouldBe(head);
        File.ReadAllText(Path.Combine(_clonePath, "first.txt")).ShouldBe("first");
    }
}
