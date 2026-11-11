namespace GitHubBackup.Infrastructure.IntegrationTests.Git;

/// <summary>
/// Tests <see cref="GitHubBackup.Infrastructure.Git.GitClient"/> on changing the <c>origin</c> URL of a clone.
/// </summary>
public sealed class GitClientRemoteUrlTests : GitClientTestBase
{
    [Fact]
    public async Task SetRemoteUrlAsync_ExistingClone_ChangesOrigin()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);
        var renamed = await _sandbox.CreateRemoteAsync("renamed", CancellationToken);

        await CreateClient().SetRemoteUrlAsync(Request(), renamed.Url, CancellationToken);

        (await GitAsync(_clonePath, "remote", "get-url", "origin")).ShouldBe([renamed.Url]);
        _clientLogger.Entries.ShouldContain(entry => entry.Message.Contains(renamed.Url));
    }

    [Theory]
    [InlineData("--upload-pack=evil")]
    [InlineData(" ")]
    public async Task SetRemoteUrlAsync_InvalidUrl_ThrowsWithoutRunningGit(string url)
    {
        var exception = await Should.ThrowAsync<ArgumentException>(
            () => CreateClient().SetRemoteUrlAsync(Request(), url, CancellationToken));

        exception.ParamName.ShouldBe("remoteUrl");
        _runnerLogger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetRemoteUrlAsync_ClonePathNotFullyQualified_Throws()
    {
        var request = Request() with { ClonePath = @"owner\repo" };

        var exception = await Should.ThrowAsync<ArgumentException>(
            () => CreateClient().SetRemoteUrlAsync(request, "https://github.com/octo/repo.git", CancellationToken));

        exception.ParamName.ShouldBe("request");
    }
}
