using GitHubBackup.Core.Git;

namespace GitHubBackup.Infrastructure.IntegrationTests.Git;

/// <summary>
/// Tests <see cref="GitHubBackup.Infrastructure.Git.GitClient"/> on cloning a repository into a new clone folder and the failures when the clone folder is not a clone.
/// </summary>
public sealed class GitClientCloneTests : GitClientTestBase
{
    [Fact]
    public async Task CloneAsync_RemoteWithBranchesAndTags_ChecksOutBranchAndFetchesEveryRef()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        var head = await remote.CommitAsync("a.txt", "a", "Second commit", CancellationToken);
        await remote.CreateBranchAsync("develop", null, CancellationToken);
        await remote.CreateTagAsync("v1", null, annotated: true, force: false, CancellationToken);
        await remote.CreateTagAsync("light", null, annotated: false, force: false, CancellationToken);

        await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);

        (await HeadAsync(_clonePath)).ShouldBe(head);
        (await GitAsync(_clonePath, "rev-parse", "--abbrev-ref", "HEAD")).ShouldBe(["main"]);
        (await RefsAsync(_clonePath)).ShouldBe(
            ["refs/remotes/origin/HEAD", "refs/remotes/origin/develop", "refs/remotes/origin/main", "refs/tags/light", "refs/tags/v1"],
            ignoreOrder: true);
        File.ReadAllText(Path.Combine(_clonePath, "a.txt")).ShouldBe("a");
    }

    [Fact]
    public async Task CloneAsync_NonDefaultBranch_ChecksItOut()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CreateBranchAsync("develop", null, CancellationToken);
        await remote.CheckoutAsync("develop", CancellationToken);
        var developHead = await remote.CommitAsync("dev.txt", "dev", "Develop commit", CancellationToken);

        await CreateClient().CloneAsync(Request("develop"), remote.Url, CancellationToken);

        (await HeadAsync(_clonePath)).ShouldBe(developHead);
        (await GitAsync(_clonePath, "rev-parse", "--abbrev-ref", "HEAD")).ShouldBe(["develop"]);
        File.Exists(Path.Combine(_clonePath, "dev.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task CloneAsync_PathLongerThanMaxPath_ChecksOutEveryFile()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        var segment = new string('d', 60);
        var relativePath = Path.Combine(segment + "1", segment + "2", segment + "3", segment + "4", "file.txt");
        await remote.CommitAsync(relativePath, "deep", "Deep file", CancellationToken);

        await CreateClient().CloneAsync(Request(), remote.Url, CancellationToken);

        var fullPath = Path.Combine(_clonePath, relativePath);
        fullPath.Length.ShouldBeGreaterThan(260);
        File.ReadAllText(fullPath).ShouldBe("deep");
    }

    [Fact]
    public async Task CloneAsync_RemoteMissing_ThrowsGitCommandException()
    {
        var url = new Uri(Path.Combine(_sandbox.RootPath, "remotes", "missing.git")).AbsoluteUri;

        var exception = await Should.ThrowAsync<GitCommandException>(
            () => CreateClient().CloneAsync(Request(), url, CancellationToken));

        exception.Command.ShouldStartWith("git clone");
        exception.ExitCode.ShouldNotBe(0);
    }

    [Theory]
    [InlineData("update")]
    [InlineData("local")]
    [InlineData("remote")]
    [InlineData("default")]
    public async Task Operation_ClonePathWithoutGitInsideAnotherRepository_ThrowsGitCommandException(string operation)
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CommitAsync("a.txt", "a", "Initial commit", CancellationToken);
        var enclosingRepository = Path.GetDirectoryName(Path.GetDirectoryName(_clonePath))!;
        Directory.CreateDirectory(_clonePath);
        await GitAsync(enclosingRepository, "init", "-b", "main");
        await GitAsync(enclosingRepository, "remote", "add", "origin", remote.Url);
        var client = CreateClient();

        var exception = await Should.ThrowAsync<GitCommandException>(() => operation switch
        {
            "update" => client.UpdateAsync(Request(), CancellationToken),
            "local" => client.GetLocalRefsAsync(Request(), CancellationToken),
            "remote" => client.GetRemoteRefsAsync(Request(), CancellationToken),
            _ => client.GetDefaultBranchAsync(Request(), CancellationToken),
        });

        exception.ExitCode.ShouldNotBe(0);
        Directory.Exists(Path.Combine(_clonePath, ".git")).ShouldBeFalse();
    }
}
