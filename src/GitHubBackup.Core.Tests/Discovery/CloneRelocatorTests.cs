using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Clones;
using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.Processes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GitHubBackup.Core.Tests.Discovery;

/// <summary>
/// Tests <see cref="CloneRelocator"/>.
/// </summary>
public sealed class CloneRelocatorTests
{
    private const string _gitPath = @"C:\git\git.exe";
    private const string _oldFolder = @"C:\sources\octo\old";
    private const string _newFolder = @"C:\sources\octo\new";
    private const string _url = "https://github.com/octo/new.git";

    private readonly MockFileSystem _fileSystem = new();
    private readonly IGitClient _git = Substitute.For<IGitClient>();
    private readonly ICloneGuard _guard = Substitute.For<ICloneGuard>();

    /// <summary>
    /// Sets up a guard that finds no local work.
    /// </summary>
    public CloneRelocatorTests()
    {
        _guard.CheckAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<RefSnapshot?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new CloneCheckResult(_oldFolder, []));
        _git.GetOriginUrlAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<CancellationToken>()).Returns(_url);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RelocateAsync_NoClone_ReturnsNotPresent()
    {
        var result = await Relocator().RelocateAsync(Request(), Token);

        result.Result.ShouldBe(CloneRelocationResult.NotPresent);
        await _git.DidNotReceiveWithAnyArgs().SetRemoteUrlAsync(default!, default!, Token);
    }

    [Fact]
    public async Task RelocateAsync_CloneExists_MovesItAndSetsRemoteUrl()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));

        var result = await Relocator().RelocateAsync(Request(), Token);

        result.Result.ShouldBe(CloneRelocationResult.Moved);
        _fileSystem.Directory.Exists(_oldFolder).ShouldBeFalse();
        _fileSystem.File.Exists(@"C:\sources\octo\new\.git\HEAD").ShouldBeTrue();
        await _git.Received(1).SetRemoteUrlAsync(
            Arg.Is<GitRepositoryRequest>(request => request.ClonePath == _newFolder && request.GitPath == _gitPath && request.Token == null),
            _url,
            Token);
    }

    [Fact]
    public async Task RelocateAsync_OwnerFolderMissing_CreatesItAndMoves()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));
        const string newFolder = @"C:\sources\acme\new";

        var result = await Relocator().RelocateAsync(Request(newFolder), Token);

        result.Result.ShouldBe(CloneRelocationResult.Moved);
        _fileSystem.File.Exists(@"C:\sources\acme\new\.git\HEAD").ShouldBeTrue();
    }

    [Fact]
    public async Task RelocateAsync_CaseOnlyRename_KeepsFolderAndSetsRemoteUrl()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));
        const string newFolder = @"C:\sources\octo\OLD";

        var result = await Relocator().RelocateAsync(Request(newFolder), Token);

        result.Result.ShouldBe(CloneRelocationResult.Moved);
        _fileSystem.File.Exists(@"C:\sources\octo\old\.git\HEAD").ShouldBeTrue();
        await _git.Received(1).SetRemoteUrlAsync(Arg.Any<GitRepositoryRequest>(), _url, Token);
    }

    [Fact]
    public async Task RelocateAsync_TargetExists_ReturnsTargetOccupiedAndKeepsBothFolders()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));
        _fileSystem.AddFile(@"C:\sources\octo\new\stale.txt", new("stale"));

        var result = await Relocator().RelocateAsync(Request(), Token);

        result.Result.ShouldBe(CloneRelocationResult.TargetOccupied);
        _fileSystem.File.Exists(@"C:\sources\octo\old\.git\HEAD").ShouldBeTrue();
        _fileSystem.File.Exists(@"C:\sources\octo\new\stale.txt").ShouldBeTrue();
        await _git.DidNotReceiveWithAnyArgs().SetRemoteUrlAsync(default!, default!, Token);
    }

    [Fact]
    public async Task RelocateAsync_TargetIsFile_ReturnsTargetOccupiedAndKeepsBoth()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));
        _fileSystem.AddFile(_newFolder, new("a file"));

        var result = await Relocator().RelocateAsync(Request(), Token);

        result.Result.ShouldBe(CloneRelocationResult.TargetOccupied);
        _fileSystem.File.Exists(@"C:\sources\octo\old\.git\HEAD").ShouldBeTrue();
        _fileSystem.File.Exists(_newFolder).ShouldBeTrue();
    }

    [Fact]
    public async Task RelocateAsync_MoveThrows_DeletesReadOnlyFilesToo()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));
        _fileSystem.AddFile(
            @"C:\sources\octo\old\.git\objects\ab\cdef", new("object") { Attributes = FileAttributes.ReadOnly });
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.Path.Returns(_fileSystem.Path);
        fileSystem.File.Returns(_fileSystem.File);
        fileSystem.DirectoryInfo.Returns(_fileSystem.DirectoryInfo);
        fileSystem.Directory.Returns(new MoveFailingDirectory(_fileSystem));

        var result = await new CloneRelocator(fileSystem, _git, _guard, NullLogger<CloneRelocator>.Instance)
            .RelocateAsync(Request(), Token);

        result.Result.ShouldBe(CloneRelocationResult.DeletedForReclone);
        _fileSystem.Directory.Exists(_oldFolder).ShouldBeFalse();
    }

    [Fact]
    public async Task RelocateAsync_MoveThrows_DeletesOldClone()
    {
        var fileSystem = FailingFileSystem(moveFails: true, deleteFails: false);

        var result = await new CloneRelocator(fileSystem, _git, _guard, NullLogger<CloneRelocator>.Instance)
            .RelocateAsync(Request(), Token);

        result.Result.ShouldBe(CloneRelocationResult.DeletedForReclone);
        fileSystem.Directory.Received(1).Delete(_oldFolder, true);
        await _git.DidNotReceiveWithAnyArgs().SetRemoteUrlAsync(default!, default!, Token);
    }

    [Fact]
    public async Task RelocateAsync_MoveAndDeleteThrow_ReturnsDeleteFailed()
    {
        var fileSystem = FailingFileSystem(moveFails: true, deleteFails: true);

        var result = await new CloneRelocator(fileSystem, _git, _guard, NullLogger<CloneRelocator>.Instance)
            .RelocateAsync(Request(), Token);

        result.Result.ShouldBe(CloneRelocationResult.DeleteFailed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RelocateAsync_SetRemoteUrlFails_DeletesMovedClone(bool gitFails)
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));
        Exception failure = gitFails
            ? new GitCommandException("failed", "git remote set-url", 2)
            : new ProcessStartException("git missing");
        _git.SetRemoteUrlAsync(default!, default!, Token).ThrowsAsyncForAnyArgs(failure);

        var result = await Relocator().RelocateAsync(Request(), Token);

        result.Result.ShouldBe(CloneRelocationResult.DeletedForReclone);
        _fileSystem.Directory.Exists(_oldFolder).ShouldBeFalse();
        _fileSystem.Directory.Exists(_newFolder).ShouldBeFalse();
    }

    [Fact]
    public async Task RelocateAsync_CloneIsLink_LeavesItAlone()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));
        _fileSystem.DirectoryInfo.New(_oldFolder).Attributes = FileAttributes.Directory | FileAttributes.ReparsePoint;

        var result = await Relocator().RelocateAsync(Request(), Token);

        result.Result.ShouldBe(CloneRelocationResult.DeleteFailed);
        _fileSystem.File.Exists(@"C:\sources\octo\old\.git\HEAD").ShouldBeTrue();
        _fileSystem.Directory.Exists(_newFolder).ShouldBeFalse();
    }

    [Fact]
    public async Task RelocateAsync_Cancelled_ThrowsAndLeavesCloneInPlace()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => Relocator().RelocateAsync(Request(), source.Token));

        _fileSystem.File.Exists(@"C:\sources\octo\old\.git\HEAD").ShouldBeTrue();
    }

    [Fact]
    public async Task RelocateAsync_SetRemoteUrlCancelled_PropagatesAndKeepsMovedClone()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));
        _git.SetRemoteUrlAsync(default!, default!, Token).ThrowsAsyncForAnyArgs(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(
            () => Relocator().RelocateAsync(Request(), Token));

        _fileSystem.File.Exists(@"C:\sources\octo\new\.git\HEAD").ShouldBeTrue();
    }

    [Fact]
    public async Task RelocateAsync_LocalChanges_LeavesCloneInOldFolder()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));
        var check = new CloneCheckResult(_oldFolder, [CloneCheckResult.UncommittedChanges]);
        _guard.CheckAsync(
                Arg.Is<GitRepositoryRequest>(git => git.ClonePath == _oldFolder && git.Branch == "main" && git.Token == "secret-token"),
                null,
                null,
                Token)
            .Returns(check);

        var result = await Relocator().RelocateAsync(Request(), Token);

        result.ShouldBe(new CloneRelocation(CloneRelocationResult.LocalChanges, check.Message));
        _fileSystem.File.Exists(@"C:\sources\octo\old\.git\HEAD").ShouldBeTrue();
        _fileSystem.Directory.Exists(_newFolder).ShouldBeFalse();
        await _git.DidNotReceiveWithAnyArgs().SetRemoteUrlAsync(default!, default!, Token);
    }

    [Fact]
    public async Task RelocateAsync_CheckFails_ReturnsCheckFailedAndKeepsClone()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));
        _guard.CheckAsync(default!, default, default, Token)
            .ThrowsAsyncForAnyArgs(new GitCommandException("status failed", "git status", 128));

        var result = await Relocator().RelocateAsync(Request(), Token);

        result.Result.ShouldBe(CloneRelocationResult.CheckFailed);
        result.Reason.ShouldNotBeNull().ShouldContain(_oldFolder);
        _fileSystem.File.Exists(@"C:\sources\octo\old\.git\HEAD").ShouldBeTrue();
    }

    [Fact]
    public async Task RelocateAsync_OldFolderWithoutGit_ReturnsNotPresentAndLeavesIt()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\notes.txt", new("notes"));

        var result = await Relocator().RelocateAsync(Request(), Token);

        result.ShouldBe(CloneRelocation.NotPresent);
        _fileSystem.File.Exists(@"C:\sources\octo\old\notes.txt").ShouldBeTrue();
        await _guard.DidNotReceiveWithAnyArgs().CheckAsync(default!, default, default, Token);
    }

    [Fact]
    public async Task RelocateAsync_MoveThrowsWithoutDeleteOnFailure_ReturnsMoveFailedAndDeletesNothing()
    {
        var fileSystem = FailingFileSystem(moveFails: true, deleteFails: false);

        var result = await new CloneRelocator(fileSystem, _git, _guard, NullLogger<CloneRelocator>.Instance)
            .RelocateAsync(Request(deleteOnFailure: false), Token);

        result.Result.ShouldBe(CloneRelocationResult.MoveFailed);
        result.Reason.ShouldNotBeNull().ShouldContain(_newFolder);
        fileSystem.Directory.DidNotReceiveWithAnyArgs().Delete(default!, default);
    }

    [Fact]
    public async Task RelocateAsync_SetRemoteUrlFailsWithoutDeleteOnFailure_KeepsMovedClone()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));
        _git.SetRemoteUrlAsync(default!, default!, Token)
            .ThrowsAsyncForAnyArgs(new GitCommandException("failed", "git remote set-url", 2));

        var result = await Relocator().RelocateAsync(Request(deleteOnFailure: false), Token);

        result.Result.ShouldBe(CloneRelocationResult.RemoteUrlNotSet);
        _fileSystem.File.Exists(@"C:\sources\octo\new\.git\HEAD").ShouldBeTrue();
    }

    [Fact]
    public async Task RelocateAsync_SameFolderAndSetRemoteUrlFailsWithoutDeleteOnFailure_KeepsClone()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));
        _git.SetRemoteUrlAsync(default!, default!, Token)
            .ThrowsAsyncForAnyArgs(new GitCommandException("failed", "git remote set-url", 2));

        var result = await Relocator().RelocateAsync(Request(_oldFolder, deleteOnFailure: false), Token);

        result.Result.ShouldBe(CloneRelocationResult.RemoteUrlNotSet);
        _fileSystem.File.Exists(@"C:\sources\octo\old\.git\HEAD").ShouldBeTrue();
        await _guard.DidNotReceiveWithAnyArgs().CheckAsync(default!, default, default, Token);
    }

    [Fact]
    public async Task PreviewAsync_CloneCanMove_ReturnsMovedWithoutChangingAnything()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));

        var result = await Relocator().PreviewAsync(Request(), Token);

        result.ShouldBe(CloneRelocation.Moved);
        _fileSystem.File.Exists(@"C:\sources\octo\old\.git\HEAD").ShouldBeTrue();
        _fileSystem.Directory.Exists(_newFolder).ShouldBeFalse();
        await _git.DidNotReceiveWithAnyArgs().SetRemoteUrlAsync(default!, default!, Token);
    }

    [Fact]
    public async Task PreviewAsync_TargetOccupied_ReturnsTargetOccupied()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));
        _fileSystem.AddFile(@"C:\sources\octo\new\stale.txt", new("stale"));

        var result = await Relocator().PreviewAsync(Request(), Token);

        result.Result.ShouldBe(CloneRelocationResult.TargetOccupied);
    }

    [Fact]
    public async Task PreviewAsync_LocalChanges_ReturnsLocalChanges()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));
        _guard.CheckAsync(default!, default, default, Token)
            .ReturnsForAnyArgs(new CloneCheckResult(_oldFolder, [CloneCheckResult.UntrackedFiles]));

        var result = await Relocator().PreviewAsync(Request(), Token);

        result.Result.ShouldBe(CloneRelocationResult.LocalChanges);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RelocateAsync_OriginOfOtherRepository_NeitherMovesNorChangesUrl(bool sameFolder)
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));
        _git.GetOriginUrlAsync(Arg.Any<GitRepositoryRequest>(), Token).Returns("https://github.com/other/repo.git");

        var result = await Relocator().RelocateAsync(Request(sameFolder ? _oldFolder : null), Token);

        result.Result.ShouldBe(CloneRelocationResult.ForeignOrigin);
        result.Reason.ShouldNotBeNull().ShouldContain(_oldFolder);
        result.Reason.ShouldNotContain("other/repo");
        _fileSystem.File.Exists(@"C:\sources\octo\old\.git\HEAD").ShouldBeTrue();
        await _git.DidNotReceiveWithAnyArgs().SetRemoteUrlAsync(default!, default!, Token);
    }

    [Fact]
    public async Task RelocateAsync_OriginIsPreviousUrl_Moves()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));
        _git.GetOriginUrlAsync(Arg.Any<GitRepositoryRequest>(), Token).Returns("https://github.com/octo/old");

        var result = await Relocator().RelocateAsync(
            Request() with { PreviousRemoteUrl = "https://github.com/octo/old.git" }, Token);

        result.ShouldBe(CloneRelocation.Moved);
    }

    [Fact]
    public async Task RelocateAsync_OriginUnreadable_ReturnsCheckFailedAndKeepsClone()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));
        _git.GetOriginUrlAsync(Arg.Any<GitRepositoryRequest>(), Token)
            .ThrowsAsync(new GitCommandException("config failed", "git config", 128));

        var result = await Relocator().RelocateAsync(Request(), Token);

        result.Result.ShouldBe(CloneRelocationResult.CheckFailed);
        _fileSystem.Directory.Exists(_newFolder).ShouldBeFalse();
    }

    [Fact]
    public async Task RelocateAsync_SameFolderWithoutGit_DoesNotSetUrl()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\notes.txt", new("notes"));

        var result = await Relocator().RelocateAsync(Request(_oldFolder, deleteOnFailure: false), Token);

        result.ShouldBe(CloneRelocation.NotPresent);
        await _git.DidNotReceiveWithAnyArgs().SetRemoteUrlAsync(default!, default!, Token);
    }

    [Fact]
    public void CloneRelocationRequest_ToString_LeavesTokenOut() =>
        Request().ToString().ShouldNotContain("secret-token");

    /// <summary>
    /// A mock directory API whose <see cref="Move"/> always fails and whose other members act on the mock file system.
    /// </summary>
    /// <param name="fileSystem">The mock file system.</param>
    private sealed class MoveFailingDirectory(MockFileSystem fileSystem)
        : MockDirectory(fileSystem, (FileBase)fileSystem.File, @"C:\")
    {
        /// <inheritdoc />
        public override void Move(string sourceDirName, string destDirName) =>
            throw new IOException("The folder is in use.");
    }

    /// <summary>
    /// Creates the relocator over the mock file system.
    /// </summary>
    /// <returns>The relocator under test.</returns>
    private CloneRelocator Relocator() => new(_fileSystem, _git, _guard, NullLogger<CloneRelocator>.Instance);

    /// <summary>
    /// Creates the request that moves the old clone, deleting it on failure as a clone under the clone root.
    /// </summary>
    /// <param name="newFolder">The target folder, or <see langword="null"/> for the default one.</param>
    /// <param name="deleteOnFailure">Whether a clone that cannot be moved or repointed is deleted.</param>
    /// <returns>The request.</returns>
    private static CloneRelocationRequest Request(string? newFolder = null, bool deleteOnFailure = true) => new()
    {
        GitPath = _gitPath,
        OldFolder = _oldFolder,
        NewFolder = newFolder ?? _newFolder,
        RemoteUrl = _url,
        Branch = "main",
        Token = "secret-token",
        DeleteOnFailure = deleteOnFailure,
    };

    /// <summary>
    /// Creates a file system in which the old clone exists, the new folder does not, and moving or deleting fails.
    /// </summary>
    /// <param name="moveFails">Whether moving a folder throws <see cref="IOException"/>.</param>
    /// <param name="deleteFails">Whether deleting a folder throws <see cref="IOException"/>.</param>
    /// <returns>The file system.</returns>
    private static IFileSystem FailingFileSystem(bool moveFails, bool deleteFails)
    {
        var mock = new MockFileSystem();
        mock.AddDirectory(_oldFolder);
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.Path.Returns(mock.Path);
        fileSystem.DirectoryInfo.Returns(mock.DirectoryInfo);
        fileSystem.File.Returns(mock.File);
        fileSystem.Directory.Exists(_oldFolder).Returns(true);
        fileSystem.Directory.Exists(Path.Combine(_oldFolder, ".git")).Returns(true);
        fileSystem.Directory.EnumerateFiles(_oldFolder, "*", SearchOption.AllDirectories).Returns([]);
        if (moveFails)
        {
            fileSystem.Directory.When(directory => directory.Move(_oldFolder, _newFolder))
                .Do(_ => throw new IOException("The folder is in use."));
        }

        if (deleteFails)
        {
            fileSystem.Directory.When(directory => directory.Delete(_oldFolder, true))
                .Do(_ => throw new UnauthorizedAccessException("Access denied."));
        }

        return fileSystem;
    }
}
