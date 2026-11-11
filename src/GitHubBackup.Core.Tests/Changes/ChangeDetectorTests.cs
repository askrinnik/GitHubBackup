using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Changes;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.State;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GitHubBackup.Core.Tests.Changes;

/// <summary>
/// Tests <see cref="ChangeDetector"/> against a substitute <see cref="IGitClient"/> and an in-memory file system.
/// </summary>
public sealed class ChangeDetectorTests
{
    private const string _archiveFolder = @"C:\backup\archives\owner\repo";
    private const string _clonePath = @"C:\backup\clones\owner\repo";
    private const string _remoteUrl = "https://github.com/owner/repo";
    private const string _archiveFile = "repo.7z";

    /// <summary>The Windows attribute <c>FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS</c> that OneDrive sets on a file stored only in the cloud.</summary>
    private const FileAttributes _recallOnDataAccess = (FileAttributes)0x400000;

    private static readonly DateTimeOffset _createdAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IGitClient _git = Substitute.For<IGitClient>();
    private readonly MockFileSystem _fileSystem = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static RefSnapshot BaseRefs => Snapshot(("heads/main", "a"), ("heads/develop", "b"), ("tags/v1", "t1"));

    public static TheoryData<string, RefSnapshot> ChangedRefs => new()
    {
        { "selected branch moved", Snapshot(("heads/main", "a2"), ("heads/develop", "b"), ("tags/v1", "t1")) },
        { "other branch moved", Snapshot(("heads/main", "a"), ("heads/develop", "b2"), ("tags/v1", "t1")) },
        { "branch added", Snapshot(("heads/main", "a"), ("heads/develop", "b"), ("heads/new", "c"), ("tags/v1", "t1")) },
        { "branch removed", Snapshot(("heads/main", "a"), ("tags/v1", "t1")) },
        { "tag added", Snapshot(("heads/main", "a"), ("heads/develop", "b"), ("tags/v1", "t1"), ("tags/v2", "t2")) },
        { "tag removed", Snapshot(("heads/main", "a"), ("heads/develop", "b")) },
        { "tag moved", Snapshot(("heads/main", "a"), ("heads/develop", "b"), ("tags/v1", "t9")) },
    };

    [Fact]
    public async Task DetectAsync_NoCloneAndNoArchive_ClonesAndArchives()
    {
        SetupClone(Sync("main", BaseRefs));

        var result = await DetectAsync(last: null);

        result.ShouldBe(new ChangeDetection(ChangeDecision.ArchiveNeeded, ChangeReason.NoArchive, "main", false, BaseRefs));
        await _git.Received(1).CloneAsync(Arg.Any<GitRepositoryRequest>(), _remoteUrl, CancellationToken);
        await NoUpdateOrRemoteReads();
    }

    [Fact]
    public async Task DetectAsync_NoCloneServerUnchanged_ClonesAndReportsUpToDate()
    {
        AddArchiveFile();
        SetupClone(Sync("main", BaseRefs));

        var result = await DetectAsync(Archive("main", BaseRefs));

        result.Decision.ShouldBe(ChangeDecision.UpToDate);
        result.Reason.ShouldBe(ChangeReason.Unchanged);
        await _git.Received(1).CloneAsync(Arg.Any<GitRepositoryRequest>(), _remoteUrl, CancellationToken);
        await NoUpdateOrRemoteReads();
    }

    [Fact]
    public async Task DetectAsync_NoCloneServerChanged_ClonesAndArchives()
    {
        AddArchiveFile();
        var changed = Snapshot(("heads/main", "a2"), ("heads/develop", "b"), ("tags/v1", "t1"));
        SetupClone(Sync("main", changed));

        var result = await DetectAsync(Archive("main", BaseRefs));

        result.Decision.ShouldBe(ChangeDecision.ArchiveNeeded);
        result.Reason.ShouldBe(ChangeReason.RefsChanged);
        result.Refs.ShouldBe(changed);
    }

    [Fact]
    public async Task DetectAsync_NoCloneArchiveFileMissing_ClonesAndArchives()
    {
        SetupClone(Sync("main", BaseRefs));

        var result = await DetectAsync(Archive("main", BaseRefs));

        result.Decision.ShouldBe(ChangeDecision.ArchiveNeeded);
        result.Reason.ShouldBe(ChangeReason.ArchiveFileMissing);
    }

    [Fact]
    public async Task DetectAsync_CloneExistsAndNoArchive_UpdatesWithoutReadingRemote()
    {
        AddClone();
        SetupUpdate(Sync("main", BaseRefs));

        var result = await DetectAsync(last: null);

        result.Decision.ShouldBe(ChangeDecision.ArchiveNeeded);
        result.Reason.ShouldBe(ChangeReason.NoArchive);
        await _git.Received(1).UpdateAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken);
        await _git.DidNotReceive().GetRemoteRefsAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken);
        await _git.DidNotReceive().CloneAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<string>(), CancellationToken);
    }

    [Fact]
    public async Task DetectAsync_ArchiveFileMissing_UpdatesWithoutReadingRemote()
    {
        AddClone();
        SetupUpdate(Sync("main", BaseRefs));

        var result = await DetectAsync(Archive("main", BaseRefs));

        result.Decision.ShouldBe(ChangeDecision.ArchiveNeeded);
        result.Reason.ShouldBe(ChangeReason.ArchiveFileMissing);
        await _git.Received(1).UpdateAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken);
        await _git.DidNotReceive().GetRemoteRefsAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken);
    }

    [Fact]
    public async Task DetectAsync_ArchiveIsCloudPlaceholder_CountsAsPresent()
    {
        AddClone();
        _fileSystem.AddFile(
            Path.Combine(_archiveFolder, _archiveFile),
            new MockFileData(string.Empty) { Attributes = FileAttributes.Offline | FileAttributes.ReparsePoint | _recallOnDataAccess });
        SetupRemote(BaseRefs);

        var result = await DetectAsync(Archive("main", BaseRefs));

        result.Decision.ShouldBe(ChangeDecision.UpToDate);
        await _git.DidNotReceive().UpdateAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken);
    }

    [Fact]
    public async Task DetectAsync_RefsAndBranchUnchanged_ReportsUpToDateWithoutFetch()
    {
        AddClone();
        AddArchiveFile();
        SetupRemote(BaseRefs);

        var result = await DetectAsync(Archive("main", BaseRefs));

        result.ShouldBe(new ChangeDetection(ChangeDecision.UpToDate, ChangeReason.Unchanged, "main", false, BaseRefs));
        await _git.DidNotReceive().UpdateAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken);
        await _git.DidNotReceive().GetDefaultBranchAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken);
    }

    [Fact]
    public async Task DetectAsync_BranchNotSetAndUnchanged_UsesServerDefaultBranch()
    {
        AddClone();
        AddArchiveFile();
        SetupRemote(BaseRefs);
        SetupDefaultBranch("main");

        var result = await DetectAsync(Archive("main", BaseRefs), branch: null);

        result.Decision.ShouldBe(ChangeDecision.UpToDate);
        result.Branch.ShouldBe("main");
        result.BranchFallback.ShouldBeFalse();
        await _git.DidNotReceive().UpdateAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken);
    }

    [Fact]
    public async Task DetectAsync_ConfiguredBranchChanged_ArchivesBranchChange()
    {
        AddClone();
        AddArchiveFile();
        SetupRemote(BaseRefs);
        SetupUpdate(Sync("develop", BaseRefs));

        var result = await DetectAsync(Archive("main", BaseRefs), branch: "develop");

        result.Decision.ShouldBe(ChangeDecision.ArchiveNeeded);
        result.Reason.ShouldBe(ChangeReason.BranchChanged);
        result.Branch.ShouldBe("develop");
    }

    [Fact]
    public async Task DetectAsync_ServerDefaultBranchChanged_ArchivesBranchChange()
    {
        AddClone();
        AddArchiveFile();
        SetupRemote(BaseRefs);
        SetupDefaultBranch("develop");
        SetupUpdate(Sync("develop", BaseRefs));

        var result = await DetectAsync(Archive("main", BaseRefs), branch: null);

        result.Decision.ShouldBe(ChangeDecision.ArchiveNeeded);
        result.Reason.ShouldBe(ChangeReason.BranchChanged);
        result.Branch.ShouldBe("develop");
    }

    [Fact]
    public async Task DetectAsync_ServerDefaultBranchUnknown_UpdatesToDecide()
    {
        AddClone();
        AddArchiveFile();
        SetupRemote(BaseRefs);
        SetupDefaultBranch(null);
        SetupUpdate(Sync("main", BaseRefs));

        var result = await DetectAsync(Archive("main", BaseRefs), branch: null);

        result.Decision.ShouldBe(ChangeDecision.UpToDate);
        await _git.Received(1).UpdateAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken);
    }

    [Fact]
    public async Task DetectAsync_DefaultBranchMissingFromRemoteRefs_UpdatesToDecide()
    {
        AddClone();
        AddArchiveFile();
        SetupRemote(BaseRefs);
        SetupDefaultBranch("gone");
        SetupUpdate(Sync("main", BaseRefs, fallback: true));

        var result = await DetectAsync(Archive("main", BaseRefs), branch: null);

        result.Decision.ShouldBe(ChangeDecision.UpToDate);
        await _git.Received(1).UpdateAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken);
    }

    [Fact]
    public async Task DetectAsync_RequestedBranchMissingOnServer_ReportsFallbackWhenUpToDate()
    {
        AddClone();
        AddArchiveFile();
        SetupRemote(BaseRefs);
        SetupDefaultBranch("main");

        var result = await DetectAsync(Archive("main", BaseRefs), branch: "missing");

        result.Decision.ShouldBe(ChangeDecision.UpToDate);
        result.Branch.ShouldBe("main");
        result.BranchFallback.ShouldBeTrue();
        await _git.DidNotReceive().UpdateAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken);
    }

    [Fact]
    public async Task DetectAsync_RequestedBranchMissingAndRefsChanged_CarriesFallbackFromUpdate()
    {
        AddClone();
        AddArchiveFile();
        var changed = Snapshot(("heads/main", "a2"));
        SetupRemote(changed);
        SetupUpdate(Sync("main", changed, fallback: true));

        var result = await DetectAsync(Archive("main", BaseRefs), branch: "missing");

        result.Decision.ShouldBe(ChangeDecision.ArchiveNeeded);
        result.BranchFallback.ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(ChangedRefs))]
    public async Task DetectAsync_RefsChanged_ArchivesRefsChange(string change, RefSnapshot changed)
    {
        change.ShouldNotBeNullOrEmpty();
        AddClone();
        AddArchiveFile();
        SetupRemote(changed);
        SetupUpdate(Sync("main", changed));

        var result = await DetectAsync(Archive("main", BaseRefs));

        result.Decision.ShouldBe(ChangeDecision.ArchiveNeeded);
        result.Reason.ShouldBe(ChangeReason.RefsChanged);
        result.Refs.ShouldBe(changed);
        await _git.Received(1).UpdateAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken);
    }

    [Fact]
    public async Task DetectAsync_RemoteDiffersButCloneMatchesArchive_ReportsUpToDate()
    {
        AddClone();
        AddArchiveFile();
        SetupRemote(Snapshot(("heads/main", "a2")));
        SetupUpdate(Sync("main", BaseRefs));

        var result = await DetectAsync(Archive("main", BaseRefs));

        result.Decision.ShouldBe(ChangeDecision.UpToDate);
        result.Reason.ShouldBe(ChangeReason.Unchanged);
        await _git.Received(1).UpdateAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken);
    }

    [Fact]
    public async Task DetectAsync_EmptyRepositoryOnUpdate_NothingToArchive()
    {
        AddClone();
        AddArchiveFile();
        SetupRemote(RefSnapshot.Empty);
        SetupDefaultBranch(null);
        SetupUpdate(new GitSyncResult(null, false, RefSnapshot.Empty));

        var result = await DetectAsync(Archive("main", BaseRefs));

        result.ShouldBe(new ChangeDetection(ChangeDecision.NothingToArchive, ChangeReason.EmptyRepository, null, false, RefSnapshot.Empty));
    }

    [Fact]
    public async Task DetectAsync_EmptyRepositoryOnClone_NothingToArchive()
    {
        SetupClone(new GitSyncResult(null, false, RefSnapshot.Empty));

        var result = await DetectAsync(last: null);

        result.Decision.ShouldBe(ChangeDecision.NothingToArchive);
        result.Reason.ShouldBe(ChangeReason.EmptyRepository);
    }

    [Fact]
    public async Task DetectAsync_BranchNotFound_Propagates()
    {
        AddClone();
        _git.UpdateAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken)
            .Returns(Task.FromException<GitSyncResult>(new GitBranchNotFoundException("no branch", "x", null)));

        await Should.ThrowAsync<GitBranchNotFoundException>(() => DetectAsync(last: null));
    }

    [Fact]
    public async Task DetectAsync_CloneFails_Propagates()
    {
        _git.CloneAsync(Arg.Any<GitRepositoryRequest>(), _remoteUrl, CancellationToken)
            .Returns(Task.FromException<GitSyncResult>(new GitCommandException("failed", "git clone", 128)));

        await Should.ThrowAsync<GitCommandException>(() => DetectAsync(last: null));
    }

    [Fact]
    public async Task DetectAsync_CancelledWhileReadingRemote_Propagates()
    {
        AddClone();
        AddArchiveFile();
        _git.GetRemoteRefsAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken)
            .Returns(Task.FromException<RefSnapshot>(new OperationCanceledException()));

        await Should.ThrowAsync<OperationCanceledException>(() => DetectAsync(Archive("main", BaseRefs)));
    }

    [Fact]
    public async Task DetectAsync_RelativeArchiveFolder_ThrowsArgumentException()
    {
        var request = CreateRequest(last: null, "main") with { ArchiveFolder = @"archives\repo" };

        await Should.ThrowAsync<ArgumentException>(() => new ChangeDetector(_git, _fileSystem, NullLogger<ChangeDetector>.Instance)
            .DetectAsync(request, CancellationToken));
    }

    private static RefSnapshot Snapshot(params (string Name, string Id)[] refs) =>
        new(refs.Select(entry => KeyValuePair.Create(entry.Name, entry.Id)));

    private static GitSyncResult Sync(string branch, RefSnapshot refs, bool fallback = false) => new(branch, fallback, refs);

    private static ArchiveRecord Archive(string branch, RefSnapshot refs) =>
        new(_archiveFile, _createdAt, branch, "0123456789abcdef", 10, refs);

    private static ChangeDetectionRequest CreateRequest(ArchiveRecord? last, string? branch) => new()
    {
        Git = new GitRepositoryRequest { GitPath = @"C:\Tools\git.exe", ClonePath = _clonePath, Branch = branch },
        RemoteUrl = _remoteUrl,
        ArchiveFolder = _archiveFolder,
        LastArchive = last,
    };

    private Task<ChangeDetection> DetectAsync(ArchiveRecord? last, string? branch = "main") =>
        new ChangeDetector(_git, _fileSystem, NullLogger<ChangeDetector>.Instance)
            .DetectAsync(CreateRequest(last, branch), CancellationToken);

    private void AddClone() => _fileSystem.AddDirectory(Path.Combine(_clonePath, ".git"));

    private void AddArchiveFile() => _fileSystem.AddFile(Path.Combine(_archiveFolder, _archiveFile), new MockFileData("archive"));

    private void SetupClone(GitSyncResult result) =>
        _git.CloneAsync(Arg.Any<GitRepositoryRequest>(), _remoteUrl, CancellationToken).Returns(result);

    private void SetupUpdate(GitSyncResult result) =>
        _git.UpdateAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken).Returns(result);

    private void SetupRemote(RefSnapshot refs) =>
        _git.GetRemoteRefsAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken).Returns(refs);

    private void SetupDefaultBranch(string? branch) =>
        _git.GetDefaultBranchAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken).Returns(branch);

    private async Task NoUpdateOrRemoteReads()
    {
        await _git.DidNotReceive().UpdateAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken);
        await _git.DidNotReceive().GetRemoteRefsAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken);
    }
}
