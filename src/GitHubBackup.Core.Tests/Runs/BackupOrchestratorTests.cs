using System.Diagnostics;
using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Archiving;
using GitHubBackup.Core.Authentication;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Changes;
using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.State;
using GitHubBackup.Core.Telemetry;
using GitHubBackup.Core.Tests.Telemetry;
using GitHubBackup.Core.Tools;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GitHubBackup.Core.Tests.Runs;

/// <summary>
/// Tests <see cref="BackupOrchestrator"/> with substitutes for the stores, the tools, discovery, the detector and the
/// archiver, an in-memory file system and a fake clock.
/// </summary>
public sealed class BackupOrchestratorTests : IDisposable
{
    private const string _sourcesRoot = @"C:\sources";
    private const string _temporaryFolder = @"C:\sources\.tmp";
    private const string _gitPath = @"C:\git\git.exe";
    private const string _sevenZipPath = @"C:\7z\7z.exe";
    private const string _headCommit = "0123456789abcdef0123456789abcdef01234567";
    private const string _archiveFile = "owner_repo_20261111-120000_0123456789.zip";
    private const string _wikiArchiveFile = "owner_repo.wiki_20261111-120000_0123456789.zip";
    private const string _repositoryUrl = "https://github.com/owner/repo.git";
    private const string _wikiUrl = "https://github.com/owner/repo.wiki.git";

    private const int _runStoppedEventId = 1;
    private const int _temporaryEntryDeletedEventId = 2;
    private const int _temporaryEntryNotDeletedEventId = 3;
    private const int _repositoryWarningEventId = 7;
    private const int _repositoryFailedEventId = 8;
    private const int _temporaryFolderIsLinkEventId = 10;
    private const int _repositoryExcludedEventId = 11;
    private const int _wikiNotFoundEventId = 13;
    private const int _wikiLookupFailedEventId = 14;
    private const int _discoverySkippedEventId = 15;
    private const int _dryRunStartedEventId = 18;
    private const int _temporaryFolderLeftUntouchedEventId = 19;
    private const int _repositoryMissingEventId = 20;
    private const int _runCancelledEventId = 21;
    private const int _repositoryIdUnresolvedEventId = 22;

    private static readonly DateTimeOffset _createdAt = new(2026, 11, 11, 12, 0, 0, TimeSpan.FromHours(1));
    private static readonly RefSnapshot _refs = new([new("heads/main", _headCommit), new("tags/v1", "abc")]);

    private readonly IBackupConfigStore _configStore = Substitute.For<IBackupConfigStore>();
    private readonly IToolLocator _toolLocator = Substitute.For<IToolLocator>();
    private readonly ITokenProvider _tokenProvider = Substitute.For<ITokenProvider>();
    private readonly IRunLock _runLock = Substitute.For<IRunLock>();
    private readonly IDisposable _runLockHandle = Substitute.For<IDisposable>();
    private readonly IGitHubApi _gitHubApi = Substitute.For<IGitHubApi>();
    private readonly IRepositoryDiscoveryService _discovery = Substitute.For<IRepositoryDiscoveryService>();
    private readonly IDiscoveryDecisionApplier _applier = Substitute.For<IDiscoveryDecisionApplier>();
    private readonly IDiscoveryDecisionHandler _handler = Substitute.For<IDiscoveryDecisionHandler>();
    private readonly IChangeDetector _detector = Substitute.For<IChangeDetector>();
    private readonly IArchiveNameBuilder _nameBuilder = Substitute.For<IArchiveNameBuilder>();
    private readonly IArchiver _archiver = Substitute.For<IArchiver>();
    private readonly IBackupStateStore _stateStore = Substitute.For<IBackupStateStore>();
    private readonly MockFileSystem _fileSystem = new();
    private readonly FakeTimeProvider _timeProvider = new(_createdAt);
    private readonly FakeLogger<BackupOrchestrator> _logger = new();
    private readonly TelemetryCapture _capture = new();
    private readonly BackupConfig _config = new() { BackupRoot = @"C:\backup", SourcesRoot = _sourcesRoot };

    /// <summary>
    /// Sets up a valid configuration with one standalone repository whose wiki feature is disabled, the tools, a token,
    /// a free run lock, a discovery without findings, an empty state and an archiver that succeeds.
    /// </summary>
    public BackupOrchestratorTests()
    {
        _config.Repositories.Add(new() { Url = "https://github.com/owner/repo", Id = 1, Branch = "main" });
        _configStore.LoadAsync(Arg.Any<CancellationToken>()).Returns(new BackupConfigLoadResult(_config, []));
        _toolLocator.LocateAsync(Arg.Any<CancellationToken>())
            .Returns(new ToolSet(new(_gitPath, "2.46.0"), "3.5.1", new(_sevenZipPath, "24.08")));
        _tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(new GitHubToken("secret-token", "test"));
        _runLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns(_runLockHandle);
        _discovery.DiscoverAsync(Arg.Any<BackupConfig>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(DiscoveryResult.Empty);
        _stateStore.LoadAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => BackupState.CreateEmpty(call.ArgAt<long>(1), call.ArgAt<string>(2)));
        _nameBuilder.Build(Arg.Any<string>(), Arg.Any<string>(), BackupTarget.Repository, Arg.Any<string>())
            .Returns(new ArchiveName(_archiveFile, _createdAt));
        _nameBuilder.Build(Arg.Any<string>(), Arg.Any<string>(), BackupTarget.Wiki, Arg.Any<string>())
            .Returns(new ArchiveName(_wikiArchiveFile, _createdAt));
        SetupWiki(hasWiki: false);
        _archiver.CreateAsync(Arg.Any<ArchiveRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => new ArchiveResult(
                Path.Combine(call.Arg<ArchiveRequest>().DestinationFolder, call.Arg<ArchiveRequest>().FileName), 1234));
        SetupDetection(ChangeDecision.ArchiveNeeded);
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    /// <inheritdoc />
    public void Dispose() => _capture.Dispose();

    [Fact]
    public async Task RunAsync_ArchiveNeeded_ArchivesAndRecordsState()
    {
        var result = await RunAsync();

        result.Items.ShouldBe([Reached(new("owner/repo", BackupItemStatus.Archived, _archiveFile, TimeSpan.FromSeconds(5), null))]);
        result.ExitCode.ShouldBe(ExitCode.Success);
        _nameBuilder.Received(1).Build("owner", "repo", BackupTarget.Repository, _headCommit);
        await _archiver.Received(1).CreateAsync(
            new ArchiveRequest
            {
                SevenZipPath = _sevenZipPath,
                SourceFolder = @"C:\sources\owner\repo",
                TemporaryFolder = _temporaryFolder,
                DestinationFolder = @"C:\backup\owner\repo",
                FileName = _archiveFile,
            },
            CancellationToken);
        await _stateStore.Received(1).AppendAsync(
            @"C:\backup\owner\repo",
            1,
            "owner/repo",
            BackupTarget.Repository,
            new ArchiveRecord(_archiveFile, _createdAt, "main", _headCommit, 1234, _refs),
            CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_ActiveRepository_DetectsWithResolvedFoldersTokenAndLastArchive()
    {
        var last = new ArchiveRecord("old.zip", _createdAt, "main", _headCommit, 1, _refs);
        _stateStore.LoadAsync(@"C:\backup\owner\repo", 1, "owner/repo", Arg.Any<CancellationToken>())
            .Returns(BackupState.CreateEmpty(1, "owner/repo").WithArchive(BackupTarget.Repository, last, "owner/repo"));

        await RunAsync();

        await _detector.Received(1).DetectAsync(
            new ChangeDetectionRequest
            {
                Git = new() { GitPath = _gitPath, ClonePath = @"C:\sources\owner\repo", Branch = "main", Token = "secret-token" },
                RemoteUrl = "https://github.com/owner/repo.git",
                ArchiveFolder = @"C:\backup\owner\repo",
                LastArchive = last,
            },
            CancellationToken);
    }

    [Fact]
    public async Task RunAsync_AnonymousToken_PassesNoTokenToGit()
    {
        _tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(GitHubToken.Anonymous);

        var result = await RunAsync();

        result.Items.ShouldHaveSingleItem().Status.ShouldBe(BackupItemStatus.Archived);
        await _detector.Received(1).DetectAsync(
            Arg.Is<ChangeDetectionRequest>(request => request.Git.Token == null), CancellationToken);
    }

    [Theory]
    [InlineData(ChangeDecision.UpToDate)]
    [InlineData(ChangeDecision.NothingToArchive)]
    public async Task RunAsync_NoArchiveNeeded_ReportsUpToDateWithoutArchive(ChangeDecision decision)
    {
        SetupDetection(decision);

        var result = await RunAsync();

        var expected = new BackupItemResult("owner/repo", BackupItemStatus.UpToDate, null, TimeSpan.FromSeconds(5), null);
        result.Items.ShouldBe([decision == ChangeDecision.UpToDate ? Reached(expected, archived: false) : expected]);
        await _archiver.DidNotReceiveWithAnyArgs().CreateAsync(default!, CancellationToken);
        await _stateStore.DidNotReceiveWithAnyArgs().AppendAsync(default!, default, default!, default, default!, CancellationToken);
    }

    [Theory]
    [InlineData(ChangeDecision.ArchiveNeeded, _archiveFile)]
    [InlineData(ChangeDecision.UpToDate, null)]
    public async Task RunAsync_BranchFallback_ReportsWarningWithArchive(ChangeDecision decision, string? archive)
    {
        _config.Repositories[0].Branch = "feature";
        SetupDetection(decision, branchFallback: true);

        var result = await RunAsync();

        var item = result.Items.ShouldHaveSingleItem();
        item.Status.ShouldBe(BackupItemStatus.Warning);
        item.ArchiveFileName.ShouldBe(archive);
        item.Message.ShouldBe("The branch 'feature' does not exist on the server; the default branch 'main' is used.");
        result.ExitCode.ShouldBe(ExitCode.Success);
        var record = SingleRecord(_repositoryWarningEventId);
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Warning").ShouldBe(item.Message);
    }

    [Fact]
    public async Task RunAsync_DetectorFails_ReportsFailedAndProcessesNext()
    {
        AddRepository("other");
        _detector.DetectAsync(Arg.Is<ChangeDetectionRequest>(request => request.RemoteUrl.Contains("/repo.git")), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("git exited with code 128."));

        var result = await RunAsync();

        result.Items.Select(item => (item.Repository, item.Status, item.Message)).ShouldBe(
        [
            ("owner/repo", BackupItemStatus.Failed, "git exited with code 128."),
            ("owner/other", BackupItemStatus.Archived, null),
        ]);
        result.ExitCode.ShouldBe(ExitCode.RepositoriesFailed);
        var record = SingleRecord(_repositoryFailedEventId);
        record.Level.ShouldBe(LogLevel.Error);
        record.Exception.ShouldNotBeNull().Message.ShouldBe("git exited with code 128.");
    }

    [Fact]
    public async Task RunAsync_ArchiverFails_ReportsFailedWithoutRecordingState()
    {
        _archiver.CreateAsync(Arg.Any<ArchiveRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("The archive cannot be moved."));

        var result = await RunAsync();

        var item = result.Items.ShouldHaveSingleItem();
        item.Status.ShouldBe(BackupItemStatus.Failed);
        item.ArchiveFileName.ShouldBeNull();
        item.Message.ShouldBe("The archive cannot be moved.");
        await _stateStore.DidNotReceiveWithAnyArgs().AppendAsync(default!, default, default!, default, default!, CancellationToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_StateStoreFails_ReportsFailed(bool onLoad)
    {
        var exception = new BackupStateException(@"C:\backup\owner\repo\.backup-state.json", ["broken"]);
        if (onLoad)
        {
            _stateStore.LoadAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(exception);
        }
        else
        {
            _stateStore.AppendAsync(
                    Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<BackupTarget>(), Arg.Any<ArchiveRecord>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(exception);
        }

        var result = await RunAsync();

        var item = result.Items.ShouldHaveSingleItem();
        item.Status.ShouldBe(BackupItemStatus.Failed);
        item.Message.ShouldBe(exception.Message);
        result.ExitCode.ShouldBe(ExitCode.RepositoriesFailed);
    }

    [Fact]
    public async Task RunAsync_UnavailableRepository_SkipsWithoutGitOrState()
    {
        _config.Repositories[0].Status = RepositoryStatus.Unavailable;

        var result = await RunAsync();

        var item = result.Items.ShouldHaveSingleItem();
        item.Status.ShouldBe(BackupItemStatus.Skipped);
        item.Message.ShouldNotBeNullOrWhiteSpace();
        result.ExitCode.ShouldBe(ExitCode.Success);
        await _detector.DidNotReceiveWithAnyArgs().DetectAsync(default!, CancellationToken);
        await _stateStore.DidNotReceiveWithAnyArgs().LoadAsync(default!, default, default!, CancellationToken);
    }

    [Fact]
    public async Task RunAsync_NoRepositories_ReturnsSuccessAndEmptiesTemporaryFolder()
    {
        _config.Repositories.Clear();
        _fileSystem.AddFile(Path.Combine(_temporaryFolder, "left.zip"), new("partial"));

        var result = await RunAsync();

        result.Items.ShouldBeEmpty();
        result.ExitCode.ShouldBe(ExitCode.Success);
        _fileSystem.Directory.GetFileSystemEntries(_temporaryFolder).ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_InvalidConfig_ReturnsCriticalWithoutTouchingAnything()
    {
        _configStore.LoadAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new BackupConfigException(@"C:\app\backup-config.json", ["backupRoot must be set.", "Second error."]));
        _fileSystem.AddFile(Path.Combine(_temporaryFolder, "left.zip"), new("partial"));

        var result = await RunAsync();

        result.CriticalErrors.ShouldBe(["backupRoot must be set.", "Second error."]);
        result.Items.ShouldBeEmpty();
        result.ExitCode.ShouldBe(ExitCode.Critical);
        _fileSystem.File.Exists(Path.Combine(_temporaryFolder, "left.zip")).ShouldBeTrue();
        await _toolLocator.DidNotReceiveWithAnyArgs().LocateAsync(CancellationToken);
        var records = Records(_runStoppedEventId);
        records.ShouldAllBe(record => record.Level == LogLevel.Error);
        records.Select(record => record.GetStructuredStateValue("Error")).ShouldBe(["backupRoot must be set.", "Second error."]);
    }

    [Fact]
    public async Task RunAsync_ToolMissing_ReturnsCriticalWithWarningsWithoutTouchingAnything()
    {
        _configStore.LoadAsync(Arg.Any<CancellationToken>()).Returns(new BackupConfigLoadResult(_config, ["a warning"]));
        _toolLocator.LocateAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new ToolUnavailableException("git was not found."));
        _fileSystem.AddFile(Path.Combine(_temporaryFolder, "left.zip"), new("partial"));

        var result = await RunAsync();

        result.CriticalErrors.ShouldBe(["git was not found."]);
        result.ConfigWarnings.ShouldBe(["a warning"]);
        result.ExitCode.ShouldBe(ExitCode.Critical);
        _fileSystem.File.Exists(Path.Combine(_temporaryFolder, "left.zip")).ShouldBeTrue();
        await _detector.DidNotReceiveWithAnyArgs().DetectAsync(default!, CancellationToken);
        await _tokenProvider.DidNotReceiveWithAnyArgs().GetTokenAsync(CancellationToken);
    }

    [Fact]
    public async Task RunAsync_Cancelled_ReturnsCancelledResult()
    {
        AddRepository("other");
        AddRepository("third");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        _detector.DetectAsync(Arg.Any<ChangeDetectionRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(3));
            cancellation.Cancel();
            return Task.FromException<ChangeDetection>(new OperationCanceledException(cancellation.Token));
        });

        var result = await CreateOrchestrator().RunAsync(Request(), cancellation.Token);

        result.Items.ShouldBe(
        [
            new("owner/repo", BackupItemStatus.Cancelled, null, TimeSpan.FromSeconds(3), BackupOrchestrator.CancelledMessage),
            new("owner/other", BackupItemStatus.Skipped, null, TimeSpan.Zero, BackupOrchestrator.NotProcessedMessage),
            new("owner/third", BackupItemStatus.Skipped, null, TimeSpan.Zero, BackupOrchestrator.NotProcessedMessage),
        ]);
        result.IsCancelled.ShouldBeTrue();
        result.CriticalErrors.ShouldBeEmpty();
        result.ExitCode.ShouldBe(ExitCode.Critical);
        await _detector.ReceivedWithAnyArgs(1).DetectAsync(default!, CancellationToken);
        var record = SingleRecord(_runCancelledEventId);
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Processed").ShouldBe("0");
        record.GetStructuredStateValue("NotProcessed").ShouldBe("2");
    }

    [Fact]
    public async Task RunAsync_CancelledBetweenRepositories_ReportsFinishedAndSkipsRest()
    {
        AddRepository("other");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        _detector.DetectAsync(Arg.Any<ChangeDetectionRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cancellation.Cancel();
            return Detection(ChangeDecision.UpToDate, branchFallback: false);
        });

        var result = await CreateOrchestrator().RunAsync(Request(), cancellation.Token);

        result.Items.Select(item => (item.Repository, item.Status)).ShouldBe(
        [
            ("owner/repo", BackupItemStatus.UpToDate),
            ("owner/other", BackupItemStatus.Skipped),
        ]);
        result.IsCancelled.ShouldBeTrue();
        result.ExitCode.ShouldBe(ExitCode.Critical);
        await _detector.ReceivedWithAnyArgs(1).DetectAsync(default!, CancellationToken);
    }

    [Fact]
    public async Task RunAsync_CancelledAfterArchiveMoved_StillRecordsState()
    {
        AddRepository("other");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        _archiver.CreateAsync(Arg.Any<ArchiveRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            cancellation.Cancel();
            return new ArchiveResult(Path.Combine(call.Arg<ArchiveRequest>().DestinationFolder, call.Arg<ArchiveRequest>().FileName), 1234);
        });

        var result = await CreateOrchestrator().RunAsync(Request(), cancellation.Token);

        result.Items.Select(item => (item.Repository, item.Status, item.ArchiveFileName)).ShouldBe(
        [
            ("owner/repo", BackupItemStatus.Archived, _archiveFile),
            ("owner/other", BackupItemStatus.Skipped, null),
        ]);
        result.IsCancelled.ShouldBeTrue();
        await _stateStore.Received(1).AppendAsync(
            @"C:\backup\owner\repo",
            1,
            "owner/repo",
            BackupTarget.Repository,
            Arg.Any<ArchiveRecord>(),
            CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_LockHeld_ReturnsCriticalWithoutTemporaryFolderCleanupOrDiscovery()
    {
        _runLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns((IDisposable?)null);
        _fileSystem.AddFile(Path.Combine(_temporaryFolder, "left.zip"), new("partial"));

        var result = await RunAsync();

        result.CriticalErrors.ShouldBe([BackupOrchestrator.RunLockHeldMessage]);
        result.Items.ShouldBeEmpty();
        result.ExitCode.ShouldBe(ExitCode.Critical);
        _fileSystem.File.Exists(Path.Combine(_temporaryFolder, "left.zip")).ShouldBeTrue();
        await _discovery.DidNotReceiveWithAnyArgs().DiscoverAsync(default!, default, CancellationToken);
        await _detector.DidNotReceiveWithAnyArgs().DetectAsync(default!, CancellationToken);
        SingleRecord(_runStoppedEventId).GetStructuredStateValue("Error").ShouldBe(BackupOrchestrator.RunLockHeldMessage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_LockTakenAfterTokenAndBeforeTemporaryFolderCleanup(bool dryRun)
    {
        _fileSystem.AddFile(Path.Combine(_temporaryFolder, "left.zip"), new("partial"));
        var leftoverAtLock = false;
        _runLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            leftoverAtLock = _fileSystem.File.Exists(Path.Combine(_temporaryFolder, "left.zip"));
            return _runLockHandle;
        });

        await CreateOrchestrator().RunAsync(Request() with { DryRun = dryRun }, CancellationToken);

        leftoverAtLock.ShouldBeTrue();
        Received.InOrder(() =>
        {
            _ = _tokenProvider.GetTokenAsync(CancellationToken);
            _ = _runLock.TryAcquireAsync(CancellationToken);
            _ = _discovery.DiscoverAsync(_config, null, CancellationToken);
        });
    }

    [Theory]
    [InlineData("success")]
    [InlineData("stopped")]
    [InlineData("cancelled")]
    public async Task RunAsync_AnyOutcome_ReleasesLock(string outcome)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        if (outcome == "stopped")
        {
            _discovery.DiscoverAsync(Arg.Any<BackupConfig>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(DiscoveryFailure("server"));
        }
        else if (outcome == "cancelled")
        {
            _detector.DetectAsync(Arg.Any<ChangeDetectionRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                cancellation.Cancel();
                return Task.FromException<ChangeDetection>(new OperationCanceledException(cancellation.Token));
            });
        }

        await CreateOrchestrator().RunAsync(Request(), cancellation.Token);

        _runLockHandle.Received(1).Dispose();
    }

    [Fact]
    public async Task RunAsync_UnexpectedException_ReleasesLock()
    {
        _discovery.DiscoverAsync(Arg.Any<BackupConfig>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Unexpected."));

        await Should.ThrowAsync<InvalidOperationException>(RunAsync());

        _runLockHandle.Received(1).Dispose();
    }

    [Fact]
    public async Task RunAsync_ToolMissing_DoesNotTakeLock()
    {
        _toolLocator.LocateAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new ToolUnavailableException("git was not found."));

        await RunAsync();

        await _runLock.DidNotReceiveWithAnyArgs().TryAcquireAsync(CancellationToken);
    }

    [Fact]
    public async Task RunAsync_OperationCanceledWithoutCancellation_ReportsFailed()
    {
        _detector.DetectAsync(Arg.Any<ChangeDetectionRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("The request timed out."));

        var result = await RunAsync();

        result.Items.ShouldHaveSingleItem().Status.ShouldBe(BackupItemStatus.Failed);
    }

    [Fact]
    public async Task RunAsync_LeftoversInTemporaryFolder_DeletedBeforeFirstRepository()
    {
        _fileSystem.AddFile(Path.Combine(_temporaryFolder, "a.zip"), new("partial"));
        _fileSystem.AddFile(Path.Combine(_temporaryFolder, "nested", "b.zip"), new("partial"));
        string[]? entriesAtDetection = null;
        _detector.DetectAsync(Arg.Any<ChangeDetectionRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            entriesAtDetection = _fileSystem.Directory.GetFileSystemEntries(_temporaryFolder);
            return Detection(ChangeDecision.UpToDate, branchFallback: false);
        });

        await RunAsync();

        entriesAtDetection.ShouldNotBeNull().ShouldBeEmpty();
        Records(_temporaryEntryDeletedEventId).Count.ShouldBe(2);
    }

    [Fact]
    public async Task RunAsync_NoTemporaryFolder_ProcessesRepositories()
    {
        var result = await RunAsync();

        result.Items.ShouldHaveSingleItem().Status.ShouldBe(BackupItemStatus.Archived);
        _fileSystem.Directory.Exists(_temporaryFolder).ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_LeftoverCannotBeDeleted_LogsWarningAndContinues()
    {
        var locked = Path.Combine(_temporaryFolder, "locked.zip");
        _fileSystem.AddFile(locked, new("partial") { AllowedFileShare = FileShare.None });

        var result = await RunAsync();

        result.Items.ShouldHaveSingleItem().Status.ShouldBe(BackupItemStatus.Archived);
        _fileSystem.File.Exists(locked).ShouldBeTrue();
        var record = SingleRecord(_temporaryEntryNotDeletedEventId);
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(locked);
    }

    [Fact]
    public async Task RunAsync_TemporaryFolderIsLink_LeavesItsContentAndContinues()
    {
        var leftover = Path.Combine(_temporaryFolder, "a.zip");
        _fileSystem.AddFile(leftover, new("partial"));
        _fileSystem.DirectoryInfo.New(_temporaryFolder).Attributes |= FileAttributes.ReparsePoint;

        var result = await RunAsync();

        result.Items.ShouldHaveSingleItem().Status.ShouldBe(BackupItemStatus.Archived);
        _fileSystem.File.Exists(leftover).ShouldBeTrue();
        var record = SingleRecord(_temporaryFolderIsLinkEventId);
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Path").ShouldBe(_temporaryFolder);
    }

    [Fact]
    public async Task RunAsync_AccountAndStandaloneRepositories_ProcessesInFileOrder()
    {
        _config.Accounts.Add(new()
        {
            Url = "https://github.com/octocat",
            Repositories = [new() { Name = "second", Id = 11 }, new() { Name = "first", Id = 10 }],
        });

        var result = await RunAsync();

        result.Items.Select(item => item.Repository).ShouldBe(["octocat/second", "octocat/first", "owner/repo"]);
    }

    [Fact]
    public async Task RunAsync_ListedRepositoryMatchesExclusion_IsSkippedWithWarningAndOthersAreBackedUp()
    {
        AddRepository("other-archive");
        _config.Exclude.Add("owner/*-archive");

        var result = await RunAsync();

        result.Items.Select(item => (item.Repository, item.Status)).ShouldBe([("owner/repo", BackupItemStatus.Archived)]);
        result.ExitCode.ShouldBe(ExitCode.Success);
        var record = SingleRecord(_repositoryExcludedEventId);
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Repository").ShouldBe("owner/other-archive");
        record.GetStructuredStateValue("Mask").ShouldBe("owner/*-archive");
        record.GetStructuredStateValue("Scope").ShouldBe(nameof(ExclusionScope.Global));
        await _detector.ReceivedWithAnyArgs(1).DetectAsync(default!, CancellationToken);
    }

    [Fact]
    public async Task RunAsync_AccountMaskMatchesAccountRepository_IsExcluded()
    {
        _config.Accounts.Add(new()
        {
            Url = "https://github.com/octocat",
            Exclude = ["sec*"],
            Repositories = [new() { Name = "secret", Id = 11 }, new() { Name = "open", Id = 10 }],
        });

        var result = await RunAsync();

        result.Items.Select(item => item.Repository).ShouldBe(["octocat/open", "owner/repo"]);
        var record = SingleRecord(_repositoryExcludedEventId);
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Repository").ShouldBe("octocat/secret");
        record.GetStructuredStateValue("Mask").ShouldBe("sec*");
        record.GetStructuredStateValue("Scope").ShouldBe(nameof(ExclusionScope.Account));
    }

    [Fact]
    public async Task RunAsync_ConfigWarnings_ReturnedWithResult()
    {
        _configStore.LoadAsync(Arg.Any<CancellationToken>()).Returns(new BackupConfigLoadResult(_config, ["w1", "w2"]));

        var result = await RunAsync();

        result.ConfigWarnings.ShouldBe(["w1", "w2"]);
        result.CriticalErrors.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_WikiEnabledAndChanged_ArchivesWikiSeparately()
    {
        SetupWiki(hasWiki: true);

        var result = await RunAsync();

        result.Items.ShouldBe(
        [
            Reached(new("owner/repo", BackupItemStatus.Archived, _archiveFile, TimeSpan.FromSeconds(5), null)),
            Reached(new("owner/repo.wiki", BackupItemStatus.Archived, _wikiArchiveFile, TimeSpan.FromSeconds(5), null)
            {
                Target = BackupTarget.Wiki,
            }),
        ]);
        await _detector.Received(1).DetectAsync(
            new ChangeDetectionRequest
            {
                Git = new() { GitPath = _gitPath, ClonePath = @"C:\sources\owner\repo.wiki", Branch = null, Token = "secret-token" },
                RemoteUrl = _wikiUrl,
                ArchiveFolder = @"C:\backup\owner\repo",
                LastArchive = null,
            },
            CancellationToken);
        _nameBuilder.Received(1).Build("owner", "repo", BackupTarget.Wiki, _headCommit);
        await _archiver.Received(1).CreateAsync(
            new ArchiveRequest
            {
                SevenZipPath = _sevenZipPath,
                SourceFolder = @"C:\sources\owner\repo.wiki",
                TemporaryFolder = _temporaryFolder,
                DestinationFolder = @"C:\backup\owner\repo",
                FileName = _wikiArchiveFile,
            },
            CancellationToken);
        await _stateStore.Received(1).AppendAsync(
            @"C:\backup\owner\repo",
            1,
            "owner/repo",
            BackupTarget.Wiki,
            new ArchiveRecord(_wikiArchiveFile, _createdAt, "main", _headCommit, 1234, _refs),
            CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_WikiDisabled_ProcessesRepositoryOnly()
    {
        var result = await RunAsync();

        result.Items.ShouldHaveSingleItem().Repository.ShouldBe("owner/repo");
        await _gitHubApi.Received(1).GetRepositoryAsync("owner", "repo", CancellationToken);
        await _detector.Received(1).DetectAsync(Arg.Any<ChangeDetectionRequest>(), CancellationToken);
        await _detector.DidNotReceive().DetectAsync(
            Arg.Is<ChangeDetectionRequest>(request => request.RemoteUrl == _wikiUrl), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WikiNotFound_SkipsWithoutResultAndLogs()
    {
        SetupWiki(hasWiki: true);
        _detector.DetectAsync(WikiRequest(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new GitRepositoryNotFoundException("'git ls-remote' failed: Repository not found.", "git ls-remote", 128));

        var result = await RunAsync();

        result.Items.ShouldBe([Reached(new("owner/repo", BackupItemStatus.Archived, _archiveFile, TimeSpan.FromSeconds(5), null))]);
        result.ExitCode.ShouldBe(ExitCode.Success);
        SingleRecord(_wikiNotFoundEventId).Level.ShouldBe(LogLevel.Information);
        _logger.Collector.GetSnapshot().ShouldNotContain(record => record.Level == LogLevel.Error);
        await _stateStore.DidNotReceive().AppendAsync(
            Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), BackupTarget.Wiki, Arg.Any<ArchiveRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WikiFails_ReportsWikiFailedAndKeepsRepositoryArchived()
    {
        SetupWiki(hasWiki: true);
        _detector.DetectAsync(WikiRequest(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new GitCommandException("'git fetch' failed with exit code 128: timeout", "git fetch", 128));

        var result = await RunAsync();

        result.Items.Select(item => (item.Repository, item.Status, item.Target, item.Message)).ShouldBe(
        [
            ("owner/repo", BackupItemStatus.Archived, BackupTarget.Repository, null),
            ("owner/repo.wiki", BackupItemStatus.Failed, BackupTarget.Wiki, "'git fetch' failed with exit code 128: timeout"),
        ]);
        result.ExitCode.ShouldBe(ExitCode.RepositoriesFailed);
    }

    [Fact]
    public async Task RunAsync_RepositoryFails_StillProcessesWiki()
    {
        SetupWiki(hasWiki: true);
        _detector.DetectAsync(Arg.Is<ChangeDetectionRequest>(request => request.RemoteUrl == _repositoryUrl), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("git exited with code 128."));

        var result = await RunAsync();

        result.Items.Select(item => (item.Repository, item.Status, item.ArchiveFileName)).ShouldBe(
        [
            ("owner/repo", BackupItemStatus.Failed, null),
            ("owner/repo.wiki", BackupItemStatus.Archived, _wikiArchiveFile),
        ]);
    }

    [Fact]
    public async Task RunAsync_WikiUpToDate_UsesWikiLastArchive()
    {
        SetupWiki(hasWiki: true);
        var repositoryArchive = new ArchiveRecord("repo.zip", _createdAt, "main", _headCommit, 1, _refs);
        var wikiArchive = new ArchiveRecord("wiki.zip", _createdAt, "master", _headCommit, 1, _refs);
        _stateStore.LoadAsync(@"C:\backup\owner\repo", 1, "owner/repo", Arg.Any<CancellationToken>())
            .Returns(BackupState.CreateEmpty(1, "owner/repo")
                .WithArchive(BackupTarget.Repository, repositoryArchive, "owner/repo")
                .WithArchive(BackupTarget.Wiki, wikiArchive, "owner/repo"));
        SetupDetection(ChangeDecision.UpToDate);

        var result = await RunAsync();

        result.Items.Select(item => (item.Repository, item.Status)).ShouldBe(
        [
            ("owner/repo", BackupItemStatus.UpToDate),
            ("owner/repo.wiki", BackupItemStatus.UpToDate),
        ]);
        await _detector.Received(1).DetectAsync(
            Arg.Is<ChangeDetectionRequest>(request => request.RemoteUrl == _wikiUrl && request.LastArchive == wikiArchive),
            CancellationToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_RepositoryUnavailableOrExcluded_DoesNotQueryWiki(bool unavailable)
    {
        SetupWiki(hasWiki: true);
        if (unavailable)
        {
            _config.Repositories[0].Status = RepositoryStatus.Unavailable;
        }
        else
        {
            _config.Exclude.Add("owner/repo");
        }

        var result = await RunAsync();

        result.Items.ShouldNotContain(item => item.Target == BackupTarget.Wiki);
        await _gitHubApi.DidNotReceiveWithAnyArgs().GetRepositoryAsync(default!, default!, CancellationToken);
        await _detector.DidNotReceiveWithAnyArgs().DetectAsync(default!, CancellationToken);
    }

    [Fact]
    public async Task RunAsync_WikiLookupFails_ReportsWikiFailed()
    {
        _gitHubApi.GetRepositoryAsync("owner", "repo", Arg.Any<CancellationToken>())
            .ThrowsAsync(new GitHubApiException("The GitHub API answered 502.", "repos/owner/repo", 502));

        var result = await RunAsync();

        result.Items.Select(item => (item.Repository, item.Status, item.Target, item.Message)).ShouldBe(
        [
            ("owner/repo", BackupItemStatus.Archived, BackupTarget.Repository, null),
            ("owner/repo.wiki", BackupItemStatus.Failed, BackupTarget.Wiki, "The GitHub API answered 502."),
        ]);
        result.ExitCode.ShouldBe(ExitCode.RepositoriesFailed);
        SingleRecord(_wikiLookupFailedEventId).Level.ShouldBe(LogLevel.Error);
        await _detector.Received(1).DetectAsync(Arg.Any<ChangeDetectionRequest>(), CancellationToken);
    }

    [Fact]
    public async Task RunAsync_RepositoryNotFoundForRepository_ReportsFailed()
    {
        _detector.DetectAsync(Arg.Any<ChangeDetectionRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new GitRepositoryNotFoundException("'git clone' failed: Repository not found.", "git clone", 128));

        var result = await RunAsync();

        var item = result.Items.ShouldHaveSingleItem();
        item.Status.ShouldBe(BackupItemStatus.Failed);
        item.Message.ShouldBe("'git clone' failed: Repository not found.");
        result.ExitCode.ShouldBe(ExitCode.RepositoriesFailed);
    }

    [Fact]
    public async Task RunAsync_CancelledDuringWiki_ReturnsCancelledResult()
    {
        SetupWiki(hasWiki: true);
        AddRepository("other");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        _detector.DetectAsync(WikiRequest(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cancellation.Cancel();
            return Task.FromException<ChangeDetection>(new OperationCanceledException(cancellation.Token));
        });

        var result = await CreateOrchestrator().RunAsync(Request(), cancellation.Token);

        result.Items.Select(item => (item.Repository, item.Status, item.Target)).ShouldBe(
        [
            ("owner/repo", BackupItemStatus.Archived, BackupTarget.Repository),
            ("owner/repo.wiki", BackupItemStatus.Cancelled, BackupTarget.Wiki),
            ("owner/other", BackupItemStatus.Skipped, BackupTarget.Repository),
        ]);
        result.IsCancelled.ShouldBeTrue();
        await _gitHubApi.DidNotReceive().GetRepositoryAsync("owner", "other", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_NoFilter_DiscoversAllAccountsAndAppliesWithHandlerAndGitPath()
    {
        var discovery = SetupApply(Applied(markedUnavailable: ["owner/repo"]));

        var result = await RunAsync();

        await _discovery.Received(1).DiscoverAsync(_config, null, CancellationToken);
        await _applier.Received(1).ApplyAsync(_config, discovery, _handler, _gitPath, CancellationToken);
        result.Discovery.ShouldNotBeNull().MarkedUnavailable.ShouldBe(["owner/repo"]);
    }

    [Fact]
    public async Task RunAsync_NoDiscoveryChanges_DoesNotApplyAndReturnsSuccess()
    {
        var result = await RunAsync();

        result.Discovery.ShouldBeNull();
        result.ExitCode.ShouldBe(ExitCode.Success);
        await _applier.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default!, default!, default!, CancellationToken);
        await _configStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, CancellationToken);
    }

    [Fact]
    public async Task RunAsync_NewRepositoryAddedByApplier_IsBackedUpInSameRun()
    {
        SetupApply(
            Applied(added: ["owner/fresh"]),
            config => config.Repositories.Add(new() { Url = "https://github.com/owner/fresh", Id = 5 }));

        var result = await RunAsync();

        result.Items.Select(item => (item.Repository, item.Status)).ShouldBe(
        [
            ("owner/repo", BackupItemStatus.Archived),
            ("owner/fresh", BackupItemStatus.Archived),
        ]);
        result.ExitCode.ShouldBe(ExitCode.DiscoveryEvents);
        await _detector.Received(1).DetectAsync(
            Arg.Is<ChangeDetectionRequest>(request => request.RemoteUrl == "https://github.com/owner/fresh.git"), CancellationToken);
    }

    [Fact]
    public async Task RunAsync_EntryMarkedUnavailableByApplier_ReportsSkippedWithoutGit()
    {
        SetupApply(
            Applied(markedUnavailable: ["owner/repo"]),
            config => config.Repositories[0].Status = RepositoryStatus.Unavailable);

        var result = await RunAsync();

        result.Items.ShouldHaveSingleItem().Status.ShouldBe(BackupItemStatus.Skipped);
        result.ExitCode.ShouldBe(ExitCode.DiscoveryEvents);
        await _detector.DidNotReceiveWithAnyArgs().DetectAsync(default!, CancellationToken);
        await _stateStore.DidNotReceiveWithAnyArgs().LoadAsync(default!, default, default!, CancellationToken);
    }

    [Fact]
    public async Task RunAsync_NewRepositoryNotConfirmed_ReportsSkippedWithoutProcessing()
    {
        SetupApply(Applied(skipped: ["owner/fresh"]));

        var result = await RunAsync();

        result.Items.Select(item => (item.Repository, item.Status)).ShouldBe(
        [
            ("owner/repo", BackupItemStatus.Archived),
            ("owner/fresh", BackupItemStatus.Skipped),
        ]);
        result.Items[1].Message.ShouldNotBeNullOrWhiteSpace();
        result.ExitCode.ShouldBe(ExitCode.DiscoveryEvents);
        await _detector.Received(1).DetectAsync(Arg.Any<ChangeDetectionRequest>(), CancellationToken);
        await _stateStore.DidNotReceive().LoadAsync(Arg.Any<string>(), Arg.Any<long>(), "owner/fresh", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_RepositoryFilter_SkipsDiscoveryAndProcessesOnlyThatRepository()
    {
        AddRepository("other");

        var result = await RunAsync(BackupRunFilter.ForRepository("OWNER", "Other"));

        result.Items.Select(item => item.Repository).ShouldBe(["owner/other"]);
        await _discovery.DidNotReceiveWithAnyArgs().DiscoverAsync(default!, default, CancellationToken);
        await _applier.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default!, default!, default!, CancellationToken);
        SingleRecord(_discoverySkippedEventId).GetStructuredStateValue("Repository").ShouldBe("OWNER/Other");
    }

    [Fact]
    public async Task RunAsync_RepositoryFilterNotInConfig_ReturnsCritical()
    {
        var result = await RunAsync(BackupRunFilter.ForRepository("owner", "absent"));

        result.CriticalErrors.ShouldBe(["The repository 'owner/absent' is not in backup-config.json."]);
        result.ExitCode.ShouldBe(ExitCode.Critical);
        await _discovery.DidNotReceiveWithAnyArgs().DiscoverAsync(default!, default, CancellationToken);
        await _tokenProvider.DidNotReceiveWithAnyArgs().GetTokenAsync(CancellationToken);
        await _detector.DidNotReceiveWithAnyArgs().DetectAsync(default!, CancellationToken);
    }

    [Fact]
    public async Task RunAsync_RepositoryFilterMatchesExclusion_ProcessesNothing()
    {
        _config.Exclude.Add("owner/repo");

        var result = await RunAsync(BackupRunFilter.ForRepository("owner", "repo"));

        result.Items.ShouldBeEmpty();
        result.ExitCode.ShouldBe(ExitCode.Success);
        await _detector.DidNotReceiveWithAnyArgs().DetectAsync(default!, CancellationToken);
    }

    [Fact]
    public async Task RunAsync_AccountFilter_DiscoversThatAccountAndProcessesOnlyItsRepositories()
    {
        _config.Accounts.Add(new() { Url = "https://github.com/octocat", Repositories = [new() { Name = "listed", Id = 10 }] });
        _config.Accounts.Add(new() { Url = "https://github.com/other", Repositories = [new() { Name = "foreign", Id = 11 }] });
        _config.Repositories.Add(new() { Url = "https://github.com/octocat/standalone", Id = 12 });

        var result = await RunAsync(BackupRunFilter.ForAccount("OctoCat"));

        result.Items.Select(item => item.Repository).ShouldBe(["octocat/listed"]);
        await _discovery.Received(1).DiscoverAsync(_config, "OctoCat", CancellationToken);
    }

    [Fact]
    public async Task RunAsync_AccountNotTracked_ReturnsCritical()
    {
        _discovery.DiscoverAsync(Arg.Any<BackupConfig>(), "ghost", Arg.Any<CancellationToken>())
            .ThrowsAsync(new ArgumentException("The account 'ghost' is not tracked.", "accountLogin"));

        var result = await RunAsync(BackupRunFilter.ForAccount("ghost"));

        result.CriticalErrors.ShouldHaveSingleItem().ShouldStartWith("The account 'ghost' is not tracked.");
        result.Items.ShouldBeEmpty();
        result.ExitCode.ShouldBe(ExitCode.Critical);
        await _detector.DidNotReceiveWithAnyArgs().DetectAsync(default!, CancellationToken);
    }

    [Theory]
    [InlineData("rate-limit")]
    [InlineData("unauthorized")]
    [InlineData("server")]
    public async Task RunAsync_DiscoveryFails_ReturnsCriticalWithoutApplyingOrProcessing(string kind)
    {
        var failure = DiscoveryFailure(kind);
        _discovery.DiscoverAsync(Arg.Any<BackupConfig>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).ThrowsAsync(failure);

        var result = await RunAsync();

        result.CriticalErrors.ShouldBe([failure.Message]);
        result.Items.ShouldBeEmpty();
        result.ExitCode.ShouldBe(ExitCode.Critical);
        await _applier.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default!, default!, default!, CancellationToken);
        await _detector.DidNotReceiveWithAnyArgs().DetectAsync(default!, CancellationToken);
        var record = SingleRecord(_runStoppedEventId);
        record.Level.ShouldBe(LogLevel.Error);
        record.GetStructuredStateValue("Error").ShouldBe(failure.Message);
    }

    [Theory]
    [InlineData("invalid", "repositories[1].id is duplicated.")]
    [InlineData("io", "The configuration file cannot be written.")]
    [InlineData("access", "Access to the configuration file is denied.")]
    public async Task RunAsync_ApplyFails_ReturnsCriticalWithoutProcessing(string kind, string expected)
    {
        SetupApply(Applied());
        _applier.ApplyAsync(
                Arg.Any<BackupConfig>(),
                Arg.Any<DiscoveryResult>(),
                Arg.Any<IDiscoveryDecisionHandler>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(ApplyFailure(kind));

        var result = await RunAsync();

        result.CriticalErrors.ShouldBe([expected]);
        result.Items.ShouldBeEmpty();
        result.ExitCode.ShouldBe(ExitCode.Critical);
        await _detector.DidNotReceiveWithAnyArgs().DetectAsync(default!, CancellationToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_CancelledDuringDiscoveryOrDecision_ReturnsCancelledResultWithoutProcessing(bool duringDiscovery)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        SetupApply(Applied());
        Task<DiscoveryResult> CancelDiscovery()
        {
            cancellation.Cancel();
            return Task.FromException<DiscoveryResult>(new OperationCanceledException(cancellation.Token));
        }

        Task<DiscoveryApplyResult> CancelDecision()
        {
            cancellation.Cancel();
            return Task.FromException<DiscoveryApplyResult>(new OperationCanceledException(cancellation.Token));
        }

        if (duringDiscovery)
        {
            _discovery.DiscoverAsync(Arg.Any<BackupConfig>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(_ => CancelDiscovery());
        }
        else
        {
            _applier.ApplyAsync(
                    Arg.Any<BackupConfig>(),
                    Arg.Any<DiscoveryResult>(),
                    Arg.Any<IDiscoveryDecisionHandler>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ => CancelDecision());
        }

        var result = await CreateOrchestrator().RunAsync(Request(), cancellation.Token);

        result.Items.ShouldBeEmpty();
        result.IsCancelled.ShouldBeTrue();
        result.CriticalErrors.ShouldBeEmpty();
        result.ExitCode.ShouldBe(ExitCode.Critical);
        _runLockHandle.Received(1).Dispose();
        await _detector.DidNotReceiveWithAnyArgs().DetectAsync(default!, CancellationToken);
        await _stateStore.DidNotReceiveWithAnyArgs().AppendAsync(default!, default, default!, default, default!, CancellationToken);
        await _configStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, CancellationToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_InvalidConfigOrToolMissing_DoesNotDiscover(bool invalidConfig)
    {
        if (invalidConfig)
        {
            _configStore.LoadAsync(Arg.Any<CancellationToken>())
                .ThrowsAsync(new BackupConfigException(@"C:\app\backup-config.json", ["backupRoot must be set."]));
        }
        else
        {
            _toolLocator.LocateAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new ToolUnavailableException("git was not found."));
        }

        var result = await RunAsync();

        result.ExitCode.ShouldBe(ExitCode.Critical);
        await _discovery.DidNotReceiveWithAnyArgs().DiscoverAsync(default!, default, CancellationToken);
    }

    [Fact]
    public async Task RunAsync_TemporaryFolderEmptiedBeforeDiscovery()
    {
        _fileSystem.AddFile(Path.Combine(_temporaryFolder, "left.zip"), new("partial"));
        string[]? entriesAtDiscovery = null;
        _discovery.DiscoverAsync(Arg.Any<BackupConfig>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            entriesAtDiscovery = _fileSystem.Directory.GetFileSystemEntries(_temporaryFolder);
            return DiscoveryResult.Empty;
        });

        await RunAsync();

        entriesAtDiscovery.ShouldNotBeNull().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("added", false, ExitCode.DiscoveryEvents)]
    [InlineData("skipped", false, ExitCode.DiscoveryEvents)]
    [InlineData("excluded", false, ExitCode.DiscoveryEvents)]
    [InlineData("unavailable", false, ExitCode.DiscoveryEvents)]
    [InlineData("renamed", false, ExitCode.DiscoveryEvents)]
    [InlineData("reactivated", false, ExitCode.Success)]
    [InlineData("added", true, ExitCode.RepositoriesFailed)]
    public async Task RunAsync_DiscoveryEvents_ExitCode(string kind, bool repositoryFails, ExitCode expected)
    {
        SetupApply(kind switch
        {
            "added" => Applied(added: ["owner/new"]),
            "skipped" => Applied(skipped: ["owner/new"]),
            "excluded" => Applied(excluded: ["owner/new"]),
            "unavailable" => Applied(markedUnavailable: ["owner/gone"]),
            "renamed" => Applied(renamed:
            [
                new("owner/old", "owner/repo", RenameDecision.KeepArchiveFolder, RenameDecision.KeepArchiveFolder, CloneRelocationResult.Moved, CloneRelocationResult.NotPresent),
            ]),
            _ => Applied(reactivated: ["owner/repo"]),
        });
        if (repositoryFails)
        {
            _detector.DetectAsync(Arg.Any<ChangeDetectionRequest>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("git exited with code 128."));
        }

        var result = await RunAsync();

        result.ExitCode.ShouldBe(expected);
    }

    [Fact]
    public async Task RunAsync_DiscoveredRepository_WikiFlagFromDiscoveryWithoutApiCall()
    {
        SetupDiscovery(DiscoveryResult.Empty with
        {
            Repositories = new Dictionary<long, GitHubRepository> { [1] = Repository(1, "owner", "repo", hasWiki: true) },
        });

        var result = await RunAsync();

        result.Items.Select(item => (item.Repository, item.Status)).ShouldBe(
        [
            ("owner/repo", BackupItemStatus.Archived),
            ("owner/repo.wiki", BackupItemStatus.Archived),
        ]);
        await _gitHubApi.DidNotReceiveWithAnyArgs().GetRepositoryAsync(default!, default!, CancellationToken);
    }

    [Fact]
    public async Task RunAsync_RepositoryFilter_WikiFlagFromApi()
    {
        SetupWiki(hasWiki: true);

        var result = await RunAsync(BackupRunFilter.ForRepository("owner", "repo"));

        result.Items.Select(item => item.Repository).ShouldBe(["owner/repo", "owner/repo.wiki"]);
        await _gitHubApi.Received(1).GetRepositoryAsync("owner", "repo", CancellationToken);
    }

    [Fact]
    public async Task RunAsync_DryRun_DoesNotApplyDetectArchiveRecordOrSave()
    {
        var discovery = new DiscoveryResult(
            [new("owner", Repository(5, "owner", "fresh"))], [], [], [], []);
        SetupDiscovery(discovery);
        SetupPreview(ChangeDecision.ArchiveNeeded);

        var result = await DryRunAsync();

        result.IsDryRun.ShouldBeTrue();
        result.Discovery.ShouldBeNull();
        result.PlannedDiscovery.ShouldBe(discovery);
        result.ExitCode.ShouldBe(ExitCode.DiscoveryEvents);
        await _applier.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default!, default!, default!, CancellationToken);
        await _detector.DidNotReceiveWithAnyArgs().DetectAsync(default!, CancellationToken);
        await _archiver.DidNotReceiveWithAnyArgs().CreateAsync(default!, CancellationToken);
        await _stateStore.DidNotReceiveWithAnyArgs().AppendAsync(default!, default, default!, default, default!, CancellationToken);
        await _configStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, CancellationToken);
        _handler.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_DryRunNoDiscoveryFindings_HasNoPlannedDiscoveryAndSucceeds()
    {
        SetupPreview(ChangeDecision.UpToDate);

        var result = await DryRunAsync();

        result.PlannedDiscovery.ShouldBeNull();
        result.ExitCode.ShouldBe(ExitCode.Success);
    }

    [Fact]
    public async Task RunAsync_DryRun_LeavesTemporaryFolderUntouched()
    {
        var leftover = Path.Combine(_temporaryFolder, "a.zip");
        _fileSystem.AddFile(leftover, new("partial"));
        SetupPreview(ChangeDecision.UpToDate);

        await DryRunAsync();

        _fileSystem.File.ReadAllText(leftover).ShouldBe("partial");
        Records(_temporaryEntryDeletedEventId).ShouldBeEmpty();
        var record = SingleRecord(_temporaryFolderLeftUntouchedEventId);
        record.Level.ShouldBe(LogLevel.Information);
        record.GetStructuredStateValue("Path").ShouldBe(_temporaryFolder);
        SingleRecord(_dryRunStartedEventId).Level.ShouldBe(LogLevel.Information);
    }

    [Fact]
    public async Task RunAsync_DryRunMissingAndReturned_SkipsMissingAndProcessesReturned()
    {
        AddRepository("other");
        _config.Repositories[1].Status = RepositoryStatus.Unavailable;
        SetupWiki(hasWiki: true);
        SetupDiscovery(new(
            [],
            [],
            [new("owner", "repo", 1, RepositoryStatus.Active, null)],
            [new(new("owner", "other", 2, RepositoryStatus.Unavailable, null), Repository(2, "owner", "other"))],
            []));
        SetupPreview(ChangeDecision.ArchiveNeeded);

        var result = await DryRunAsync();

        result.Items.Select(item => (item.Repository, item.Status, item.Message)).ShouldBe(
        [
            ("owner/repo", BackupItemStatus.Skipped, "The repository is missing on GitHub; a backup run marks it Unavailable."),
            ("owner/other", BackupItemStatus.WouldArchive, null),
            ("owner/other.wiki", BackupItemStatus.WouldArchive, null),
        ]);
        await _detector.DidNotReceive().PreviewAsync(
            Arg.Is<ChangeDetectionRequest>(request => request.RemoteUrl.Contains("/repo.")), Arg.Any<CancellationToken>());
        SingleRecord(_repositoryMissingEventId).Level.ShouldBe(LogLevel.Information);
        result.ExitCode.ShouldBe(ExitCode.DiscoveryEvents);
    }

    [Fact]
    public async Task RunAsync_DryRunArchiveNeeded_ReportsWouldArchiveWithFutureName()
    {
        SetupPreview(ChangeDecision.ArchiveNeeded);

        var result = await DryRunAsync();

        result.Items.ShouldBe([Reached(new("owner/repo", BackupItemStatus.WouldArchive, _archiveFile, TimeSpan.FromSeconds(5), null))]);
        result.ExitCode.ShouldBe(ExitCode.Success);
        _nameBuilder.Received(1).Build("owner", "repo", BackupTarget.Repository, _headCommit);
        await _detector.Received(1).PreviewAsync(
            new ChangeDetectionRequest
            {
                Git = new() { GitPath = _gitPath, ClonePath = @"C:\sources\owner\repo", Branch = "main", Token = "secret-token" },
                RemoteUrl = _repositoryUrl,
                ArchiveFolder = @"C:\backup\owner\repo",
                LastArchive = null,
            },
            CancellationToken);
    }

    [Fact]
    public async Task RunAsync_DryRunCloneNeeded_ReportsWouldCloneWithoutName()
    {
        SetupPreview(ChangeDecision.CloneNeeded);

        var result = await DryRunAsync();

        result.Items.ShouldBe([new("owner/repo", BackupItemStatus.WouldClone, null, TimeSpan.FromSeconds(5), null)]);
        _nameBuilder.DidNotReceiveWithAnyArgs().Build(default!, default!, default, default!);
    }

    [Theory]
    [InlineData(ChangeDecision.UpToDate)]
    [InlineData(ChangeDecision.NothingToArchive)]
    public async Task RunAsync_DryRunNoArchiveNeeded_ReportsUpToDate(ChangeDecision decision)
    {
        SetupPreview(decision);

        var result = await DryRunAsync();

        var expected = new BackupItemResult("owner/repo", BackupItemStatus.UpToDate, null, TimeSpan.FromSeconds(5), null);
        result.Items.ShouldBe([decision == ChangeDecision.UpToDate ? Reached(expected, archived: false) : expected]);
    }

    [Fact]
    public async Task RunAsync_DryRunBranchFallback_ReportsWarningWithFutureArchive()
    {
        _config.Repositories[0].Branch = "feature";
        SetupPreview(ChangeDecision.ArchiveNeeded, branchFallback: true);

        var result = await DryRunAsync();

        var item = result.Items.ShouldHaveSingleItem();
        item.Status.ShouldBe(BackupItemStatus.Warning);
        item.ArchiveFileName.ShouldBe(_archiveFile);
        item.Message.ShouldBe("The branch 'feature' does not exist on the server; the default branch 'main' is used.");
    }

    [Fact]
    public async Task RunAsync_DryRunPreviewFails_ReportsFailedAndProcessesNext()
    {
        AddRepository("other");
        SetupPreview(ChangeDecision.ArchiveNeeded);
        _detector.PreviewAsync(Arg.Is<ChangeDetectionRequest>(request => request.RemoteUrl.Contains("/repo.git")), Arg.Any<CancellationToken>())
            .ThrowsAsync(new GitCommandException("git ls-remote exited with code 128.", "git ls-remote", 128));

        var result = await DryRunAsync();

        result.Items.Select(item => (item.Repository, item.Status)).ShouldBe(
        [
            ("owner/repo", BackupItemStatus.Failed),
            ("owner/other", BackupItemStatus.WouldArchive),
        ]);
        result.ExitCode.ShouldBe(ExitCode.RepositoriesFailed);
    }

    [Fact]
    public async Task RunAsync_DryRunRepositoryFilter_SkipsDiscovery()
    {
        SetupPreview(ChangeDecision.UpToDate);

        var result = await CreateOrchestrator().RunAsync(
            Request(BackupRunFilter.ForRepository("owner", "repo")) with { DryRun = true }, CancellationToken);

        result.Items.ShouldHaveSingleItem().Status.ShouldBe(BackupItemStatus.UpToDate);
        result.PlannedDiscovery.ShouldBeNull();
        await _discovery.DidNotReceiveWithAnyArgs().DiscoverAsync(default!, default, CancellationToken);
    }

    [Fact]
    public async Task RunAsync_DryRunWikiEnabled_PreviewsWiki()
    {
        SetupWiki(hasWiki: true);
        SetupPreview(ChangeDecision.ArchiveNeeded);

        var result = await DryRunAsync();

        result.Items.ShouldBe(
        [
            Reached(new("owner/repo", BackupItemStatus.WouldArchive, _archiveFile, TimeSpan.FromSeconds(5), null)),
            Reached(new("owner/repo.wiki", BackupItemStatus.WouldArchive, _wikiArchiveFile, TimeSpan.FromSeconds(5), null)
            {
                Target = BackupTarget.Wiki,
            }),
        ]);
        await _detector.Received(1).PreviewAsync(WikiRequest(), CancellationToken);
    }

    [Fact]
    public async Task RunAsync_ArchiveNeededAfterEarlierArchive_ReportsOnlyChangedRefs()
    {
        var last = new ArchiveRecord("old.zip", _createdAt, "main", _headCommit, 1, new([new("heads/main", _headCommit), new("heads/old", "def")]));
        _stateStore.LoadAsync(@"C:\backup\owner\repo", 1, "owner/repo", Arg.Any<CancellationToken>())
            .Returns(BackupState.CreateEmpty(1, "owner/repo").WithArchive(BackupTarget.Repository, last, "owner/repo"));

        var result = await RunAsync();

        var item = result.Items.ShouldHaveSingleItem();
        item.Branch.ShouldBe("main");
        item.HeadCommit.ShouldBe(_headCommit);
        item.ChangedRefs.ShouldBe(["heads/old", "tags/v1"]);
    }

    [Theory]
    [InlineData(ChangeDecision.ArchiveNeeded, false)]
    [InlineData(ChangeDecision.UpToDate, false)]
    [InlineData(ChangeDecision.ArchiveNeeded, true)]
    [InlineData(ChangeDecision.UpToDate, true)]
    public async Task RunAsync_BranchFallback_ReportsDefaultBranchAndChangedRefsOnlyWithArchive(ChangeDecision decision, bool dryRun)
    {
        _config.Repositories[0].Branch = "feature";
        SetupDetection(decision, branchFallback: true);
        SetupPreview(decision, branchFallback: true);

        var result = dryRun ? await DryRunAsync() : await RunAsync();

        var item = result.Items.ShouldHaveSingleItem();
        item.Status.ShouldBe(BackupItemStatus.Warning);
        item.Branch.ShouldBe("main");
        item.HeadCommit.ShouldBe(_headCommit);
        item.ChangedRefs.ShouldBe(decision == ChangeDecision.ArchiveNeeded ? ["heads/main", "tags/v1"] : []);
    }

    [Fact]
    public async Task RunAsync_DryRunCloneNeeded_ReportsNoBranchOrRefs()
    {
        SetupPreview(ChangeDecision.CloneNeeded);

        var result = await DryRunAsync();

        var item = result.Items.ShouldHaveSingleItem();
        item.Branch.ShouldBeNull();
        item.HeadCommit.ShouldBeNull();
        item.ChangedRefs.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_RepositoryFails_ReportsNoBranchOrRefs()
    {
        _archiver.CreateAsync(Arg.Any<ArchiveRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("The archive cannot be moved."));

        var result = await RunAsync();

        var item = result.Items.ShouldHaveSingleItem();
        item.Status.ShouldBe(BackupItemStatus.Failed);
        item.Branch.ShouldBeNull();
        item.HeadCommit.ShouldBeNull();
        item.ChangedRefs.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_Archived_TracesRunDiscoveryRepositoryAndArchiveHierarchy()
    {
        using var test = _capture.StartTest();

        await RunAsync();

        var run = _capture.Named(BackupTelemetry.RunSpanName).ShouldHaveSingleItem();
        var discovery = _capture.Named(BackupTelemetry.DiscoverySpanName).ShouldHaveSingleItem();
        var repository = _capture.Named(BackupTelemetry.RepositorySpanName).ShouldHaveSingleItem();
        var archive = _capture.Named(BackupTelemetry.ArchiveOperation).ShouldHaveSingleItem();
        run.ParentSpanId.ShouldBe(test.SpanId);
        run.GetTagItem(BackupTelemetry.DryRunTag).ShouldBe(false);
        run.GetTagItem(BackupTelemetry.RunOutcomeTag).ShouldBe(BackupTelemetry.CompletedOutcome);
        run.Status.ShouldBe(ActivityStatusCode.Unset);
        discovery.ParentSpanId.ShouldBe(run.SpanId);
        repository.ParentSpanId.ShouldBe(run.SpanId);
        repository.GetTagItem(BackupTelemetry.RepositoryTag).ShouldBe("owner/repo");
        repository.GetTagItem(BackupTelemetry.TargetTag).ShouldBe(nameof(BackupTarget.Repository));
        repository.GetTagItem(BackupTelemetry.StatusTag).ShouldBe(nameof(BackupItemStatus.Archived));
        archive.ParentSpanId.ShouldBe(repository.SpanId);
        archive.GetTagItem(BackupTelemetry.OperationTag).ShouldBe(BackupTelemetry.ArchiveOperation);
    }

    [Fact]
    public async Task RunAsync_RepositoryFails_MarksRepositorySpanFailedWithTypeOnly()
    {
        _detector.DetectAsync(Arg.Any<ChangeDetectionRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new GitCommandException("'git fetch' failed for secret-token", "git fetch", 128));
        using var test = _capture.StartTest();

        await RunAsync();

        var repository = _capture.Named(BackupTelemetry.RepositorySpanName).ShouldHaveSingleItem();
        repository.GetTagItem(BackupTelemetry.StatusTag).ShouldBe(nameof(BackupItemStatus.Failed));
        repository.Status.ShouldBe(ActivityStatusCode.Error);
        repository.StatusDescription.ShouldBeNull();
        repository.GetTagItem(BackupTelemetry.ErrorTypeTag).ShouldBe(typeof(GitCommandException).FullName);
        _capture.Spans.ShouldAllBe(span => !span.Events.Any());
        _capture.Spans.SelectMany(span => span.TagObjects)
            .ShouldAllBe(tag => !(tag.Value ?? string.Empty).ToString()!.Contains("secret-token"));
        var run = _capture.Named(BackupTelemetry.RunSpanName).ShouldHaveSingleItem();
        run.Status.ShouldBe(ActivityStatusCode.Unset);
        run.GetTagItem(BackupTelemetry.RunOutcomeTag).ShouldBe(BackupTelemetry.CompletedOutcome);
    }

    [Fact]
    public async Task RunAsync_Cancelled_TagsCurrentRepositoryAndCountsNotStartedAsSkipped()
    {
        AddRepository("other");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        _detector.DetectAsync(Arg.Any<ChangeDetectionRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cancellation.Cancel();
            return Task.FromException<ChangeDetection>(new OperationCanceledException(cancellation.Token));
        });
        using var repositories = _capture.Collect<long>(BackupTelemetry.RepositoriesMetric);
        using var test = _capture.StartTest();

        await CreateOrchestrator().RunAsync(Request(), cancellation.Token);

        var repository = _capture.Named(BackupTelemetry.RepositorySpanName).ShouldHaveSingleItem();
        repository.GetTagItem(BackupTelemetry.RepositoryTag).ShouldBe("owner/repo");
        repository.GetTagItem(BackupTelemetry.StatusTag).ShouldBe(nameof(BackupItemStatus.Cancelled));
        repository.Status.ShouldBe(ActivityStatusCode.Unset);
        _capture.Named(BackupTelemetry.RunSpanName).ShouldHaveSingleItem()
            .GetTagItem(BackupTelemetry.RunOutcomeTag).ShouldBe(BackupTelemetry.CancelledOutcome);
        Statuses(repositories).ShouldBe([nameof(BackupItemStatus.Cancelled), nameof(BackupItemStatus.Skipped)]);
    }

    [Fact]
    public async Task RunAsync_UnavailableRepository_TracesSkippedRepository()
    {
        _config.Repositories[0].Status = RepositoryStatus.Unavailable;
        using var test = _capture.StartTest();

        await RunAsync();

        var repository = _capture.Named(BackupTelemetry.RepositorySpanName).ShouldHaveSingleItem();
        repository.GetTagItem(BackupTelemetry.StatusTag).ShouldBe(nameof(BackupItemStatus.Skipped));
        _capture.Named(BackupTelemetry.ArchiveOperation).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(ChangeDecision.CloneNeeded, BackupItemStatus.WouldClone)]
    [InlineData(ChangeDecision.ArchiveNeeded, BackupItemStatus.WouldArchive)]
    public async Task RunAsync_DryRun_TracesPlannedStatusWithoutArchive(ChangeDecision decision, BackupItemStatus expected)
    {
        SetupPreview(decision);
        using var repositories = _capture.Collect<long>(BackupTelemetry.RepositoriesMetric);
        using var sizes = _capture.Collect<long>(BackupTelemetry.ArchiveSizeMetric);
        using var test = _capture.StartTest();

        await DryRunAsync();

        _capture.Named(BackupTelemetry.RunSpanName).ShouldHaveSingleItem().GetTagItem(BackupTelemetry.DryRunTag).ShouldBe(true);
        _capture.Named(BackupTelemetry.RepositorySpanName).ShouldHaveSingleItem()
            .GetTagItem(BackupTelemetry.StatusTag).ShouldBe(expected.ToString());
        _capture.Named(BackupTelemetry.ArchiveOperation).ShouldBeEmpty();
        repositories.GetMeasurementSnapshot().ShouldHaveSingleItem().Tags[BackupTelemetry.DryRunTag].ShouldBe(true);
        sizes.GetMeasurementSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_DryRunMissingRepository_CountsSkippedWithoutRepositorySpan()
    {
        SetupDiscovery(MissingRepository());
        using var repositories = _capture.Collect<long>(BackupTelemetry.RepositoriesMetric);
        using var test = _capture.StartTest();

        await DryRunAsync();

        _capture.Named(BackupTelemetry.RepositorySpanName).ShouldBeEmpty();
        Statuses(repositories).ShouldBe([nameof(BackupItemStatus.Skipped)]);
    }

    [Fact]
    public async Task RunAsync_NewRepositoryNotConfirmed_CountsSkipped()
    {
        SetupApply(Applied(skipped: ["owner/new"]));
        using var repositories = _capture.Collect<long>(BackupTelemetry.RepositoriesMetric);
        using var test = _capture.StartTest();

        var result = await RunAsync();

        Statuses(repositories).ShouldBe([.. result.Items.Select(item => item.Status.ToString())]);
        Statuses(repositories).ShouldContain(nameof(BackupItemStatus.Skipped));
    }

    [Fact]
    public async Task RunAsync_RepositoryFilter_TracesNoDiscovery()
    {
        using var test = _capture.StartTest();

        await RunAsync(BackupRunFilter.ForRepository("owner", "repo"));

        _capture.Named(BackupTelemetry.DiscoverySpanName).ShouldBeEmpty();
        _capture.Named(BackupTelemetry.RepositorySpanName).ShouldHaveSingleItem()
            .ParentSpanId.ShouldBe(_capture.Named(BackupTelemetry.RunSpanName).ShouldHaveSingleItem().SpanId);
    }

    [Theory]
    [InlineData("config", typeof(BackupConfigException))]
    [InlineData("lock", null)]
    [InlineData("tool", typeof(ToolUnavailableException))]
    [InlineData("discovery", typeof(GitHubApiException))]
    public async Task RunAsync_Stopped_MarksRunFailedAndRecordsStoppedOutcome(string cause, Type? errorType)
    {
        switch (cause)
        {
            case "config":
                _configStore.LoadAsync(Arg.Any<CancellationToken>())
                    .ThrowsAsync(new BackupConfigException(@"C:\app\backup-config.json", ["backupRoot must be set."]));
                break;
            case "lock":
                _runLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns((IDisposable?)null);
                break;
            case "tool":
                _toolLocator.LocateAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new ToolUnavailableException("git was not found."));
                break;
            default:
                _discovery.DiscoverAsync(Arg.Any<BackupConfig>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                    .ThrowsAsync(DiscoveryFailure("server"));
                break;
        }

        using var runs = _capture.Collect<double>(BackupTelemetry.RunDurationMetric);
        using var test = _capture.StartTest();

        await RunAsync();

        var run = _capture.Named(BackupTelemetry.RunSpanName).ShouldHaveSingleItem();
        run.Status.ShouldBe(ActivityStatusCode.Error);
        run.StatusDescription.ShouldBeNull();
        run.GetTagItem(BackupTelemetry.ErrorTypeTag).ShouldBe(errorType?.FullName);
        run.GetTagItem(BackupTelemetry.RunOutcomeTag).ShouldBe(BackupTelemetry.StoppedOutcome);
        _capture.Named(BackupTelemetry.RepositorySpanName).ShouldBeEmpty();
        runs.GetMeasurementSnapshot().ShouldHaveSingleItem().Tags[BackupTelemetry.RunOutcomeTag]
            .ShouldBe(BackupTelemetry.StoppedOutcome);
        if (cause == "discovery")
        {
            _capture.Named(BackupTelemetry.DiscoverySpanName).ShouldHaveSingleItem().Status.ShouldBe(ActivityStatusCode.Error);
        }
    }

    [Fact]
    public async Task RunAsync_Archived_RecordsRepositoryCountDurationsAndArchiveSize()
    {
        AddRepository("other");
        _config.Repositories[1].Status = RepositoryStatus.Unavailable;
        using var repositories = _capture.Collect<long>(BackupTelemetry.RepositoriesMetric);
        using var durations = _capture.Collect<double>(BackupTelemetry.RepositoryDurationMetric);
        using var runs = _capture.Collect<double>(BackupTelemetry.RunDurationMetric);
        using var sizes = _capture.Collect<long>(BackupTelemetry.ArchiveSizeMetric);

        var result = await RunAsync();

        Statuses(repositories).ShouldBe([.. result.Items.Select(item => item.Status.ToString())]);
        repositories.GetMeasurementSnapshot().ShouldAllBe(measurement => measurement.Value == 1);
        repositories.GetMeasurementSnapshot()[0].Tags[BackupTelemetry.TargetTag].ShouldBe(nameof(BackupTarget.Repository));
        repositories.GetMeasurementSnapshot()[0].Tags[BackupTelemetry.DryRunTag].ShouldBe(false);
        durations.GetMeasurementSnapshot().Select(measurement => measurement.Value).ShouldBe([5.0, 0.0]);
        var run = runs.GetMeasurementSnapshot().ShouldHaveSingleItem();
        run.Value.ShouldBe(5.0);
        run.Tags[BackupTelemetry.RunOutcomeTag].ShouldBe(BackupTelemetry.CompletedOutcome);
        var size = sizes.GetMeasurementSnapshot().ShouldHaveSingleItem();
        size.Value.ShouldBe(1234);
        size.Tags[BackupTelemetry.TargetTag].ShouldBe(nameof(BackupTarget.Repository));
    }

    [Fact]
    public async Task RunAsync_Wiki_TracesWikiAsSiblingOfRepositoryAndRecordsItsArchive()
    {
        SetupWiki(hasWiki: true);
        using var sizes = _capture.Collect<long>(BackupTelemetry.ArchiveSizeMetric);
        using var test = _capture.StartTest();

        await RunAsync();

        var run = _capture.Named(BackupTelemetry.RunSpanName).ShouldHaveSingleItem();
        var items = _capture.Named(BackupTelemetry.RepositorySpanName);
        items.Select(span => $"{span.GetTagItem(BackupTelemetry.RepositoryTag)} {span.GetTagItem(BackupTelemetry.TargetTag)}")
            .ShouldBe(["owner/repo Repository", "owner/repo.wiki Wiki"]);
        items.ShouldAllBe(span => span.ParentSpanId == run.SpanId);
        _capture.Named(BackupTelemetry.ArchiveOperation).Select(span => span.ParentSpanId)
            .ShouldBe([items[0].SpanId, items[1].SpanId]);
        sizes.GetMeasurementSnapshot().Select(measurement => measurement.Tags[BackupTelemetry.TargetTag] as string)
            .ShouldBe([nameof(BackupTarget.Repository), nameof(BackupTarget.Wiki)]);
    }

    [Fact]
    public async Task RunAsync_WikiLookupFails_TracesFailedWikiSpan()
    {
        _gitHubApi.GetRepositoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(DiscoveryFailure("server"));
        using var test = _capture.StartTest();

        await RunAsync();

        var wiki = _capture.Named(BackupTelemetry.RepositorySpanName)
            .Where(span => span.GetTagItem(BackupTelemetry.TargetTag) as string == nameof(BackupTarget.Wiki))
            .ShouldHaveSingleItem();
        wiki.GetTagItem(BackupTelemetry.StatusTag).ShouldBe(nameof(BackupItemStatus.Failed));
        wiki.Status.ShouldBe(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task RunAsync_Progress_ReportsDiscoveryThenEachOperationOfRepository()
    {
        _detector.DetectAsync(Arg.Any<ChangeDetectionRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            call.Arg<ChangeDetectionRequest>().OperationProgress?.Report(BackupProgressStage.Cloning);
            return Detection(ChangeDecision.ArchiveNeeded, branchFallback: false);
        });
        var progress = new RecordingProgress<BackupProgress>();

        await CreateOrchestrator().RunAsync(Request() with { Progress = progress }, CancellationToken);

        progress.Values.ShouldBe(
        [
            new(BackupProgressStage.Discovering, null, 0, 0),
            new(BackupProgressStage.Preparing, "owner/repo", 1, 1),
            new(BackupProgressStage.Cloning, "owner/repo", 1, 1),
            new(BackupProgressStage.Archiving, "owner/repo", 1, 1),
        ]);
    }

    [Fact]
    public async Task RunAsync_ProgressWithWikiAndSecondRepository_ReportsWikiAtPositionOfItsRepository()
    {
        AddRepository("other");
        _config.Repositories[1].Status = RepositoryStatus.Unavailable;
        SetupWiki(hasWiki: true);
        var progress = new RecordingProgress<BackupProgress>();

        await CreateOrchestrator().RunAsync(Request() with { Progress = progress }, CancellationToken);

        progress.Values.ShouldBe(
        [
            new(BackupProgressStage.Discovering, null, 0, 0),
            new(BackupProgressStage.Preparing, "owner/repo", 1, 2),
            new(BackupProgressStage.Archiving, "owner/repo", 1, 2),
            new(BackupProgressStage.Preparing, "owner/repo.wiki", 1, 2),
            new(BackupProgressStage.Archiving, "owner/repo.wiki", 1, 2),
            new(BackupProgressStage.Preparing, "owner/other", 2, 2),
        ]);
    }

    [Fact]
    public async Task RunAsync_ProgressForOneRepository_ReportsNoDiscovery()
    {
        var progress = new RecordingProgress<BackupProgress>();

        await CreateOrchestrator().RunAsync(
            Request(BackupRunFilter.ForRepository("owner", "repo")) with { Progress = progress }, CancellationToken);

        progress.Values.ShouldBe(
        [
            new(BackupProgressStage.Preparing, "owner/repo", 1, 1),
            new(BackupProgressStage.Archiving, "owner/repo", 1, 1),
        ]);
    }

    [Fact]
    public async Task DryRun_Progress_ReportsNoArchiving()
    {
        SetupPreview(ChangeDecision.ArchiveNeeded);
        var progress = new RecordingProgress<BackupProgress>();

        await CreateOrchestrator().RunAsync(Request() with { DryRun = true, Progress = progress }, CancellationToken);

        progress.Values.ShouldBe(
        [
            new(BackupProgressStage.Discovering, null, 0, 0),
            new(BackupProgressStage.Preparing, "owner/repo", 1, 1),
        ]);
    }

    [Fact]
    public async Task RunAsync_Progress_PassesOperationProgressToDetector()
    {
        await CreateOrchestrator().RunAsync(
            Request() with { Progress = new RecordingProgress<BackupProgress>() }, CancellationToken);

        await _detector.Received(1).DetectAsync(
            Arg.Is<ChangeDetectionRequest>(request => request.OperationProgress != null), CancellationToken);
    }

    [Fact]
    public async Task Run_EntryWithoutId_FillsIdBeforeDiscoverySavesOnceAndBacksUp()
    {
        _config.Repositories[0].Id = 0;
        SetupResolution(Resolved("owner", "repo", 9));
        long? idAtDiscovery = null;
        _discovery.DiscoverAsync(Arg.Any<BackupConfig>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            idAtDiscovery = call.Arg<BackupConfig>().Repositories[0].Id;
            return DiscoveryResult.Empty;
        });
        long? idAtSave = null;
        _configStore.When(store => store.SaveAsync(Arg.Any<BackupConfig>(), Arg.Any<CancellationToken>()))
            .Do(call => idAtSave = call.Arg<BackupConfig>().Repositories[0].Id);

        var result = await RunAsync();

        result.Items.ShouldBe([Reached(new("owner/repo", BackupItemStatus.Archived, _archiveFile, TimeSpan.FromSeconds(5), null))]);
        result.SavedRepositoryIds.ShouldBe(["owner/repo"]);
        idAtDiscovery.ShouldBe(9);
        idAtSave.ShouldBe(9);
        await _configStore.Received(1).SaveAsync(_config, CancellationToken);
        await _stateStore.Received(1).AppendAsync(
            @"C:\backup\owner\repo", 9, "owner/repo", BackupTarget.Repository, Arg.Any<ArchiveRecord>(), CancellationToken.None);
        await _gitHubApi.DidNotReceiveWithAnyArgs().GetRepositoryByIdAsync(default, CancellationToken);
    }

    [Fact]
    public async Task Run_OnlyIdFilled_ExitCodeSuccess()
    {
        _config.Repositories[0].Id = 0;
        SetupResolution(Resolved("owner", "repo", 9));

        var result = await RunAsync();

        result.ExitCode.ShouldBe(ExitCode.Success);
        result.Discovery.ShouldBeNull();
    }

    [Fact]
    public async Task Run_IdsAndDiscoveryChanges_SavedOnceByApplier()
    {
        _config.Repositories[0].Id = 0;
        SetupResolution(Resolved("owner", "repo", 9));
        long? idAtApply = null;
        SetupApply(Applied(), config => idAtApply = config.Repositories[0].Id);

        await RunAsync();

        idAtApply.ShouldBe(9);
        await _configStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, CancellationToken);
    }

    [Fact]
    public async Task Run_ApplierSavedNothing_SavesIds()
    {
        _config.Repositories[0].Id = 0;
        SetupResolution(Resolved("owner", "repo", 9));
        SetupApply(Applied() with { ConfigSaved = false });

        await RunAsync();

        await _configStore.Received(1).SaveAsync(_config, CancellationToken);
    }

    [Fact]
    public async Task Run_NotFound_FailedOthersProcessedNoUnavailable()
    {
        _config.Repositories[0].Id = 0;
        AddRepository("other");
        SetupResolution(new([], ["owner/repo"], []));

        var result = await RunAsync();

        result.Items.ShouldBe(
        [
            new("owner/repo", BackupItemStatus.Failed, null, TimeSpan.Zero, BackupOrchestrator.RepositoryNotFoundMessage),
            Reached(new("owner/other", BackupItemStatus.Archived, _archiveFile, TimeSpan.FromSeconds(5), null)),
        ]);
        result.ExitCode.ShouldBe(ExitCode.RepositoriesFailed);
        _config.Repositories[0].Id.ShouldBe(0);
        _config.Repositories[0].Status.ShouldBe(RepositoryStatus.Active);
        await _configStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, CancellationToken);
        await _detector.Received(1).DetectAsync(Arg.Any<ChangeDetectionRequest>(), Arg.Any<CancellationToken>());
        var record = SingleRecord(_repositoryIdUnresolvedEventId);
        record.Level.ShouldBe(LogLevel.Error);
        record.GetStructuredStateValue("Reason").ShouldBe(BackupOrchestrator.RepositoryNotFoundMessage);
    }

    [Fact]
    public async Task Run_Conflict_FailedWithReasonAndIdNotWritten()
    {
        _config.Repositories[0].Id = 0;
        const string reason = "The id 2 found on GitHub for owner/repo already belongs to owner/old in backup-config.json.";
        SetupResolution(new([], [], [new("owner/repo", reason)]));

        var result = await RunAsync();

        result.Items.ShouldBe([new("owner/repo", BackupItemStatus.Failed, null, TimeSpan.Zero, reason)]);
        _config.Repositories[0].Id.ShouldBe(0);
        await _configStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, CancellationToken);
    }

    [Fact]
    public async Task Run_InteractiveNotDryRun_DoesNotAskConfirm()
    {
        _config.Repositories[0].Id = 0;
        SetupResolution(Resolved("owner", "repo", 9));

        await CreateOrchestrator().RunAsync(Request() with { Mode = RunMode.Interactive }, CancellationToken);

        await _handler.DidNotReceiveWithAnyArgs().ConfirmIdSaveAsync(default!, CancellationToken);
        _config.Repositories[0].Id.ShouldBe(9);
    }

    [Fact]
    public async Task Run_RepoFilterEntryWithoutId_ResolvedWithoutDiscovery()
    {
        _config.Repositories[0].Id = 0;
        SetupResolution(Resolved("owner", "repo", 9));

        var result = await RunAsync(BackupRunFilter.ForRepository("owner", "repo"));

        result.Items.Select(item => item.Status).ShouldBe([BackupItemStatus.Archived]);
        _config.Repositories[0].Id.ShouldBe(9);
        await _discovery.DidNotReceiveWithAnyArgs().DiscoverAsync(default!, default, CancellationToken);
        await _gitHubApi.DidNotReceiveWithAnyArgs().GetRepositoryAsync(default!, default!, CancellationToken);
        await _configStore.Received(1).SaveAsync(_config, CancellationToken);
    }

    [Fact]
    public async Task Run_AccountFilter_ResolvesOnlyThatAccount()
    {
        _config.Repositories[0].Id = 0;
        _config.Accounts.Add(new() { Url = "https://github.com/octocat", Repositories = [new() { Name = "listed" }] });
        _config.Accounts.Add(new() { Url = "https://github.com/other", Repositories = [new() { Name = "foreign" }] });
        IReadOnlyList<BackupWorkItem>? resolvedItems = null;
        _discovery.ResolveIdsAsync(Arg.Any<BackupConfig>(), Arg.Any<IReadOnlyList<BackupWorkItem>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                resolvedItems = call.Arg<IReadOnlyList<BackupWorkItem>>();
                return Resolved("octocat", "listed", 9, "octocat");
            });

        await RunAsync(BackupRunFilter.ForAccount("OctoCat"));

        resolvedItems.ShouldNotBeNull().Select(item => item.FullName).ShouldBe(["octocat/listed"]);
        _config.Accounts[0].Repositories[0].Id.ShouldBe(9);
    }

    [Fact]
    public async Task Run_ExcludedEntryWithoutId_NotResolved()
    {
        _config.Repositories[0].Id = 0;
        _config.Exclude.Add("owner/repo");

        var result = await RunAsync();

        result.Items.ShouldBeEmpty();
        await _discovery.DidNotReceiveWithAnyArgs().ResolveIdsAsync(default!, default!, CancellationToken);
        await _gitHubApi.DidNotReceiveWithAnyArgs().GetRepositoryAsync(default!, default!, CancellationToken);
    }

    [Theory]
    [InlineData("rate-limit")]
    [InlineData("unauthorized")]
    [InlineData("server")]
    public async Task Run_ResolveApiFailure_Critical(string kind)
    {
        _config.Repositories[0].Id = 0;
        var failure = DiscoveryFailure(kind);
        _discovery.ResolveIdsAsync(Arg.Any<BackupConfig>(), Arg.Any<IReadOnlyList<BackupWorkItem>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<RepositoryIdResolution>(failure));

        var result = await RunAsync();

        result.CriticalErrors.ShouldBe([failure.Message]);
        result.ExitCode.ShouldBe(ExitCode.Critical);
        await _discovery.DidNotReceiveWithAnyArgs().DiscoverAsync(default!, default, CancellationToken);
        await _configStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, CancellationToken);
        await _detector.DidNotReceiveWithAnyArgs().DetectAsync(default!, CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Run_CancelledDuringResolveOrQuestion_CancelledWithoutSave(bool dryRun)
    {
        _config.Repositories[0].Id = 0;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        if (dryRun)
        {
            SetupResolution(Resolved("owner", "repo", 9));
            _handler.ConfirmIdSaveAsync(Arg.Any<RepositoryIdProposal>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                cancellation.Cancel();
                return Task.FromException<bool>(new OperationCanceledException(cancellation.Token));
            });
        }
        else
        {
            _discovery.ResolveIdsAsync(Arg.Any<BackupConfig>(), Arg.Any<IReadOnlyList<BackupWorkItem>>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    cancellation.Cancel();
                    return Task.FromException<RepositoryIdResolution>(new OperationCanceledException(cancellation.Token));
                });
        }

        var result = await CreateOrchestrator().RunAsync(Request() with { DryRun = dryRun }, cancellation.Token);

        result.IsCancelled.ShouldBeTrue();
        result.ExitCode.ShouldBe(ExitCode.Critical);
        await _configStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, CancellationToken);
    }

    [Fact]
    public async Task DryRun_ConfirmYes_SavesOnlyConfirmedIds()
    {
        _config.Repositories[0].Id = 0;
        _config.Repositories.Add(new() { Url = "https://github.com/owner/other" });
        SetupResolution(new([Resolved("owner", "repo", 9).Resolved[0], Resolved("owner", "other", 10).Resolved[0]], [], []));
        _handler.ConfirmIdSaveAsync(new("owner/repo", 9), Arg.Any<CancellationToken>()).Returns(true);
        _handler.ConfirmIdSaveAsync(new("owner/other", 10), Arg.Any<CancellationToken>()).Returns(false);
        long[]? idsAtSave = null;
        _configStore.When(store => store.SaveAsync(Arg.Any<BackupConfig>(), Arg.Any<CancellationToken>()))
            .Do(call => idsAtSave = [.. call.Arg<BackupConfig>().Repositories.Select(repository => repository.Id)]);
        SetupPreview(ChangeDecision.CloneNeeded);

        var result = await DryRunAsync();

        idsAtSave.ShouldBe([9, 0]);
        await _configStore.Received(1).SaveAsync(_config, CancellationToken);
        result.SavedRepositoryIds.ShouldBe(["owner/repo"]);
        result.Items.Select(item => item.Status).ShouldBe([BackupItemStatus.WouldClone, BackupItemStatus.WouldClone]);
        await _applier.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default!, default!, default!, CancellationToken);
    }

    [Fact]
    public async Task DryRun_ConfirmNo_NoSaveItemChecked()
    {
        _config.Repositories[0].Id = 0;
        SetupResolution(Resolved("owner", "repo", 9));
        _handler.ConfirmIdSaveAsync(Arg.Any<RepositoryIdProposal>(), Arg.Any<CancellationToken>()).Returns(false);
        SetupPreview(ChangeDecision.CloneNeeded);

        var result = await DryRunAsync();

        await _configStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, CancellationToken);
        result.SavedRepositoryIds.ShouldBeEmpty();
        result.Items.ShouldBe([new("owner/repo", BackupItemStatus.WouldClone, null, TimeSpan.FromSeconds(5), null)]);
        await _stateStore.Received(1).LoadAsync(@"C:\backup\owner\repo", 9, "owner/repo", Arg.Any<CancellationToken>());
        await _handler.Received(1).ConfirmIdSaveAsync(new("owner/repo", 9), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Makes the lookup of the entries without an ID return <paramref name="resolution"/>.
    /// </summary>
    /// <param name="resolution">The outcome of the lookups.</param>
    private void SetupResolution(RepositoryIdResolution resolution) =>
        _discovery.ResolveIdsAsync(Arg.Any<BackupConfig>(), Arg.Any<IReadOnlyList<BackupWorkItem>>(), Arg.Any<CancellationToken>())
            .Returns(resolution);

    /// <summary>
    /// Creates the outcome of a lookup that found one ID.
    /// </summary>
    /// <param name="owner">The owner of the entry.</param>
    /// <param name="name">The name of the entry.</param>
    /// <param name="id">The ID found.</param>
    /// <param name="accountLogin">The account that lists the entry, or <see langword="null"/> for a standalone entry.</param>
    /// <returns>The outcome.</returns>
    private static RepositoryIdResolution Resolved(string owner, string name, long id, string? accountLogin = null) =>
        new([new(owner, name, accountLogin, Repository(id, owner, name))], [], []);

    /// <summary>
    /// Adds the branch <c>main</c> and its commit from <see cref="_refs"/> to an expected result, and every ref of
    /// <see cref="_refs"/> as changed when the item has its first archive.
    /// </summary>
    /// <param name="item">The expected result without branch data.</param>
    /// <param name="archived">Whether the item has an archive in the run.</param>
    /// <returns>The expected result.</returns>
    private static BackupItemResult Reached(BackupItemResult item, bool archived = true) =>
        item with { Branch = "main", HeadCommit = _headCommit, ChangedRefs = archived ? ["heads/main", "tags/v1"] : [] };

    /// <summary>
    /// Creates a detection result for the branch <c>main</c> with <see cref="_refs"/>.
    /// </summary>
    /// <param name="decision">The decision.</param>
    /// <param name="branchFallback">Whether the default branch replaced the requested one.</param>
    /// <returns>The detection result.</returns>
    private static ChangeDetection Detection(ChangeDecision decision, bool branchFallback) =>
        decision == ChangeDecision.NothingToArchive
            ? new(decision, ChangeReason.EmptyRepository, null, false, RefSnapshot.Empty)
            : new(decision, ChangeReason.NoArchive, "main", branchFallback, _refs);

    /// <summary>
    /// Makes the detector return <paramref name="decision"/> after five seconds of the fake clock.
    /// </summary>
    /// <param name="decision">The decision.</param>
    /// <param name="branchFallback">Whether the default branch replaced the requested one.</param>
    private void SetupDetection(ChangeDecision decision, bool branchFallback = false) =>
        _detector.DetectAsync(Arg.Any<ChangeDetectionRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(5));
            return Detection(decision, branchFallback);
        });

    /// <summary>
    /// Makes the preview of the detector return <paramref name="decision"/> after five seconds of the fake clock.
    /// </summary>
    /// <param name="decision">The decision.</param>
    /// <param name="branchFallback">Whether the default branch replaced the requested one.</param>
    private void SetupPreview(ChangeDecision decision, bool branchFallback = false) =>
        _detector.PreviewAsync(Arg.Any<ChangeDetectionRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(5));
            return decision == ChangeDecision.CloneNeeded
                ? new ChangeDetection(decision, ChangeReason.NoClone, null, false, RefSnapshot.Empty)
                : Detection(decision, branchFallback);
        });

    /// <summary>
    /// Runs the orchestrator as a dry run over every repository with the test cancellation token.
    /// </summary>
    /// <returns>The result of the run.</returns>
    private Task<BackupRunResult> DryRunAsync() =>
        CreateOrchestrator().RunAsync(Request() with { DryRun = true }, CancellationToken);

    /// <summary>
    /// Makes the GitHub API report for every repository whether its wiki feature is enabled.
    /// </summary>
    /// <param name="hasWiki">Whether the wiki feature is enabled.</param>
    private void SetupWiki(bool hasWiki) =>
        _gitHubApi.GetRepositoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            new GitHubRepository(
                1,
                call.ArgAt<string>(0),
                call.ArgAt<string>(1),
                IsPrivate: false,
                IsFork: false,
                IsArchived: false,
                hasWiki,
                "main",
                $"https://github.com/{call.ArgAt<string>(0)}/{call.ArgAt<string>(1)}"));

    /// <summary>
    /// Matches the detection request of the wiki of <c>owner/repo</c>.
    /// </summary>
    /// <returns>The argument matcher.</returns>
    private static ChangeDetectionRequest WikiRequest() =>
        Arg.Is<ChangeDetectionRequest>(request => request.RemoteUrl == _wikiUrl);

    /// <summary>
    /// Adds a standalone repository of <c>owner</c> after the existing ones.
    /// </summary>
    /// <param name="name">The repository name.</param>
    private void AddRepository(string name) =>
        _config.Repositories.Add(new() { Url = $"https://github.com/owner/{name}", Id = _config.Repositories.Count + 1 });

    /// <summary>
    /// Creates the orchestrator over the substitutes.
    /// </summary>
    /// <returns>The orchestrator.</returns>
    private BackupOrchestrator CreateOrchestrator() => new(
        _configStore,
        _toolLocator,
        _tokenProvider,
        _runLock,
        new RepositoryPathResolver(@"C:\app\sources"),
        new ExclusionMatcher(),
        _discovery,
        _applier,
        new GitHubRemoteUrlProvider(),
        _gitHubApi,
        _detector,
        _nameBuilder,
        _archiver,
        _stateStore,
        _fileSystem,
        _timeProvider,
        _capture.Telemetry,
        _logger);

    /// <summary>
    /// Returns the status tag of each measurement of the repository counter, in the order they were recorded.
    /// </summary>
    /// <param name="collector">The collector of <see cref="BackupTelemetry.RepositoriesMetric"/>.</param>
    /// <returns>The statuses.</returns>
    private static List<string?> Statuses(MetricCollector<long> collector) =>
        [.. collector.GetMeasurementSnapshot().Select(measurement => measurement.Tags[BackupTelemetry.StatusTag] as string)];

    /// <summary>
    /// Creates the request of a run with the substitute decision handler.
    /// </summary>
    /// <param name="filter">The filter of the run, or <see langword="null"/> for <see cref="BackupRunFilter.All"/>.</param>
    /// <returns>The request.</returns>
    private BackupRunRequest Request(BackupRunFilter? filter = null) => new(filter ?? BackupRunFilter.All, _handler);

    /// <summary>
    /// Runs the orchestrator with the test cancellation token.
    /// </summary>
    /// <param name="filter">The filter of the run, or <see langword="null"/> for <see cref="BackupRunFilter.All"/>.</param>
    /// <returns>The result of the run.</returns>
    private Task<BackupRunResult> RunAsync(BackupRunFilter? filter = null) =>
        CreateOrchestrator().RunAsync(Request(filter), CancellationToken);

    /// <summary>
    /// Returns the log records with the event id <paramref name="eventId"/>, in the order they were written.
    /// </summary>
    /// <param name="eventId">The event id of the <see cref="LoggerMessageAttribute"/>.</param>
    /// <returns>The records.</returns>
    private List<FakeLogRecord> Records(int eventId) =>
        [.. _logger.Collector.GetSnapshot().Where(record => record.Id.Id == eventId)];

    /// <summary>
    /// Returns the only log record with the event id <paramref name="eventId"/> and fails if there is not exactly one.
    /// </summary>
    /// <param name="eventId">The event id of the <see cref="LoggerMessageAttribute"/>.</param>
    /// <returns>The record.</returns>
    private FakeLogRecord SingleRecord(int eventId) => Records(eventId).ShouldHaveSingleItem();

    /// <summary>
    /// Makes discovery report <paramref name="result"/>.
    /// </summary>
    /// <param name="result">The classified repositories.</param>
    private void SetupDiscovery(DiscoveryResult result) =>
        _discovery.DiscoverAsync(Arg.Any<BackupConfig>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(result);

    /// <summary>
    /// Makes discovery report <c>owner/repo</c> as missing, so the applier is asked, and makes the applier run
    /// <paramref name="apply"/> on the configuration and return <paramref name="result"/>.
    /// </summary>
    /// <param name="result">What the applier reports.</param>
    /// <param name="apply">Changes the configuration as the decisions would, or <see langword="null"/>.</param>
    /// <returns>The discovery result the applier receives.</returns>
    private DiscoveryResult SetupApply(DiscoveryApplyResult result, Action<BackupConfig>? apply = null)
    {
        var discovery = MissingRepository();
        SetupDiscovery(discovery);
        _applier.ApplyAsync(
                Arg.Any<BackupConfig>(),
                Arg.Any<DiscoveryResult>(),
                Arg.Any<IDiscoveryDecisionHandler>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                apply?.Invoke(call.Arg<BackupConfig>());
                return result;
            });
        return discovery;
    }

    /// <summary>
    /// Creates a discovery result that reports the standalone <c>owner/repo</c> as missing.
    /// </summary>
    /// <returns>The discovery result.</returns>
    private static DiscoveryResult MissingRepository() =>
        new([], [], [new("owner", "repo", 1, RepositoryStatus.Active, null)], [], []);

    /// <summary>
    /// Creates what the applier reports.
    /// </summary>
    /// <param name="added">The added repositories.</param>
    /// <param name="skipped">The new repositories left out.</param>
    /// <param name="excluded">The new repositories excluded.</param>
    /// <param name="markedUnavailable">The entries marked unavailable.</param>
    /// <param name="reactivated">The entries reactivated.</param>
    /// <param name="renamed">The renamed repositories.</param>
    /// <returns>The apply result.</returns>
    private static DiscoveryApplyResult Applied(
        string[]? added = null,
        string[]? skipped = null,
        string[]? excluded = null,
        string[]? markedUnavailable = null,
        string[]? reactivated = null,
        RenameOutcome[]? renamed = null) =>
        new(added ?? [], skipped ?? [], excluded ?? [], markedUnavailable ?? [], reactivated ?? [], renamed ?? [], ConfigSaved: true);

    /// <summary>
    /// Creates repository metadata as GitHub reports it.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <param name="owner">The owner.</param>
    /// <param name="name">The name.</param>
    /// <param name="hasWiki">Whether the wiki feature is enabled.</param>
    /// <returns>The repository.</returns>
    private static GitHubRepository Repository(long id, string owner, string name, bool hasWiki = false) =>
        new(id, owner, name, false, false, false, hasWiki, "main", $"https://github.com/{owner}/{name}");

    /// <summary>
    /// Creates the exception of a failed discovery request.
    /// </summary>
    /// <param name="kind">The kind of failure: <c>rate-limit</c>, <c>unauthorized</c> or <c>server</c>.</param>
    /// <returns>The exception.</returns>
    private static GitHubApiException DiscoveryFailure(string kind) => kind switch
    {
        "rate-limit" => new GitHubRateLimitExceededException(
            "The GitHub API rate limit is exhausted; it resets at 2026-11-11 13:00.", "user/repos", 403, _createdAt.AddHours(1), 60, 0),
        "unauthorized" => new GitHubAuthenticationException("The GitHub API rejected the token (401).", "user/repos"),
        _ => new GitHubApiException("The GitHub API answered 502.", "user/repos", 502),
    };

    /// <summary>
    /// Creates the exception of a failed apply step.
    /// </summary>
    /// <param name="kind">The kind of failure: <c>invalid</c>, <c>io</c> or <c>access</c>.</param>
    /// <returns>The exception.</returns>
    private static Exception ApplyFailure(string kind) => kind switch
    {
        "invalid" => new BackupConfigException(@"C:\app\backup-config.json", ["repositories[1].id is duplicated."]),
        "io" => new IOException("The configuration file cannot be written."),
        _ => new UnauthorizedAccessException("Access to the configuration file is denied."),
    };
}
