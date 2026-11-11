using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
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

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RelocateAsync_NoClone_ReturnsNotPresent()
    {
        var result = await Relocator().RelocateAsync(_gitPath, _oldFolder, _newFolder, _url, Token);

        result.ShouldBe(CloneRelocationResult.NotPresent);
        await _git.DidNotReceiveWithAnyArgs().SetRemoteUrlAsync(default!, default!, Token);
    }

    [Fact]
    public async Task RelocateAsync_CloneExists_MovesItAndSetsRemoteUrl()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));

        var result = await Relocator().RelocateAsync(_gitPath, _oldFolder, _newFolder, _url, Token);

        result.ShouldBe(CloneRelocationResult.Moved);
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

        var result = await Relocator().RelocateAsync(_gitPath, _oldFolder, newFolder, _url, Token);

        result.ShouldBe(CloneRelocationResult.Moved);
        _fileSystem.File.Exists(@"C:\sources\acme\new\.git\HEAD").ShouldBeTrue();
    }

    [Fact]
    public async Task RelocateAsync_CaseOnlyRename_KeepsFolderAndSetsRemoteUrl()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));
        const string newFolder = @"C:\sources\octo\OLD";

        var result = await Relocator().RelocateAsync(_gitPath, _oldFolder, newFolder, _url, Token);

        result.ShouldBe(CloneRelocationResult.Moved);
        _fileSystem.File.Exists(@"C:\sources\octo\old\.git\HEAD").ShouldBeTrue();
        await _git.Received(1).SetRemoteUrlAsync(Arg.Any<GitRepositoryRequest>(), _url, Token);
    }

    [Fact]
    public async Task RelocateAsync_TargetExists_DeletesOldCloneIncludingReadOnlyFilesAndKeepsTarget()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));
        _fileSystem.AddFile(
            @"C:\sources\octo\old\.git\objects\ab\cdef", new("object") { Attributes = FileAttributes.ReadOnly });
        _fileSystem.AddFile(@"C:\sources\octo\new\stale.txt", new("stale"));

        var result = await Relocator().RelocateAsync(_gitPath, _oldFolder, _newFolder, _url, Token);

        result.ShouldBe(CloneRelocationResult.DeletedForReclone);
        _fileSystem.Directory.Exists(_oldFolder).ShouldBeFalse();
        _fileSystem.File.Exists(@"C:\sources\octo\new\stale.txt").ShouldBeTrue();
        await _git.DidNotReceiveWithAnyArgs().SetRemoteUrlAsync(default!, default!, Token);
    }

    [Fact]
    public async Task RelocateAsync_MoveThrows_DeletesOldClone()
    {
        var fileSystem = FailingFileSystem(moveFails: true, deleteFails: false);

        var result = await new CloneRelocator(fileSystem, _git, NullLogger<CloneRelocator>.Instance)
            .RelocateAsync(_gitPath, _oldFolder, _newFolder, _url, Token);

        result.ShouldBe(CloneRelocationResult.DeletedForReclone);
        fileSystem.Directory.Received(1).Delete(_oldFolder, true);
        await _git.DidNotReceiveWithAnyArgs().SetRemoteUrlAsync(default!, default!, Token);
    }

    [Fact]
    public async Task RelocateAsync_MoveAndDeleteThrow_ReturnsDeleteFailed()
    {
        var fileSystem = FailingFileSystem(moveFails: true, deleteFails: true);

        var result = await new CloneRelocator(fileSystem, _git, NullLogger<CloneRelocator>.Instance)
            .RelocateAsync(_gitPath, _oldFolder, _newFolder, _url, Token);

        result.ShouldBe(CloneRelocationResult.DeleteFailed);
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

        var result = await Relocator().RelocateAsync(_gitPath, _oldFolder, _newFolder, _url, Token);

        result.ShouldBe(CloneRelocationResult.DeletedForReclone);
        _fileSystem.Directory.Exists(_oldFolder).ShouldBeFalse();
        _fileSystem.Directory.Exists(_newFolder).ShouldBeFalse();
    }

    [Fact]
    public async Task RelocateAsync_CloneIsLink_LeavesItAlone()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));
        _fileSystem.DirectoryInfo.New(_oldFolder).Attributes = FileAttributes.Directory | FileAttributes.ReparsePoint;

        var result = await Relocator().RelocateAsync(_gitPath, _oldFolder, _newFolder, _url, Token);

        result.ShouldBe(CloneRelocationResult.DeleteFailed);
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
            () => Relocator().RelocateAsync(_gitPath, _oldFolder, _newFolder, _url, source.Token));

        _fileSystem.File.Exists(@"C:\sources\octo\old\.git\HEAD").ShouldBeTrue();
    }

    [Fact]
    public async Task RelocateAsync_SetRemoteUrlCancelled_PropagatesAndKeepsMovedClone()
    {
        _fileSystem.AddFile(@"C:\sources\octo\old\.git\HEAD", new("ref"));
        _git.SetRemoteUrlAsync(default!, default!, Token).ThrowsAsyncForAnyArgs(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(
            () => Relocator().RelocateAsync(_gitPath, _oldFolder, _newFolder, _url, Token));

        _fileSystem.File.Exists(@"C:\sources\octo\new\.git\HEAD").ShouldBeTrue();
    }

    /// <summary>
    /// Creates the relocator over the mock file system.
    /// </summary>
    /// <returns>The relocator under test.</returns>
    private CloneRelocator Relocator() => new(_fileSystem, _git, NullLogger<CloneRelocator>.Instance);

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
