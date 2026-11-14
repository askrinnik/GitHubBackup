using System.Diagnostics;
using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Changes;
using GitHubBackup.Core.Clones;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.State;
using GitHubBackup.Core.Telemetry;
using GitHubBackup.Core.Tests.Runs;
using GitHubBackup.Core.Tests.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GitHubBackup.Core.Tests.Changes;

/// <summary>
/// Tests <see cref="ChangeDetector"/> against a substitute <see cref="IGitClient"/> and an in-memory file system.
/// </summary>
public sealed class ChangeDetectorTests : IDisposable
{
    private const string _archiveFolder = @"C:\backup\archives\owner\repo";
    private const string _clonePath = @"C:\backup\clones\owner\repo";
    private const string _remoteUrl = "https://github.com/owner/repo";
    private const string _archiveFile = "repo.7z";

    /// <summary>The Windows attribute <c>FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS</c> that OneDrive sets on a file stored only in the cloud.</summary>
    private const FileAttributes _recallOnDataAccess = (FileAttributes)0x400000;

    private static readonly DateTimeOffset _createdAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IGitClient _git = Substitute.For<IGitClient>();
    private readonly ICloneGuard _guard = Substitute.For<ICloneGuard>();
    private readonly MockFileSystem _fileSystem = new();
    private readonly TelemetryCapture _capture = new();
    private readonly RecordingProgress<BackupProgressStage> _progress = new();

    /// <summary>
    /// Sets up a clone whose origin points at the repository and a guard that finds no local work.
    /// </summary>
    public ChangeDetectorTests()
    {
        _git.GetOriginUrlAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<CancellationToken>()).Returns(_remoteUrl + ".git");
        _guard.CheckAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<RefSnapshot?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new CloneCheckResult(_clonePath, []) { ExpectedBranch = "main" });
        _guard.CheckLocalAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<RefSnapshot?>(), Arg.Any<CancellationToken>())
            .Returns(new CloneCheckResult(_clonePath, []));
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    /// <inheritdoc />
    public void Dispose() => _capture.Dispose();

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
        await _git.Received(1).SyncAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<string?>(), CancellationToken);
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
        await _git.Received(1).SyncAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<string?>(), CancellationToken);
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
        await _git.DidNotReceive().SyncAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<string?>(), CancellationToken);
    }

    [Fact]
    public async Task DetectAsync_RefsAndBranchUnchanged_ReportsUpToDateWithoutFetch()
    {
        AddClone();
        AddArchiveFile();
        SetupRemote(BaseRefs);

        var result = await DetectAsync(Archive("main", BaseRefs));

        result.ShouldBe(new ChangeDetection(ChangeDecision.UpToDate, ChangeReason.Unchanged, "main", false, BaseRefs));
        await _git.DidNotReceive().SyncAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<string?>(), CancellationToken);
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
        await _git.DidNotReceive().SyncAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<string?>(), CancellationToken);
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
        await _git.Received(1).SyncAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<string?>(), CancellationToken);
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
        await _git.Received(1).SyncAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<string?>(), CancellationToken);
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
        await _git.DidNotReceive().SyncAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<string?>(), CancellationToken);
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
        await _git.Received(1).SyncAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<string?>(), CancellationToken);
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
        await _git.Received(1).SyncAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<string?>(), CancellationToken);
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
        _git.SyncAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<string?>(), CancellationToken)
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

        await Should.ThrowAsync<ArgumentException>(() => new ChangeDetector(_git, _guard, _fileSystem, _capture.Telemetry, NullLogger<ChangeDetector>.Instance)
            .DetectAsync(request, CancellationToken));
    }

    [Fact]
    public async Task DetectAsync_LocalChanges_ThrowsWithoutUpdating()
    {
        AddClone();
        AddArchiveFile();
        var changed = Snapshot(("heads/main", "a2"));
        SetupRemote(changed);
        var check = new CloneCheckResult(_clonePath, [CloneCheckResult.UncommittedChanges]);
        _guard.CheckAsync(Arg.Any<GitRepositoryRequest>(), changed, "main", CancellationToken).Returns(check);

        var exception = await Should.ThrowAsync<LocalChangesException>(() => DetectAsync(Archive("main", BaseRefs)));

        exception.Message.ShouldBe(check.Message);
        await _git.DidNotReceive().SyncAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DetectAsync_NoArchive_ChecksCloneWithoutRemoteRefsBeforeUpdating()
    {
        AddClone();
        SetupUpdate(Sync("main", BaseRefs));

        await DetectAsync(last: null);

        Received.InOrder(() =>
        {
            _guard.CheckAsync(
                Arg.Is<GitRepositoryRequest>(git => git.ClonePath == _clonePath), null, null, CancellationToken);
            _git.SyncAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<string?>(), CancellationToken);
        });
    }

    [Fact]
    public async Task DetectAsync_CloneUpdated_FetchesThenChecksAgainLocallyThenSyncs()
    {
        AddClone();
        SetupUpdate(Sync("main", BaseRefs));
        var beforeFetch = Snapshot(("heads/main", "old"));
        _git.GetLocalRefsAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken).Returns(beforeFetch);

        await DetectAsync(Archive("dev", BaseRefs));

        Received.InOrder(() =>
        {
            _guard.CheckAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<RefSnapshot?>(), "dev", CancellationToken);
            _git.GetLocalRefsAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken);
            _git.FetchAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken);
            _guard.CheckLocalAsync(Arg.Any<GitRepositoryRequest>(), "main", "dev", beforeFetch, CancellationToken);
            _git.SyncAsync(Arg.Any<GitRepositoryRequest>(), "main", CancellationToken);
        });
    }

    [Fact]
    public async Task DetectAsync_WorkSavedDuringFetch_ThrowsWithoutSync()
    {
        AddClone();
        var recheck = new CloneCheckResult(_clonePath, [CloneCheckResult.UntrackedFiles]);
        _guard.CheckLocalAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<RefSnapshot?>(), CancellationToken)
            .Returns(recheck);

        var exception = await Should.ThrowAsync<LocalChangesException>(() => DetectAsync(last: null));

        exception.Message.ShouldBe(recheck.Message);
        await _git.Received(1).FetchAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken);
        await _git.DidNotReceiveWithAnyArgs().SyncAsync(default!, default, CancellationToken);
    }

    [Fact]
    public async Task DetectAsync_ServerUnchanged_ReportsUpToDateWithoutCheckingClone()
    {
        AddClone();
        AddArchiveFile();
        SetupRemote(BaseRefs);

        var result = await DetectAsync(Archive("main", BaseRefs));

        result.Decision.ShouldBe(ChangeDecision.UpToDate);
        await _guard.DidNotReceiveWithAnyArgs().CheckAsync(default!, default, default, CancellationToken);
    }

    [Theory]
    [InlineData("https://github.com/other/repo.git")]
    [InlineData("git@github.com:owner/repo.git")]
    [InlineData(null)]
    public async Task DetectAsync_OriginElsewhere_ThrowsBeforeReadingServer(string? origin)
    {
        AddClone();
        AddArchiveFile();
        _git.GetOriginUrlAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken).Returns(origin);

        var exception = await Should.ThrowAsync<CloneFolderException>(() => DetectAsync(Archive("main", BaseRefs)));

        exception.Folder.ShouldBe(_clonePath);
        exception.Message.ShouldContain(_remoteUrl);
        await NoUpdateOrRemoteReads();
    }

    [Theory]
    [InlineData("https://github.com/owner/repo")]
    [InlineData("https://github.com/Owner/Repo.git")]
    [InlineData("https://github.com/owner/repo/")]
    public async Task DetectAsync_OriginSameRepositoryInOtherForm_UsesClone(string origin)
    {
        AddClone();
        SetupUpdate(Sync("main", BaseRefs));
        _git.GetOriginUrlAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken).Returns(origin);

        var result = await DetectAsync(last: null);

        result.Decision.ShouldBe(ChangeDecision.ArchiveNeeded);
        await _git.Received(1).SyncAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<string?>(), CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetectAsync_FolderNotEmptyWithoutGitFolder_ThrowsAndLeavesFolder(bool gitFile)
    {
        _fileSystem.AddFile(Path.Combine(_clonePath, gitFile ? ".git" : "notes.txt"), new MockFileData("data"));

        await Should.ThrowAsync<CloneFolderException>(() => DetectAsync(last: null));

        _git.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task DetectAsync_EmptyFolder_ClonesIntoIt()
    {
        _fileSystem.AddDirectory(_clonePath);
        SetupClone(Sync("main", BaseRefs));

        await DetectAsync(last: null);

        await _git.Received(1).CloneAsync(Arg.Any<GitRepositoryRequest>(), _remoteUrl, CancellationToken);
    }

    [Fact]
    public async Task PreviewAsync_ChangedAndLocalChanges_ThrowsTheSameFailure()
    {
        AddClone();
        AddArchiveFile();
        var changed = Snapshot(("heads/main", "a2"));
        SetupRemote(changed);
        var check = new CloneCheckResult(_clonePath, [CloneCheckResult.UntrackedFiles]);
        _guard.CheckAsync(Arg.Any<GitRepositoryRequest>(), changed, "main", CancellationToken).Returns(check);

        var exception = await Should.ThrowAsync<LocalChangesException>(() => PreviewAsync(Archive("main", BaseRefs)));

        exception.Message.ShouldBe(check.Message);
    }

    [Fact]
    public async Task PreviewAsync_Unchanged_DoesNotCheckClone()
    {
        AddClone();
        AddArchiveFile();
        SetupRemote(BaseRefs);

        await PreviewAsync(Archive("main", BaseRefs));

        await _guard.DidNotReceiveWithAnyArgs().CheckAsync(default!, default, default, CancellationToken);
    }

    [Fact]
    public async Task PreviewAsync_FolderNotEmptyWithoutGitFolder_Throws()
    {
        _fileSystem.AddFile(Path.Combine(_clonePath, "notes.txt"), new MockFileData("data"));

        await Should.ThrowAsync<CloneFolderException>(() => PreviewAsync(last: null));
    }

    [Fact]
    public async Task PreviewAsync_NoClone_ReturnsCloneNeededWithoutGit()
    {
        AddArchiveFile();

        var result = await PreviewAsync(Archive("main", BaseRefs));

        result.ShouldBe(new ChangeDetection(ChangeDecision.CloneNeeded, ChangeReason.NoClone, null, false, RefSnapshot.Empty));
        _git.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task PreviewAsync_RefsUnchanged_ReturnsUpToDate()
    {
        AddClone();
        AddArchiveFile();
        SetupRemote(BaseRefs);

        var result = await PreviewAsync(Archive("main", BaseRefs));

        result.ShouldBe(new ChangeDetection(ChangeDecision.UpToDate, ChangeReason.Unchanged, "main", false, BaseRefs));
        await NoCloneOrUpdate();
    }

    [Fact]
    public async Task PreviewAsync_RefsChanged_ReturnsArchiveNeeded()
    {
        AddClone();
        AddArchiveFile();
        var changed = Snapshot(("heads/main", "a2"), ("heads/develop", "b"), ("tags/v1", "t1"));
        SetupRemote(changed);

        var result = await PreviewAsync(Archive("main", BaseRefs));

        result.ShouldBe(new ChangeDetection(ChangeDecision.ArchiveNeeded, ChangeReason.RefsChanged, "main", false, changed));
        await NoCloneOrUpdate();
    }

    [Fact]
    public async Task PreviewAsync_ArchiveFileMissing_ReturnsArchiveNeeded()
    {
        AddClone();
        SetupRemote(BaseRefs);

        var result = await PreviewAsync(Archive("main", BaseRefs));

        result.Decision.ShouldBe(ChangeDecision.ArchiveNeeded);
        result.Reason.ShouldBe(ChangeReason.ArchiveFileMissing);
        await NoCloneOrUpdate();
    }

    [Fact]
    public async Task PreviewAsync_NoLastArchive_ReturnsArchiveNeeded()
    {
        AddClone();
        SetupRemote(BaseRefs);

        var result = await PreviewAsync(last: null);

        result.ShouldBe(new ChangeDetection(ChangeDecision.ArchiveNeeded, ChangeReason.NoArchive, "main", false, BaseRefs));
        await NoCloneOrUpdate();
    }

    [Fact]
    public async Task PreviewAsync_BranchChanged_ReturnsArchiveNeeded()
    {
        AddClone();
        AddArchiveFile();
        SetupRemote(BaseRefs);

        var result = await PreviewAsync(Archive("main", BaseRefs), branch: "develop");

        result.Decision.ShouldBe(ChangeDecision.ArchiveNeeded);
        result.Reason.ShouldBe(ChangeReason.BranchChanged);
        result.Branch.ShouldBe("develop");
        await NoCloneOrUpdate();
    }

    [Fact]
    public async Task PreviewAsync_EmptyRemote_ReturnsNothingToArchive()
    {
        AddClone();
        SetupRemote(RefSnapshot.Empty);

        var result = await PreviewAsync(last: null);

        result.ShouldBe(new ChangeDetection(
            ChangeDecision.NothingToArchive, ChangeReason.EmptyRepository, null, false, RefSnapshot.Empty));
        await _git.DidNotReceive().GetDefaultBranchAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken);
        await NoCloneOrUpdate();
    }

    [Fact]
    public async Task PreviewAsync_BranchMissing_FallsBackToDefault()
    {
        AddClone();
        AddArchiveFile();
        SetupRemote(BaseRefs);
        SetupDefaultBranch("main");

        var result = await PreviewAsync(Archive("main", BaseRefs), branch: "gone");

        result.ShouldBe(new ChangeDetection(ChangeDecision.UpToDate, ChangeReason.Unchanged, "main", true, BaseRefs));
        await NoCloneOrUpdate();
    }

    [Fact]
    public async Task PreviewAsync_BranchNotSet_UsesDefaultWithoutFallback()
    {
        AddClone();
        AddArchiveFile();
        SetupRemote(BaseRefs);
        SetupDefaultBranch("develop");

        var result = await PreviewAsync(Archive("main", BaseRefs), branch: null);

        result.ShouldBe(new ChangeDetection(ChangeDecision.ArchiveNeeded, ChangeReason.BranchChanged, "develop", false, BaseRefs));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("gone")]
    public async Task PreviewAsync_NoBranchAtAll_ThrowsGitBranchNotFound(string? defaultBranch)
    {
        AddClone();
        SetupRemote(BaseRefs);
        SetupDefaultBranch(defaultBranch);

        var exception = await Should.ThrowAsync<GitBranchNotFoundException>(() => PreviewAsync(last: null, branch: "missing"));

        exception.RequestedBranch.ShouldBe("missing");
        exception.DefaultBranch.ShouldBe(defaultBranch);
        await NoCloneOrUpdate();
    }

    [Fact]
    public async Task PreviewAsync_RequestedBranchStartsWithDash_ThrowsArgumentException()
    {
        AddClone();
        SetupRemote(BaseRefs);

        await Should.ThrowAsync<ArgumentException>(() => PreviewAsync(last: null, branch: "-main"));

        await _git.DidNotReceive().GetDefaultBranchAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken);
        await NoCloneOrUpdate();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("gone")]
    public async Task PreviewAsync_DefaultBranchStartsWithDash_ThrowsArgumentException(string? requested)
    {
        AddClone();
        SetupRemote(Snapshot(("heads/-main", "a")));
        SetupDefaultBranch("-main");

        await Should.ThrowAsync<ArgumentException>(() => PreviewAsync(last: null, branch: requested));

        await NoCloneOrUpdate();
    }

    [Fact]
    public async Task PreviewAsync_RemoteReadFails_Propagates()
    {
        AddClone();
        _git.GetRemoteRefsAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken)
            .Returns(Task.FromException<RefSnapshot>(new GitCommandException("failed", "git ls-remote", 128)));

        await Should.ThrowAsync<GitCommandException>(() => PreviewAsync(last: null));
    }

    [Fact]
    public async Task PreviewAsync_RelativeArchiveFolder_ThrowsArgumentException()
    {
        var request = CreateRequest(last: null, "main") with { ArchiveFolder = @"archives\repo" };

        await Should.ThrowAsync<ArgumentException>(() => new ChangeDetector(_git, _guard, _fileSystem, _capture.Telemetry, NullLogger<ChangeDetector>.Instance)
            .PreviewAsync(request, CancellationToken));
    }

    [Fact]
    public async Task DetectAsync_NoClone_TracesCloneUnderCurrentSpan()
    {
        SetupClone(Sync("main", BaseRefs));
        using var test = _capture.StartTest();

        await DetectAsync(last: null);

        var span = _capture.Spans.ShouldHaveSingleItem();
        span.OperationName.ShouldBe(BackupTelemetry.CloneOperation);
        span.GetTagItem(BackupTelemetry.OperationTag).ShouldBe(BackupTelemetry.CloneOperation);
        span.ParentSpanId.ShouldBe(test.SpanId);
        span.Status.ShouldBe(ActivityStatusCode.Unset);
    }

    [Fact]
    public async Task DetectAsync_ServerUnchanged_TracesOnlyLsRemote()
    {
        AddClone();
        AddArchiveFile();
        SetupRemote(BaseRefs);
        using var test = _capture.StartTest();

        await DetectAsync(Archive("main", BaseRefs));

        Operations().ShouldBe([BackupTelemetry.LsRemoteOperation]);
    }

    [Fact]
    public async Task DetectAsync_ServerChanged_TracesLsRemoteThenFetch()
    {
        AddClone();
        AddArchiveFile();
        var changed = Snapshot(("heads/main", "a2"), ("heads/develop", "b"), ("tags/v1", "t1"));
        SetupRemote(changed);
        SetupUpdate(Sync("main", changed));
        using var test = _capture.StartTest();

        await DetectAsync(Archive("main", BaseRefs));

        Operations().ShouldBe([BackupTelemetry.LsRemoteOperation, BackupTelemetry.FetchOperation]);
    }

    [Fact]
    public async Task PreviewAsync_RequestedBranchMissing_TracesLsRemoteForRefsAndDefaultBranch()
    {
        AddClone();
        SetupRemote(BaseRefs);
        SetupDefaultBranch("main");
        using var test = _capture.StartTest();

        await PreviewAsync(last: null, branch: "gone");

        Operations().ShouldBe([BackupTelemetry.LsRemoteOperation, BackupTelemetry.LsRemoteOperation]);
    }

    [Fact]
    public async Task PreviewAsync_NoClone_TracesNoOperation()
    {
        using var test = _capture.StartTest();

        await PreviewAsync(last: null);

        _capture.Spans.ShouldBeEmpty();
    }

    [Fact]
    public async Task DetectAsync_CloneFails_MarksOperationFailedWithTypeOnly()
    {
        _git.CloneAsync(Arg.Any<GitRepositoryRequest>(), _remoteUrl, CancellationToken)
            .ThrowsAsync(new InvalidOperationException("git clone failed for secret-token"));
        using var test = _capture.StartTest();

        await Should.ThrowAsync<InvalidOperationException>(() => DetectAsync(last: null));

        var span = _capture.Spans.ShouldHaveSingleItem();
        span.Status.ShouldBe(ActivityStatusCode.Error);
        span.StatusDescription.ShouldBeNull();
        span.GetTagItem(BackupTelemetry.ErrorTypeTag).ShouldBe(typeof(InvalidOperationException).FullName);
        span.Events.ShouldBeEmpty();
        span.TagObjects.ShouldAllBe(tag => !(tag.Value ?? string.Empty).ToString()!.Contains("secret-token"));
    }

    [Fact]
    public async Task DetectAsync_CloneCancelled_LeavesOperationStatusUnset()
    {
        _git.CloneAsync(Arg.Any<GitRepositoryRequest>(), _remoteUrl, CancellationToken)
            .ThrowsAsync(new OperationCanceledException());
        using var test = _capture.StartTest();

        await Should.ThrowAsync<OperationCanceledException>(() => DetectAsync(last: null));

        _capture.Spans.ShouldHaveSingleItem().Status.ShouldBe(ActivityStatusCode.Unset);
    }

    [Fact]
    public async Task DetectAsync_NoClone_ReportsCloning()
    {
        SetupClone(Sync("main", BaseRefs));

        await DetectWithProgressAsync(last: null);

        _progress.Values.ShouldBe([BackupProgressStage.Cloning]);
    }

    [Fact]
    public async Task DetectAsync_CloneExistsAndNoArchive_ReportsFetchingOnly()
    {
        AddClone();
        SetupUpdate(Sync("main", BaseRefs));

        await DetectWithProgressAsync(last: null);

        _progress.Values.ShouldBe([BackupProgressStage.Fetching]);
    }

    [Fact]
    public async Task DetectAsync_ServerUnchanged_ReportsCheckingRemoteOnly()
    {
        AddClone();
        AddArchiveFile();
        SetupRemote(BaseRefs);

        await DetectWithProgressAsync(Archive("main", BaseRefs));

        _progress.Values.ShouldBe([BackupProgressStage.CheckingRemote]);
    }

    [Fact]
    public async Task DetectAsync_ServerChanged_ReportsCheckingRemoteThenFetching()
    {
        AddClone();
        AddArchiveFile();
        var changed = Snapshot(("heads/main", "a2"), ("heads/develop", "b"), ("tags/v1", "t1"));
        SetupRemote(changed);
        SetupUpdate(Sync("main", changed));

        await DetectWithProgressAsync(Archive("main", BaseRefs));

        _progress.Values.ShouldBe([BackupProgressStage.CheckingRemote, BackupProgressStage.Fetching]);
    }

    [Fact]
    public async Task PreviewAsync_CloneExists_ReportsCheckingRemote()
    {
        AddClone();
        SetupRemote(BaseRefs);

        await CreateDetector().PreviewAsync(
            CreateRequest(last: null, "main") with { OperationProgress = _progress }, CancellationToken);

        _progress.Values.ShouldBe([BackupProgressStage.CheckingRemote]);
    }

    [Fact]
    public async Task PreviewAsync_NoClone_ReportsNothing()
    {
        await CreateDetector().PreviewAsync(
            CreateRequest(last: null, "main") with { OperationProgress = _progress }, CancellationToken);

        _progress.Values.ShouldBeEmpty();
    }

    /// <summary>
    /// Creates the detector over the substitute git client, the in-memory file system and the telemetry capture.
    /// </summary>
    /// <returns>The detector.</returns>
    private ChangeDetector CreateDetector() =>
        new(_git, _guard, _fileSystem, _capture.Telemetry, NullLogger<ChangeDetector>.Instance);

    /// <summary>
    /// Runs the detection of the branch <c>main</c> with <see cref="_progress"/> as the receiver of its operations.
    /// </summary>
    /// <param name="last">The last archive, or <see langword="null"/>.</param>
    /// <returns>The detection result.</returns>
    private Task<ChangeDetection> DetectWithProgressAsync(ArchiveRecord? last) =>
        CreateDetector().DetectAsync(CreateRequest(last, "main") with { OperationProgress = _progress }, CancellationToken);

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
        new ChangeDetector(_git, _guard, _fileSystem, _capture.Telemetry, NullLogger<ChangeDetector>.Instance)
            .DetectAsync(CreateRequest(last, branch), CancellationToken);

    private Task<ChangeDetection> PreviewAsync(ArchiveRecord? last, string? branch = "main") =>
        new ChangeDetector(_git, _guard, _fileSystem, _capture.Telemetry, NullLogger<ChangeDetector>.Instance)
            .PreviewAsync(CreateRequest(last, branch), CancellationToken);

    private void AddClone() => _fileSystem.AddDirectory(Path.Combine(_clonePath, ".git"));

    private void AddArchiveFile() => _fileSystem.AddFile(Path.Combine(_archiveFolder, _archiveFile), new MockFileData("archive"));

    private void SetupClone(GitSyncResult result) =>
        _git.CloneAsync(Arg.Any<GitRepositoryRequest>(), _remoteUrl, CancellationToken).Returns(result);

    private void SetupUpdate(GitSyncResult result) =>
        _git.SyncAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<string?>(), CancellationToken).Returns(result);

    private void SetupRemote(RefSnapshot refs) =>
        _git.GetRemoteRefsAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken).Returns(refs);

    private void SetupDefaultBranch(string? branch) =>
        _git.GetDefaultBranchAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken).Returns(branch);

    private async Task NoUpdateOrRemoteReads()
    {
        await _git.DidNotReceive().SyncAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<string?>(), CancellationToken);
        await _git.DidNotReceive().GetRemoteRefsAsync(Arg.Any<GitRepositoryRequest>(), CancellationToken);
    }

    private List<string?> Operations() =>
        [.. _capture.Spans.Select(span => span.GetTagItem(BackupTelemetry.OperationTag) as string)];

    private async Task NoCloneOrUpdate()
    {
        await _git.DidNotReceive().CloneAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _git.DidNotReceive().SyncAsync(Arg.Any<GitRepositoryRequest>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}
