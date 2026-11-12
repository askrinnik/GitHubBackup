using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.History;
using GitHubBackup.Core.Runs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GitHubBackup.Core.Tests.Runs;

/// <summary>
/// Tests <see cref="RecordingBackupOrchestrator"/> with a substitute inner orchestrator and run history store and a
/// fake clock.
/// </summary>
public sealed class RecordingBackupOrchestratorTests
{
    private const int _runRecordedEventId = 1;
    private const int _runNotRecordedEventId = 2;
    private const string _databasePath = @"C:\app\data\history.db";

    private static readonly Guid _runId = new("6f9619ff-8b86-d011-b42d-00c04fc964ff");
    private static readonly DateTimeOffset _startedAt = new(2026, 11, 12, 10, 0, 0, TimeSpan.FromHours(1));

    private readonly IBackupOrchestrator _inner = Substitute.For<IBackupOrchestrator>();
    private readonly IRunHistoryStore _store = Substitute.For<IRunHistoryStore>();
    private readonly IRunContext _runContext = Substitute.For<IRunContext>();
    private readonly FakeTimeProvider _timeProvider = new(_startedAt.ToUniversalTime());
    private readonly FakeLogger<RecordingBackupOrchestrator> _logger = new();
    private readonly BackupRunRequest _request = new(BackupRunFilter.All, Substitute.For<IDiscoveryDecisionHandler>())
    {
        Mode = RunMode.Silent,
    };

    private RunHistoryEntry? _recorded;

    /// <summary>
    /// Sets up a fixed run identifier, a local time zone of the fake clock and a store that keeps the added run.
    /// </summary>
    public RecordingBackupOrchestratorTests()
    {
        _timeProvider.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(1), "Test", "Test"));
        _runContext.RunId.Returns(_runId);
        _store.AddAsync(Arg.Do<RunHistoryEntry>(entry => _recorded = entry), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RunAsync_RunFinishes_RecordsItWithRunIdTimesAndItemsAndReturnsResult()
    {
        var result = new BackupRunResult(
            [new("owner/repo", BackupItemStatus.Archived, "repo.zip", TimeSpan.FromSeconds(5), null)], [], []);
        SetupInner(result, TimeSpan.FromMinutes(2));

        var returned = await CreateOrchestrator().RunAsync(_request, CancellationToken);

        returned.ShouldBeSameAs(result);
        var entry = _recorded.ShouldNotBeNull();
        entry.RunId.ShouldBe(_runId);
        entry.StartedAt.ShouldBe(_startedAt);
        entry.FinishedAt.ShouldBe(_startedAt.AddMinutes(2));
        entry.Mode.ShouldBe(RunMode.Silent);
        entry.ExitCode.ShouldBe(ExitCode.Success);
        entry.Items.ShouldHaveSingleItem().ArchiveFileName.ShouldBe("repo.zip");
        await _inner.Received(1).RunAsync(_request, CancellationToken);
        var record = SingleRecord(_runRecordedEventId);
        record.Level.ShouldBe(LogLevel.Information);
        record.GetStructuredStateValue("ExitCode").ShouldBe(nameof(ExitCode.Success));
    }

    [Fact]
    public async Task RunAsync_CancelledRunReturned_RecordsWithoutCancellationToken()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        SetupInner(new([], [], []) { IsCancelled = true }, TimeSpan.Zero);

        var returned = await CreateOrchestrator().RunAsync(_request, cancellation.Token);

        returned.ExitCode.ShouldBe(ExitCode.Critical);
        _recorded.ShouldNotBeNull().ExitCode.ShouldBe(ExitCode.Critical);
        await _store.Received(1).AddAsync(Arg.Any<RunHistoryEntry>(), CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_StoppedRun_RecordsCriticalWithoutItems()
    {
        SetupInner(BackupRunResult.Stopped([BackupOrchestrator.RunLockHeldMessage], []), TimeSpan.Zero);

        await CreateOrchestrator().RunAsync(_request, CancellationToken);

        var entry = _recorded.ShouldNotBeNull();
        entry.ExitCode.ShouldBe(ExitCode.Critical);
        entry.Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_HistoryFails_KeepsResultAndExitCodeAddsWarningAndLogsError(bool stopped)
    {
        var result = stopped
            ? BackupRunResult.Stopped(["The tools are missing."], [])
            : new([new("owner/repo", BackupItemStatus.Failed, null, TimeSpan.Zero, "git failed.")], ["warning"], []);
        SetupInner(result, TimeSpan.Zero);
        var failure = new RunHistoryException(_databasePath, "The database is locked.");
        _store.AddAsync(Arg.Any<RunHistoryEntry>(), Arg.Any<CancellationToken>()).ThrowsAsync(failure);

        var returned = await CreateOrchestrator().RunAsync(_request, CancellationToken);

        returned.ShouldBe(result with { HistoryWarning = RecordingBackupOrchestrator.HistoryWarningPrefix + failure.Message });
        returned.ExitCode.ShouldBe(result.ExitCode);
        var record = SingleRecord(_runNotRecordedEventId);
        record.Level.ShouldBe(LogLevel.Error);
        record.Exception.ShouldBeSameAs(failure);
        record.GetStructuredStateValue("DatabasePath").ShouldBe(_databasePath);
        _logger.Collector.GetSnapshot().ShouldNotContain(entry => entry.Id.Id == _runRecordedEventId);
    }

    [Fact]
    public async Task RunAsync_CancelledBeforeLock_RecordsCancelledRunWithoutItemsAndRethrows()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var dryRequest = _request with { DryRun = true };
        _inner.RunAsync(dryRequest, cancellation.Token).ThrowsAsync(new OperationCanceledException(cancellation.Token));

        await Should.ThrowAsync<OperationCanceledException>(() => CreateOrchestrator().RunAsync(dryRequest, cancellation.Token));

        var entry = _recorded.ShouldNotBeNull();
        entry.ExitCode.ShouldBe(ExitCode.Critical);
        entry.IsDryRun.ShouldBeTrue();
        entry.Items.ShouldBeEmpty();
        await _store.Received(1).AddAsync(Arg.Any<RunHistoryEntry>(), CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_CancelledBeforeLockAndHistoryFails_LogsErrorAndRethrowsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _inner.RunAsync(_request, cancellation.Token).ThrowsAsync(new OperationCanceledException(cancellation.Token));
        _store.AddAsync(Arg.Any<RunHistoryEntry>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RunHistoryException(_databasePath, "The database is locked."));

        await Should.ThrowAsync<OperationCanceledException>(() => CreateOrchestrator().RunAsync(_request, cancellation.Token));

        SingleRecord(_runNotRecordedEventId).Level.ShouldBe(LogLevel.Error);
    }

    [Fact]
    public async Task RunAsync_CancelledByHandlerWithoutCancellation_RethrowsWithoutRecording()
    {
        _inner.RunAsync(_request, Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() => CreateOrchestrator().RunAsync(_request, CancellationToken));

        await _store.DidNotReceiveWithAnyArgs().AddAsync(default!, CancellationToken);
    }

    [Fact]
    public async Task RunAsync_InnerThrows_RethrowsWithoutRecording()
    {
        _inner.RunAsync(_request, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("Broken."));

        await Should.ThrowAsync<InvalidOperationException>(() => CreateOrchestrator().RunAsync(_request, CancellationToken));

        await _store.DidNotReceiveWithAnyArgs().AddAsync(default!, CancellationToken);
    }

    [Fact]
    public async Task RunAsync_StoreThrowsOtherException_PropagatesIt()
    {
        SetupInner(new([], [], []), TimeSpan.Zero);
        _store.AddAsync(Arg.Any<RunHistoryEntry>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("Bug."));

        await Should.ThrowAsync<InvalidOperationException>(() => CreateOrchestrator().RunAsync(_request, CancellationToken));
    }

    /// <summary>
    /// Makes the inner orchestrator return <paramref name="result"/> after <paramref name="duration"/> of the fake clock.
    /// </summary>
    /// <param name="result">The result of the run.</param>
    /// <param name="duration">How long the run takes.</param>
    private void SetupInner(BackupRunResult result, TimeSpan duration) =>
        _inner.RunAsync(Arg.Any<BackupRunRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _timeProvider.Advance(duration);
            return result;
        });

    /// <summary>
    /// Creates the decorator over the substitutes.
    /// </summary>
    /// <returns>The decorator.</returns>
    private RecordingBackupOrchestrator CreateOrchestrator() =>
        new(_inner, _store, _runContext, _timeProvider, _logger);

    /// <summary>
    /// Returns the only log record with the event id <paramref name="eventId"/> and fails if there is not exactly one.
    /// </summary>
    /// <param name="eventId">The event id of the <see cref="LoggerMessageAttribute"/>.</param>
    /// <returns>The record.</returns>
    private FakeLogRecord SingleRecord(int eventId) =>
        _logger.Collector.GetSnapshot().Where(record => record.Id.Id == eventId).ShouldHaveSingleItem();
}
