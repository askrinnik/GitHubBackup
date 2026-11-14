using GitHubBackup.Core.Clones;
using GitHubBackup.Core.Git;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GitHubBackup.Core.Tests.Clones;

/// <summary>
/// Tests <see cref="CloneGuard"/> against a substitute <see cref="IGitClient"/>.
/// </summary>
public sealed class CloneGuardTests
{
    private const string _clonePath = @"D:\Work\MyRepo";

    private static readonly RefSnapshot _remote = new([new("heads/main", "a"), new("heads/dev", "b")]);

    private readonly IGitClient _git = Substitute.For<IGitClient>();
    private readonly CloneGuard _guard;

    /// <summary>
    /// Sets up a server with the branches <c>main</c>, its default, and <c>dev</c>.
    /// </summary>
    public CloneGuardTests()
    {
        _guard = new(_git, NullLogger<CloneGuard>.Instance);
        _git.GetRemoteRefsAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<CancellationToken>()).Returns(_remote);
        _git.GetDefaultBranchAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<CancellationToken>()).Returns("main");
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<WorkingCopyStatus, string> SingleProblems => new()
    {
        { Status(uncommitted: true), "uncommitted changes" },
        { Status(untracked: true), "untracked files" },
        { Status(branch: "feature"), "branch 'feature' instead of 'main'" },
        { Status(branch: null), "detached HEAD instead of branch 'main'" },
        { Status(unpushed: 2), "2 unpushed commit(s)" },
    };

    [Fact]
    public async Task CheckAsync_CleanCloneOnBranch_Passes()
    {
        SetupStatus(Status());

        var result = await CheckAsync("main");

        result.Passed.ShouldBeTrue();
        result.Problems.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(SingleProblems))]
    public async Task CheckAsync_OneProblem_ReportsIt(WorkingCopyStatus status, string problem)
    {
        SetupStatus(status);

        var result = await CheckAsync("main");

        result.Problems.ShouldBe([problem]);
        result.Message.ShouldBe(
            $"The clone '{_clonePath}' has local changes: {problem}. Commit and push them or switch the branch, then run the backup again.");
    }

    [Fact]
    public async Task CheckAsync_EveryProblem_ReportsThemInFixedOrder()
    {
        SetupStatus(Status(branch: "feature", uncommitted: true, untracked: true, unpushed: 3));

        var result = await CheckAsync("main");

        result.Problems.ShouldBe(
            ["uncommitted changes", "untracked files", "branch 'feature' instead of 'main'", "3 unpushed commit(s)"]);
    }

    [Fact]
    public async Task CheckAsync_ConfiguredBranchMissingOnServer_ExpectsDefaultBranch()
    {
        SetupStatus(Status(branch: "main"));

        var result = await CheckAsync("gone");

        result.Passed.ShouldBeTrue();
    }

    [Fact]
    public async Task CheckAsync_BranchNotConfigured_ExpectsDefaultBranch()
    {
        SetupStatus(Status(branch: "dev"));

        var result = await CheckAsync(null);

        result.Problems.ShouldBe(["branch 'dev' instead of 'main'"]);
    }

    [Fact]
    public async Task CheckAsync_ConfiguredBranchOnServer_ExpectsItWithoutAskingForDefault()
    {
        SetupStatus(Status(branch: "dev"));

        var result = await CheckAsync("dev");

        result.Passed.ShouldBeTrue();
        await _git.DidNotReceiveWithAnyArgs().GetDefaultBranchAsync(default!, Token);
    }

    [Fact]
    public async Task CheckAsync_BranchOfLastArchive_IsAccepted()
    {
        SetupStatus(Status(branch: "main"));

        var result = await _guard.CheckAsync(Request("dev"), null, "main", Token);

        result.Passed.ShouldBeTrue();
    }

    [Fact]
    public async Task CheckAsync_AcceptedBranchAndLocalExpectedBranchAhead_ReportsItsCommits()
    {
        SetupStatus(Status(branch: "main"));
        _git.GetUnpushedBranchCommitsAsync(Arg.Any<GitRepositoryRequest>(), "dev", Arg.Any<RefSnapshot?>(), Token).Returns(2);

        var result = await _guard.CheckAsync(Request("dev"), null, "main", Token);

        result.Problems.ShouldBe(["2 unpushed commit(s) on branch 'dev'"]);
    }

    [Fact]
    public async Task CheckAsync_OnExpectedBranch_DoesNotCountOtherBranch()
    {
        SetupStatus(Status(branch: "main"));

        await CheckAsync("main");

        await _git.DidNotReceiveWithAnyArgs().GetUnpushedBranchCommitsAsync(default!, default!, default, Token);
    }

    [Fact]
    public async Task CheckAsync_Passed_ReportsExpectedBranch()
    {
        SetupStatus(Status(branch: "main"));

        var result = await CheckAsync(null);

        result.ExpectedBranch.ShouldBe("main");
    }

    [Fact]
    public async Task CheckLocalAsync_GivenBranch_ChecksWithoutReadingServer()
    {
        SetupStatus(Status(branch: "feature", untracked: true));

        var result = await _guard.CheckLocalAsync(Request("main"), "main", null, null, Token);

        result.Problems.ShouldBe(["untracked files", "branch 'feature' instead of 'main'"]);
        await _git.DidNotReceiveWithAnyArgs().GetRemoteRefsAsync(default!, Token);
        await _git.DidNotReceiveWithAnyArgs().GetDefaultBranchAsync(default!, Token);
    }

    [Fact]
    public async Task CheckAsync_DetachedHeadWithAcceptedBranch_IsStillReported()
    {
        SetupStatus(Status(branch: null));

        var result = await _guard.CheckAsync(Request("main"), null, "main", Token);

        result.Problems.ShouldBe(["detached HEAD instead of branch 'main'"]);
    }

    [Fact]
    public async Task CheckAsync_RemoteRefsGiven_DoesNotReadThemAgain()
    {
        SetupStatus(Status());

        await _guard.CheckAsync(Request("main"), _remote, null, Token);

        await _git.DidNotReceiveWithAnyArgs().GetRemoteRefsAsync(default!, Token);
    }

    [Fact]
    public async Task CheckAsync_EmptyServer_DoesNotCheckBranch()
    {
        _git.GetRemoteRefsAsync(Arg.Any<GitRepositoryRequest>(), Token).Returns(RefSnapshot.Empty);
        SetupStatus(Status(branch: "feature"));

        var result = await CheckAsync("main");

        result.Passed.ShouldBeTrue();
    }

    [Fact]
    public async Task CheckAsync_CloneWithoutCommit_DoesNotCheckBranchOrReadServer()
    {
        SetupStatus(new WorkingCopyStatus("master", false, false, false, 0));

        var result = await CheckAsync("main");

        result.Passed.ShouldBeTrue();
        await _git.DidNotReceiveWithAnyArgs().GetRemoteRefsAsync(default!, Token);
    }

    [Fact]
    public async Task CheckAsync_WikiCloneOnDefaultBranch_Passes()
    {
        SetupStatus(Status(branch: "main"));

        var result = await _guard.CheckAsync(Request(null) with { ClonePath = _clonePath + ".wiki" }, null, null, Token);

        result.CloneFolder.ShouldBe(_clonePath + ".wiki");
        result.Passed.ShouldBeTrue();
    }

    /// <summary>
    /// Creates a status with commits and the given problems.
    /// </summary>
    /// <param name="branch">The checked-out branch, or <see langword="null"/> for a detached <c>HEAD</c>.</param>
    /// <param name="uncommitted">Whether there are uncommitted changes.</param>
    /// <param name="untracked">Whether there are untracked files.</param>
    /// <param name="unpushed">The number of unpushed commits.</param>
    /// <returns>The status.</returns>
    private static WorkingCopyStatus Status(
        string? branch = "main", bool uncommitted = false, bool untracked = false, int unpushed = 0) =>
        new(branch, true, uncommitted, untracked, unpushed);

    /// <summary>
    /// Creates the request for the clone with the given configured branch.
    /// </summary>
    /// <param name="branch">The configured branch, or <see langword="null"/>.</param>
    /// <returns>The request.</returns>
    private static GitRepositoryRequest Request(string? branch) =>
        new() { GitPath = @"C:\git\git.exe", ClonePath = _clonePath, Branch = branch, Token = "secret-token" };

    /// <summary>
    /// Makes the git client report <paramref name="status"/> for the clone.
    /// </summary>
    /// <param name="status">The status.</param>
    private void SetupStatus(WorkingCopyStatus status) =>
        _git.GetWorkingCopyStatusAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<RefSnapshot?>(), Arg.Any<CancellationToken>()).Returns(status);

    /// <summary>
    /// Checks the clone with the configured branch, without remote refs or an accepted branch.
    /// </summary>
    /// <param name="branch">The configured branch, or <see langword="null"/>.</param>
    /// <returns>The result.</returns>
    private Task<CloneCheckResult> CheckAsync(string? branch) => _guard.CheckAsync(Request(branch), null, null, Token);
}
