using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Archiving;
using GitHubBackup.Core.Authentication;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Changes;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.State;
using GitHubBackup.Core.Tests.Logging;
using GitHubBackup.Core.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GitHubBackup.Core.Tests.Runs;

/// <summary>
/// Tests <see cref="BackupOrchestrator"/> with substitutes for the stores, the tools, the detector and the archiver, an
/// in-memory file system and a fake clock.
/// </summary>
public sealed class BackupOrchestratorTests
{
    private const string _sourcesRoot = @"C:\sources";
    private const string _temporaryFolder = @"C:\sources\.tmp";
    private const string _gitPath = @"C:\git\git.exe";
    private const string _sevenZipPath = @"C:\7z\7z.exe";
    private const string _headCommit = "0123456789abcdef0123456789abcdef01234567";
    private const string _archiveFile = "owner_repo_20261111-120000_0123456789.zip";

    private static readonly DateTimeOffset _createdAt = new(2026, 11, 11, 12, 0, 0, TimeSpan.FromHours(1));
    private static readonly RefSnapshot _refs = new([new("heads/main", _headCommit), new("tags/v1", "abc")]);

    private readonly IBackupConfigStore _configStore = Substitute.For<IBackupConfigStore>();
    private readonly IToolLocator _toolLocator = Substitute.For<IToolLocator>();
    private readonly ITokenProvider _tokenProvider = Substitute.For<ITokenProvider>();
    private readonly IChangeDetector _detector = Substitute.For<IChangeDetector>();
    private readonly IArchiveNameBuilder _nameBuilder = Substitute.For<IArchiveNameBuilder>();
    private readonly IArchiver _archiver = Substitute.For<IArchiver>();
    private readonly IBackupStateStore _stateStore = Substitute.For<IBackupStateStore>();
    private readonly MockFileSystem _fileSystem = new();
    private readonly FakeTimeProvider _timeProvider = new(_createdAt);
    private readonly RecordingLogger<BackupOrchestrator> _logger = new();
    private readonly BackupConfig _config = new() { BackupRoot = @"C:\backup", SourcesRoot = _sourcesRoot };

    /// <summary>
    /// Sets up a valid configuration with one standalone repository, the tools, a token, an empty state and an
    /// archiver that succeeds.
    /// </summary>
    public BackupOrchestratorTests()
    {
        _config.Repositories.Add(new() { Url = "https://github.com/owner/repo", Id = 1, Branch = "main" });
        _configStore.LoadAsync(Arg.Any<CancellationToken>()).Returns(new BackupConfigLoadResult(_config, []));
        _toolLocator.LocateAsync(Arg.Any<CancellationToken>())
            .Returns(new ToolSet(new(_gitPath, "2.46.0"), "3.5.1", new(_sevenZipPath, "24.08")));
        _tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(new GitHubToken("secret-token", "test"));
        _stateStore.LoadAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => BackupState.CreateEmpty(call.ArgAt<long>(1), call.ArgAt<string>(2)));
        _nameBuilder.Build(Arg.Any<string>(), Arg.Any<string>(), BackupTarget.Repository, Arg.Any<string>())
            .Returns(new ArchiveName(_archiveFile, _createdAt));
        _archiver.CreateAsync(Arg.Any<ArchiveRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => new ArchiveResult(Path.Combine(call.Arg<ArchiveRequest>().DestinationFolder, _archiveFile), 1234));
        SetupDetection(ChangeDecision.ArchiveNeeded);
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RunAsync_ArchiveNeeded_ArchivesAndRecordsState()
    {
        var result = await RunAsync();

        result.Items.ShouldBe([new("owner/repo", BackupItemStatus.Archived, _archiveFile, TimeSpan.FromSeconds(5), null)]);
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
            CancellationToken);
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

        result.Items.ShouldBe([new("owner/repo", BackupItemStatus.UpToDate, null, TimeSpan.FromSeconds(5), null)]);
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
        _logger.Entries.ShouldContain((LogLevel.Warning, item.Message!));
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
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Error);
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
        _logger.Entries.Count(entry => entry.Level == LogLevel.Error).ShouldBe(2);
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
    public async Task RunAsync_Cancelled_ThrowsAndStopsProcessing()
    {
        AddRepository("other");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        _detector.DetectAsync(Arg.Any<ChangeDetectionRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cancellation.Cancel();
            return Task.FromException<ChangeDetection>(new OperationCanceledException(cancellation.Token));
        });

        await Should.ThrowAsync<OperationCanceledException>(() => CreateOrchestrator().RunAsync(cancellation.Token));

        await _detector.ReceivedWithAnyArgs(1).DetectAsync(default!, CancellationToken);
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
        _logger.Entries.Count(entry => entry.Message.Contains("Deleted the leftover temporary archive")).ShouldBe(2);
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
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains(locked));
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
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("is a link"));
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
    public async Task RunAsync_ConfigWarnings_ReturnedWithResult()
    {
        _configStore.LoadAsync(Arg.Any<CancellationToken>()).Returns(new BackupConfigLoadResult(_config, ["w1", "w2"]));

        var result = await RunAsync();

        result.ConfigWarnings.ShouldBe(["w1", "w2"]);
        result.CriticalErrors.ShouldBeEmpty();
    }

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
        new RepositoryPathResolver(@"C:\app\sources"),
        new GitHubRemoteUrlProvider(),
        _detector,
        _nameBuilder,
        _archiver,
        _stateStore,
        _fileSystem,
        _timeProvider,
        _logger);

    /// <summary>
    /// Runs the orchestrator with the test cancellation token.
    /// </summary>
    /// <returns>The result of the run.</returns>
    private Task<BackupRunResult> RunAsync() => CreateOrchestrator().RunAsync(CancellationToken);
}
