using GitHubBackup.Core.Git;

namespace GitHubBackup.Infrastructure.IntegrationTests.Git;

/// <summary>
/// Tests <see cref="GitHubBackup.Infrastructure.Git.GitClient"/> on the ref snapshots of the server and of the clone.
/// </summary>
public sealed class GitClientRefSnapshotTests : GitClientTestBase
{
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
        var remoteRefs = await client.GetRemoteRefsAsync(Request(), CancellationToken);
        var localRefs = await client.GetLocalRefsAsync(Request(), CancellationToken);

        remoteRefs.ShouldBe(localRefs);
        result.Refs.ShouldBe(localRefs);
        remoteRefs.Refs.Keys.ShouldBe(
            ["heads/develop", "heads/feature/nested", "heads/main", "tags/light", "tags/v1"], ignoreOrder: true);
        (await remote.GetRemoteRefsAsync(CancellationToken)).ShouldContainKey("refs/pull/1/head");
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
        remoteRefs.Refs["tags/v1"].ShouldBe(tagObject);
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

        remoteBefore.ShouldNotBe(localBefore);
        localAfter.ShouldBe(remoteBefore);
        result.Refs.ShouldBe(localAfter);
        localAfter.Refs.Keys.ShouldBe(["heads/added", "heads/main", "tags/moved"], ignoreOrder: true);
    }
}
