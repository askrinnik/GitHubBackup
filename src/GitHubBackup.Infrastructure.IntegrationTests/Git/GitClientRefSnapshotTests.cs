using GitHubBackup.Core.Git;
using GitHubBackup.Infrastructure.Processes;

namespace GitHubBackup.Infrastructure.IntegrationTests.Git;

/// <summary>
/// Tests <see cref="GitHubBackup.Infrastructure.Git.GitClient"/> on the ref snapshots of the server and of the clone.
/// </summary>
public sealed class GitClientRefSnapshotTests : GitClientTestBase
{
    /// <summary>The event id of the entry <see cref="ProcessRunner"/> logs for each line a process writes.</summary>
    private const int _outputLineEventId = 2;

    [Fact]
    public async Task GetRemoteRefsAsync_RemoteDeletedAfterClone_ThrowsGitRepositoryNotFoundException()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CommitAsync("a.txt", "a", "Initial commit", CancellationToken);
        var client = CreateClient();
        await client.CloneAsync(Request(), remote.Url, CancellationToken);
        var missing = new Uri(Path.Combine(_sandbox.RootPath, "remotes", "missing.git")).AbsoluteUri;
        await client.SetRemoteUrlAsync(Request(), missing, CancellationToken);

        var exception = await Should.ThrowAsync<GitRepositoryNotFoundException>(
            () => client.GetRemoteRefsAsync(Request(), CancellationToken));

        exception.Command.ShouldStartWith("git ls-remote");
        File.Exists(Path.Combine(_clonePath, "a.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task GetRemoteRefsAsync_UrlWithoutClone_ReadsBranchesAndTagsAndCreatesNothing()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CreateBranchAsync("develop", null, CancellationToken);
        await remote.CreateTagAsync("v1", null, annotated: false, force: false, CancellationToken);

        var refs = await CreateClient().GetRemoteRefsAsync(Request(), remote.Url, CancellationToken);

        refs.Refs.Keys.ShouldBe(["heads/develop", "heads/main", "tags/v1"], ignoreOrder: true);
        Directory.Exists(Path.Combine(_sandbox.RootPath, "backup")).ShouldBeFalse();
    }

    [Fact]
    public async Task GetRemoteRefsAsync_UrlWithoutCloneInsideEnclosingRepository_IgnoresItsConfiguration()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        var enclosing = Path.Combine(_sandbox.RootPath, "enclosing");
        var workingFolder = Path.Combine(enclosing, "sub");
        Directory.CreateDirectory(workingFolder);
        await GitAsync(enclosing, "init", "-b", "main");
        var missing = new Uri(Path.Combine(_sandbox.RootPath, "remotes", "missing.git")).AbsoluteUri;
        await GitAsync(enclosing, "config", $"url.{missing}.insteadOf", remote.Url);
        var request = Request() with { ClonePath = Path.Combine(workingFolder, "backup", "owner", "repo") };

        var refs = await CreateClient().GetRemoteRefsAsync(request, remote.Url, CancellationToken);

        refs.Refs.Keys.ShouldBe(["heads/main"]);
    }

    [Fact]
    public async Task GetRemoteRefsAsync_UrlWithoutRepository_ThrowsGitRepositoryNotFoundException()
    {
        var missing = new Uri(Path.Combine(_sandbox.RootPath, "remotes", "missing.git")).AbsoluteUri;

        var exception = await Should.ThrowAsync<GitRepositoryNotFoundException>(
            () => CreateClient().GetRemoteRefsAsync(Request(), missing, CancellationToken));

        exception.Command.ShouldStartWith("git ls-remote");
    }

    [Fact]
    public async Task Snapshots_OneServerState_RemoteAndLocalMatch()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CreateBranchAsync("develop", null, CancellationToken);
        await remote.CreateBranchAsync("feature/nested", null, CancellationToken);
        await remote.CreateTagAsync("v1", null, annotated: true, force: false, CancellationToken);
        await remote.CreateTagAsync("light", null, annotated: false, force: false, CancellationToken);
        await _sandbox.RunGitAsync(remote.WorkingPath, CancellationToken, "push", "origin", "HEAD:refs/pull/1/head");
        var client = CreateClient();

        var result = await client.CloneAsync(Request(), remote.Url, CancellationToken);
        _runnerLogger.Collector.Clear();
        var remoteRefs = await client.GetRemoteRefsAsync(Request(), CancellationToken);
        var output = Records(_runnerLogger, _outputLineEventId);
        var localRefs = await client.GetLocalRefsAsync(Request(), CancellationToken);

        remoteRefs.Refs.ShouldBe(localRefs);
        result.Refs.ShouldBe(localRefs);
        remoteRefs.Refs.Refs.Keys.ShouldBe(
            ["heads/develop", "heads/feature/nested", "heads/main", "tags/light", "tags/v1"], ignoreOrder: true);
        remoteRefs.DefaultBranch.ShouldBe("main");
        output.ShouldNotBeEmpty();
        output.Select(record => record.GetStructuredStateValue("Line") ?? string.Empty)
            .ShouldAllBe(line => !line.Contains("refs/pull/"));
        (await remote.GetRemoteRefsAsync(CancellationToken)).ShouldContainKey("refs/pull/1/head");
    }

    [Fact]
    public async Task GetRemoteRefsAsync_ServerHeadOnOtherBranch_ReturnsTheBranchTheCloneChecksOut()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CreateBranchAsync("develop", null, CancellationToken);
        await remote.SetDefaultBranchAsync("develop", CancellationToken);
        var client = CreateClient();
        var result = await client.CloneAsync(Request(branch: null), remote.Url, CancellationToken);

        var remoteRefs = await client.GetRemoteRefsAsync(Request(branch: null), CancellationToken);

        remoteRefs.DefaultBranch.ShouldBe("develop");
        result.Branch.ShouldBe(remoteRefs.DefaultBranch);
        remoteRefs.Refs.ShouldBe(result.Refs);
    }

    [Fact]
    public async Task Snapshots_AnnotatedTag_BothMapItToTheTagObject()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CreateTagAsync("v1", null, annotated: true, force: false, CancellationToken);
        var tagObject = (await _sandbox.RunGitAsync(remote.BarePath, CancellationToken, "rev-parse", "refs/tags/v1")).Single();
        var commit = (await _sandbox.RunGitAsync(remote.BarePath, CancellationToken, "rev-parse", "refs/tags/v1^{commit}")).Single();
        var client = CreateClient();

        await client.CloneAsync(Request(), remote.Url, CancellationToken);
        var remoteRefs = await client.GetRemoteRefsAsync(Request(), CancellationToken);
        var localRefs = await client.GetLocalRefsAsync(Request(), CancellationToken);

        tagObject.ShouldNotBe(commit);
        remoteRefs.Refs.Refs["tags/v1"].ShouldBe(tagObject);
        localRefs.Refs["tags/v1"].ShouldBe(tagObject);
    }

    [Fact]
    public async Task Snapshots_ServerRefsChanged_DifferBeforeUpdateAndMatchAfter()
    {
        var remote = await _sandbox.CreateRemoteAsync("repo", CancellationToken);
        await remote.CreateBranchAsync("feature", null, CancellationToken);
        await remote.CreateTagAsync("moved", null, annotated: false, force: false, CancellationToken);
        await remote.CreateTagAsync("gone", null, annotated: true, force: false, CancellationToken);
        var client = CreateClient();
        await client.CloneAsync(Request(), remote.Url, CancellationToken);
        await remote.CommitAsync("new.txt", "new", "New commit", CancellationToken);
        await remote.CreateTagAsync("moved", null, annotated: false, force: true, CancellationToken);
        await remote.DeleteTagAsync("gone", CancellationToken);
        await remote.DeleteBranchAsync("feature", CancellationToken);
        await remote.CreateBranchAsync("added", null, CancellationToken);

        var remoteBefore = await client.GetRemoteRefsAsync(Request(), CancellationToken);
        var localBefore = await client.GetLocalRefsAsync(Request(), CancellationToken);
        var result = await client.UpdateAsync(Request(), CancellationToken);
        var localAfter = await client.GetLocalRefsAsync(Request(), CancellationToken);

        remoteBefore.Refs.ShouldNotBe(localBefore);
        localAfter.ShouldBe(remoteBefore.Refs);
        result.Refs.ShouldBe(localAfter);
        localAfter.Refs.Keys.ShouldBe(["heads/added", "heads/main", "tags/moved"], ignoreOrder: true);
    }
}
