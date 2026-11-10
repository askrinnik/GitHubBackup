using GitHubBackup.TestUtilities.Git;

namespace GitHubBackup.Infrastructure.IntegrationTests.TestInfrastructure;

/// <summary>
/// Tests the helpers of <see cref="LocalGitRemote"/>; each result is verified through git commands that bypass the helpers.
/// </summary>
public sealed class LocalGitRemoteTests : IDisposable
{
    private readonly LocalGitSandbox _sandbox = new();

    public void Dispose() => _sandbox.Dispose();

    [Fact]
    public async Task CommitAsync_TwoCommits_RemoteBranchPointsToSecondWithFirstAsParent()
    {
        var ct = TestContext.Current.CancellationToken;
        var remote = await _sandbox.CreateRemoteAsync("repo", ct);
        var first = await remote.CommitAsync("a.txt", "1", "First", ct);

        var second = await remote.CommitAsync("a.txt", "2", "Second", ct);

        (await ResolveAsync(remote, "refs/heads/main", ct)).ShouldBe(second);
        (await ResolveAsync(remote, second + "^", ct)).ShouldBe(first);
    }

    [Fact]
    public async Task CreateBranchAsync_FromStartPoint_PublishesBranchAtThatCommit()
    {
        var ct = TestContext.Current.CancellationToken;
        var remote = await _sandbox.CreateRemoteAsync("repo", ct);
        var first = await remote.CommitAsync("a.txt", "1", "First", ct);
        await remote.CommitAsync("a.txt", "2", "Second", ct);

        await remote.CreateBranchAsync("feature", first, ct);

        (await ResolveAsync(remote, "refs/heads/feature", ct)).ShouldBe(first);
    }

    [Fact]
    public async Task DeleteBranchAsync_ExistingBranch_RemovesRefFromRemote()
    {
        var ct = TestContext.Current.CancellationToken;
        var remote = await _sandbox.CreateRemoteAsync("repo", ct);
        await remote.CreateBranchAsync("feature", null, ct);

        await remote.DeleteBranchAsync("feature", ct);

        (await remote.GetRemoteRefsAsync(ct)).Keys.ShouldNotContain("refs/heads/feature");
    }

    [Fact]
    public async Task DeleteBranchAsync_MissingBranch_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var remote = await _sandbox.CreateRemoteAsync("repo", ct);

        await Should.ThrowAsync<InvalidOperationException>(() => remote.DeleteBranchAsync("missing", ct));
    }

    [Fact]
    public async Task CreateTagAsync_Lightweight_PublishesTagPointingToCommit()
    {
        var ct = TestContext.Current.CancellationToken;
        var remote = await _sandbox.CreateRemoteAsync("repo", ct);
        var commit = await remote.CommitAsync("a.txt", "1", "First", ct);

        await remote.CreateTagAsync("v1", null, annotated: false, force: false, ct);

        var refs = await remote.GetRemoteRefsAsync(ct);
        refs["refs/tags/v1"].ShouldBe(commit);
        (await ObjectTypeAsync(remote, "refs/tags/v1", ct)).ShouldBe("commit");
    }

    [Fact]
    public async Task CreateTagAsync_Annotated_PublishesTagObject()
    {
        var ct = TestContext.Current.CancellationToken;
        var remote = await _sandbox.CreateRemoteAsync("repo", ct);
        var commit = await remote.CommitAsync("a.txt", "1", "First", ct);

        await remote.CreateTagAsync("v1", null, annotated: true, force: false, ct);

        var refs = await remote.GetRemoteRefsAsync(ct);
        refs["refs/tags/v1"].ShouldNotBe(commit);
        (await ObjectTypeAsync(remote, "refs/tags/v1", ct)).ShouldBe("tag");
        (await ResolveAsync(remote, "refs/tags/v1^{commit}", ct)).ShouldBe(commit);
    }

    [Fact]
    public async Task CreateTagAsync_ForceExistingTag_MovesTag()
    {
        var ct = TestContext.Current.CancellationToken;
        var remote = await _sandbox.CreateRemoteAsync("repo", ct);
        await remote.CreateTagAsync("v1", null, annotated: false, force: false, ct);
        var second = await remote.CommitAsync("a.txt", "2", "Second", ct);

        await remote.CreateTagAsync("v1", second, annotated: false, force: true, ct);

        (await ResolveAsync(remote, "refs/tags/v1", ct)).ShouldBe(second);
    }

    [Fact]
    public async Task DeleteTagAsync_ExistingTag_RemovesRefFromRemote()
    {
        var ct = TestContext.Current.CancellationToken;
        var remote = await _sandbox.CreateRemoteAsync("repo", ct);
        await remote.CreateTagAsync("v1", null, annotated: false, force: false, ct);

        await remote.DeleteTagAsync("v1", ct);

        (await remote.GetRemoteRefsAsync(ct)).Keys.ShouldNotContain("refs/tags/v1");
    }

    [Fact]
    public async Task ForcePushAsync_ToEarlierCommitThenCommit_RewritesHistory()
    {
        var ct = TestContext.Current.CancellationToken;
        var remote = await _sandbox.CreateRemoteAsync("repo", ct);
        var first = await remote.CommitAsync("a.txt", "1", "First", ct);
        var second = await remote.CommitAsync("a.txt", "2", "Second", ct);

        await remote.ForcePushAsync("main", first, ct);
        var third = await remote.CommitAsync("a.txt", "3", "Third", ct);

        (await ResolveAsync(remote, "refs/heads/main", ct)).ShouldBe(third);
        (await ResolveAsync(remote, third + "^", ct)).ShouldBe(first);
        var ancestors = await _sandbox.RunGitAsync(remote.BarePath, ct, "rev-list", "refs/heads/main");
        ancestors.ShouldNotContain(second);
    }

    [Fact]
    public async Task SetDefaultBranchAsync_OtherBranch_ChangesRemoteHead()
    {
        var ct = TestContext.Current.CancellationToken;
        var remote = await _sandbox.CreateRemoteAsync("repo", ct);
        await remote.CreateBranchAsync("develop", null, ct);

        await remote.SetDefaultBranchAsync("develop", ct);

        var head = await _sandbox.RunGitAsync(remote.BarePath, ct, "symbolic-ref", "HEAD");
        head.ShouldBe(["refs/heads/develop"]);
    }

    [Fact]
    public async Task AddSubmoduleAsync_OtherRemote_CloneWithSubmodulesContainsItsFiles()
    {
        var ct = TestContext.Current.CancellationToken;
        var inner = await _sandbox.CreateRemoteAsync("inner", ct);
        await inner.CommitAsync("inner.txt", "inner content", "Add inner file", ct);
        var outer = await _sandbox.CreateRemoteAsync("outer", ct);
        await outer.AddSubmoduleAsync(inner, "libs/inner", ct);
        var target = Path.Combine(_sandbox.CreateFolder("clones"), "outer");

        await _sandbox.RunGitAsync(_sandbox.RootPath, ct, "clone", "--recurse-submodules", outer.Url, target);

        var file = Path.Combine(target, "libs", "inner", "inner.txt");
        (await File.ReadAllTextAsync(file, ct)).ShouldBe("inner content");
    }

    private Task<IReadOnlyList<string>> RunInBareAsync(LocalGitRemote remote, CancellationToken ct, params string[] args) =>
        _sandbox.RunGitAsync(remote.BarePath, ct, args);

    private async Task<string> ResolveAsync(LocalGitRemote remote, string revision, CancellationToken ct) =>
        (await RunInBareAsync(remote, ct, "rev-parse", revision)).Single();

    private async Task<string> ObjectTypeAsync(LocalGitRemote remote, string revision, CancellationToken ct) =>
        (await RunInBareAsync(remote, ct, "cat-file", "-t", revision)).Single();
}
