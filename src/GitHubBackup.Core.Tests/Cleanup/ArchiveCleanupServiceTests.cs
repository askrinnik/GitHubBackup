using GitHubBackup.Core.Archiving;
using GitHubBackup.Core.Cleanup;
using GitHubBackup.Core.Files;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.History;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.State;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GitHubBackup.Core.Tests.Cleanup;

/// <summary>
/// Tests <see cref="ArchiveCleanupService"/> over substituted stores, Recycle Bin and run lock.
/// </summary>
public sealed class ArchiveCleanupServiceTests
{
    private const int _startedEventId = 1;

    private const int _fileRecycledEventId = 2;

    private const int _fileFailedEventId = 3;

    private const int _recordRemovedEventId = 4;

    private const int _stateWriteFailedEventId = 5;

    private const int _runLockHeldEventId = 6;

    private const int _planChangedEventId = 7;

    private const string _folder = @"C:\backup\octo\tool";

    private const string _fullName = "octo/tool";

    private const long _id = 7;

    private static readonly DateTimeOffset _createdAt = new(2026, 10, 1, 9, 0, 0, TimeSpan.FromHours(2));

    private readonly IBackupStateStore _stateStore = Substitute.For<IBackupStateStore>();

    private readonly IArchiveHistoryProvider _historyProvider = Substitute.For<IArchiveHistoryProvider>();

    private readonly IRecycleBin _recycleBin = Substitute.For<IRecycleBin>();

    private readonly IRunLock _runLock = Substitute.For<IRunLock>();

    private readonly IDisposable _lockHandle = Substitute.For<IDisposable>();

    private readonly IArchiveNameBuilder _nameBuilder = Substitute.For<IArchiveNameBuilder>();

    private readonly FakeLogger<ArchiveCleanupService> _logger = new();

    private readonly ArchiveCleanupService _service;

    private BackupState _state;

    /// <summary>
    /// Creates the service over a repository with two old archives on the disk, one record without a file, the last
    /// archive and one archive file without a record; every file goes to the Recycle Bin and the run lock is free.
    /// </summary>
    public ArchiveCleanupServiceTests()
    {
        _state = BackupState.CreateEmpty(_id, _fullName)
            .WithArchive(BackupTarget.Repository, CreateRecord("old1.zip", 0), _fullName)
            .WithArchive(BackupTarget.Repository, CreateRecord("old2.zip", 1), _fullName)
            .WithArchive(BackupTarget.Repository, CreateRecord("gone.zip", 2), _fullName)
            .WithArchive(BackupTarget.Repository, CreateRecord("last.zip", 3), _fullName);
        _stateStore.LoadAsync(_folder, _id, _fullName, Arg.Any<CancellationToken>()).Returns(_ => _state);
        _historyProvider.LoadAsync(_folder, _id, _fullName, Arg.Any<CancellationToken>()).Returns(new ArchiveHistory(_folder, true,
        [
            CreateEntry("extra.zip", 4, exists: true, isInState: false),
            CreateEntry("last.zip", 3, exists: true, isInState: true),
            CreateEntry("gone.zip", 2, exists: false, isInState: true),
            CreateEntry("old2.zip", 1, exists: true, isInState: true),
            CreateEntry("old1.zip", 0, exists: true, isInState: true),
        ]));
        _recycleBin.RecycleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(RecycleOutcome.Success);
        _runLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns(_lockHandle);
        _nameBuilder.TryParse(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), out Arg.Any<ParsedArchiveName?>())
            .Returns(call =>
            {
                call[3] = new ParsedArchiveName(BackupTarget.Repository, _createdAt, "abcdef0123");
                return true;
            });
        _service = new(_stateStore, _historyProvider, _recycleBin, _runLock, _nameBuilder, _logger);
    }

    [Fact]
    public async Task ExecuteAsync_RecycleThrowsForOneFile_ReportsItContinuesAndWritesStateOnce()
    {
        _recycleBin.RecycleAsync($@"{_folder}\old1.zip", Arg.Any<CancellationToken>())
            .Returns<RecycleOutcome>(_ => throw new InvalidOperationException("The Shell failed."));

        var result = await ExecuteAsync(await PrepareAsync());

        result.RecycledFiles.Select(file => file.File).ShouldBe(["old2.zip", "extra.zip"]);
        result.Errors.ShouldBe([$@"'{_folder}\old1.zip': The Shell failed."]);
        await _stateStore.Received(1).ReplaceAsync(
            _folder,
            Arg.Is<BackupState>(state => HasRepositoryFiles(state, "old1.zip", "last.zip")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PrepareAsync_Archives_ReturnsPlanWithoutTakingLock()
    {
        var plan = await PrepareAsync();

        plan.FilesToRecycle.Select(file => file.File).ShouldBe(["old1.zip", "old2.zip", "extra.zip"]);
        plan.RecordsWithoutFiles.ShouldBe([new(BackupTarget.Repository, "gone.zip")]);
        await _runLock.DidNotReceive().TryAcquireAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_LockHeld_ChangesNothing()
    {
        var plan = await PrepareAsync();
        _runLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns((IDisposable?)null);

        var result = await ExecuteAsync(plan);

        result.ShouldBe(ArchiveCleanupResult.RunLockHeld);
        await _recycleBin.DidNotReceive().RecycleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _stateStore.DidNotReceive().ReplaceAsync(Arg.Any<string>(), Arg.Any<BackupState>(), Arg.Any<CancellationToken>());
        var record = SingleRecord(_runLockHeldEventId);
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Repository").ShouldBe(_fullName);
    }

    [Fact]
    public async Task ExecuteAsync_PlanChangedUnderLock_ChangesNothingAndReleasesLock()
    {
        var plan = await PrepareAsync();
        _historyProvider.LoadAsync(_folder, _id, _fullName, Arg.Any<CancellationToken>()).Returns(new ArchiveHistory(_folder, true,
        [
            CreateEntry("last.zip", 3, exists: true, isInState: true),
            CreateEntry("gone.zip", 2, exists: false, isInState: true),
            CreateEntry("old2.zip", 1, exists: true, isInState: true),
            CreateEntry("old1.zip", 0, exists: false, isInState: true),
        ]));

        var result = await ExecuteAsync(plan);

        result.ShouldBe(ArchiveCleanupResult.PlanChanged);
        await _recycleBin.DidNotReceive().RecycleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _stateStore.DidNotReceive().ReplaceAsync(Arg.Any<string>(), Arg.Any<BackupState>(), Arg.Any<CancellationToken>());
        _lockHandle.Received(1).Dispose();
        SingleRecord(_planChangedEventId).Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public async Task ExecuteAsync_AllFilesMoved_RecyclesInOrderAndWritesStateOnce()
    {
        var plan = await PrepareAsync();

        var result = await ExecuteAsync(plan);

        result.Status.ShouldBe(ArchiveCleanupStatus.Completed);
        result.RecycledFiles.ShouldBe(plan.FilesToRecycle);
        result.RemovedRecords.ShouldBe(
        [
            new(BackupTarget.Repository, "old1.zip"),
            new(BackupTarget.Repository, "old2.zip"),
            new(BackupTarget.Repository, "gone.zip"),
        ]);
        result.Errors.ShouldBeEmpty();
        Received.InOrder(() =>
        {
            _recycleBin.RecycleAsync($@"{_folder}\old1.zip", Arg.Any<CancellationToken>());
            _recycleBin.RecycleAsync($@"{_folder}\old2.zip", Arg.Any<CancellationToken>());
            _recycleBin.RecycleAsync($@"{_folder}\extra.zip", Arg.Any<CancellationToken>());
        });
        await _stateStore.Received(1).ReplaceAsync(
            _folder,
            Arg.Is<BackupState>(state => HasRepositoryFiles(state, "last.zip")),
            Arg.Any<CancellationToken>());
        _lockHandle.Received(1).Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_AllFilesMoved_LogsRepositoryFilesAndRecords()
    {
        await ExecuteAsync(await PrepareAsync());

        var started = SingleRecord(_startedEventId);
        (started.GetStructuredStateValue("Repository"), started.GetStructuredStateValue("Files"), started.GetStructuredStateValue("Records"))
            .ShouldBe((_fullName, "3", "1"));
        Records(_fileRecycledEventId).Select(record => record.GetStructuredStateValue("Path"))
            .ShouldBe([$@"{_folder}\old1.zip", $@"{_folder}\old2.zip", $@"{_folder}\extra.zip"]);
        Records(_recordRemovedEventId).Select(record => record.GetStructuredStateValue("File"))
            .ShouldBe(["old1.zip", "old2.zip", "gone.zip"]);
    }

    [Fact]
    public async Task ExecuteAsync_OneFileFails_KeepsItsRecordAndContinues()
    {
        _recycleBin.RecycleAsync($@"{_folder}\old1.zip", Arg.Any<CancellationToken>())
            .Returns(RecycleOutcome.Failed("The file is in use."));

        var result = await ExecuteAsync(await PrepareAsync());

        result.RecycledFiles.Select(file => file.File).ShouldBe(["old2.zip", "extra.zip"]);
        result.Errors.ShouldBe([$@"'{_folder}\old1.zip': The file is in use."]);
        await _stateStore.Received(1).ReplaceAsync(
            _folder,
            Arg.Is<BackupState>(state => HasRepositoryFiles(state, "old1.zip", "last.zip")),
            Arg.Any<CancellationToken>());
        var record = SingleRecord(_fileFailedEventId);
        record.Level.ShouldBe(LogLevel.Warning);
        record.GetStructuredStateValue("Error").ShouldBe("The file is in use.");
    }

    [Fact]
    public async Task ExecuteAsync_NoRecordRemoved_DoesNotWriteState()
    {
        _state = BackupState.CreateEmpty(_id, _fullName)
            .WithArchive(BackupTarget.Repository, CreateRecord("last.zip", 3), _fullName);
        _historyProvider.LoadAsync(_folder, _id, _fullName, Arg.Any<CancellationToken>()).Returns(new ArchiveHistory(_folder, true,
        [
            CreateEntry("extra.zip", 4, exists: true, isInState: false),
            CreateEntry("last.zip", 3, exists: true, isInState: true),
        ]));

        var result = await ExecuteAsync(await PrepareAsync());

        result.RecycledFiles.ShouldHaveSingleItem().File.ShouldBe("extra.zip");
        result.RemovedRecords.ShouldBeEmpty();
        await _stateStore.DidNotReceive().ReplaceAsync(Arg.Any<string>(), Arg.Any<BackupState>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(nameof(BackupStateException))]
    [InlineData(nameof(IOException))]
    [InlineData(nameof(UnauthorizedAccessException))]
    public async Task ExecuteAsync_StateWriteFails_KeepsMovedFilesAndReportsError(string kind)
    {
        Exception error = kind switch
        {
            nameof(BackupStateException) => new BackupStateException($@"{_folder}\.backup-state.json", ["It belongs to another repository."]),
            nameof(IOException) => new IOException("The disk is full."),
            _ => new UnauthorizedAccessException("Access is denied."),
        };
        _stateStore.ReplaceAsync(Arg.Any<string>(), Arg.Any<BackupState>(), Arg.Any<CancellationToken>()).ThrowsAsync(error);

        var result = await ExecuteAsync(await PrepareAsync());

        result.Status.ShouldBe(ArchiveCleanupStatus.Completed);
        result.RecycledFiles.Count.ShouldBe(3);
        result.RemovedRecords.ShouldBeEmpty();
        result.Errors.ShouldHaveSingleItem().ShouldContain(error.Message);
        SingleRecord(_stateWriteFailedEventId).Level.ShouldBe(LogLevel.Error);
        Records(_recordRemovedEventId).ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_StateUnreadableUnderLock_ThrowsAndReleasesLock()
    {
        var plan = await PrepareAsync();
        _stateStore.LoadAsync(_folder, _id, _fullName, Arg.Any<CancellationToken>())
            .ThrowsAsync(new BackupStateException($@"{_folder}\.backup-state.json", ["broken"]));

        await Should.ThrowAsync<BackupStateException>(() => ExecuteAsync(plan));

        _lockHandle.Received(1).Dispose();
        await _recycleBin.DidNotReceive().RecycleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_CancelledAfterFirstFile_WritesStateForMovedFileAndThrows()
    {
        var plan = await PrepareAsync();
        using var cancellation = new CancellationTokenSource();
        _recycleBin.RecycleAsync($@"{_folder}\old1.zip", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cancellation.Cancel();
            return RecycleOutcome.Success;
        });

        await Should.ThrowAsync<OperationCanceledException>(
            () => _service.ExecuteAsync(_folder, _id, _fullName, plan, cancellation.Token));

        await _recycleBin.Received(1).RecycleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _stateStore.Received(1).ReplaceAsync(
            _folder,
            Arg.Is<BackupState>(state => HasRepositoryFiles(state, "old2.zip", "last.zip")),
            CancellationToken.None);
        _lockHandle.Received(1).Dispose();
    }

    /// <summary>
    /// Prepares the plan of the repository.
    /// </summary>
    /// <returns>The plan.</returns>
    private Task<ArchiveCleanupPlan> PrepareAsync() =>
        _service.PrepareAsync(_folder, _id, _fullName, TestContext.Current.CancellationToken);

    /// <summary>
    /// Runs <paramref name="plan"/>.
    /// </summary>
    /// <param name="plan">The confirmed plan.</param>
    /// <returns>The result.</returns>
    private Task<ArchiveCleanupResult> ExecuteAsync(ArchiveCleanupPlan plan) =>
        _service.ExecuteAsync(_folder, _id, _fullName, plan, TestContext.Current.CancellationToken);

    /// <summary>
    /// Returns the log records with an event id.
    /// </summary>
    /// <param name="eventId">The event id.</param>
    /// <returns>The records in order.</returns>
    private FakeLogRecord[] Records(int eventId) =>
        [.. _logger.Collector.GetSnapshot().Where(record => record.Id.Id == eventId)];

    /// <summary>
    /// Returns the only log record with an event id.
    /// </summary>
    /// <param name="eventId">The event id.</param>
    /// <returns>The record.</returns>
    private FakeLogRecord SingleRecord(int eventId) => Records(eventId).ShouldHaveSingleItem();

    /// <summary>
    /// Returns whether the repository section of <paramref name="state"/> records exactly <paramref name="files"/>.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="files">The expected file names, oldest first.</param>
    /// <returns><see langword="true"/> when the records match.</returns>
    private static bool HasRepositoryFiles(BackupState state, params string[] files) =>
        state.Repository.Archives.Select(record => record.File).SequenceEqual(files);

    /// <summary>
    /// Creates a record created <paramref name="hours"/> hours after the first one.
    /// </summary>
    /// <param name="file">The file name.</param>
    /// <param name="hours">The hours after the first record.</param>
    /// <returns>The record.</returns>
    private static ArchiveRecord CreateRecord(string file, int hours) =>
        new(file, _createdAt.AddHours(hours), "main", "a1b2c3", 10, RefSnapshot.Empty);

    /// <summary>
    /// Creates a history entry of the repository.
    /// </summary>
    /// <param name="file">The file name.</param>
    /// <param name="hours">The hours after the first record.</param>
    /// <param name="exists">Whether the file is on the disk.</param>
    /// <param name="isInState">Whether the state file records the archive.</param>
    /// <returns>The entry.</returns>
    private static ArchiveHistoryEntry CreateEntry(string file, int hours, bool exists, bool isInState) => new(
        BackupTarget.Repository,
        file,
        $@"{_folder}\{file}",
        _createdAt.AddHours(hours),
        isInState ? "main" : null,
        "a1b2c3",
        10,
        [],
        exists,
        isInState,
        exists ? 10 : null);
}
