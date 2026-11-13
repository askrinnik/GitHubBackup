using System.Diagnostics;
using System.Diagnostics.Metrics;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.State;

namespace GitHubBackup.Core.Telemetry;

/// <summary>
/// Creates the spans and records the metrics of backup runs under the source and meter <see cref="SourceName"/>.
/// </summary>
/// <remarks>
/// Without a listener, such as the OpenTelemetry SDK, the start methods return <see langword="null"/> and the
/// instruments drop every measurement, so the instrumentation costs almost nothing while telemetry is disabled. Spans
/// never carry an exception message: a message may quote a git command line or a server answer, so an error is
/// reported only as <see cref="ActivityStatusCode.Error"/> and the exception type in <see cref="ErrorTypeTag"/>.
/// </remarks>
public sealed class BackupTelemetry : IDisposable
{
    /// <summary>The name of the <see cref="ActivitySource"/> and of the <see cref="Meter"/>.</summary>
    public const string SourceName = "GitHubBackup";

    /// <summary>The name of the span of a whole run.</summary>
    public const string RunSpanName = "backup.run";

    /// <summary>The name of the span of the discovery of new, missing and renamed repositories.</summary>
    public const string DiscoverySpanName = "backup.discovery";

    /// <summary>The name of the span of one repository or wiki.</summary>
    public const string RepositorySpanName = "backup.repository";

    /// <summary>The operation that creates the clone; each operation span is named after its operation.</summary>
    public const string CloneOperation = "clone";

    /// <summary>The operation that brings an existing clone up to date.</summary>
    public const string FetchOperation = "fetch";

    /// <summary>The operation that reads the refs or the default branch of the server without touching the clone.</summary>
    public const string LsRemoteOperation = "ls-remote";

    /// <summary>The operation that creates and records an archive.</summary>
    public const string ArchiveOperation = "archive";

    /// <summary>The tag that tells whether the run only shows what a backup run would do.</summary>
    public const string DryRunTag = "githubbackup.dry_run";

    /// <summary>The tag of the outcome of a run, one of the <c>*Outcome</c> constants.</summary>
    public const string RunOutcomeTag = "githubbackup.run.outcome";

    /// <summary>The tag of the repository as <c>owner/name</c> or the wiki as <c>owner/name.wiki</c>.</summary>
    public const string RepositoryTag = "githubbackup.repository";

    /// <summary>The tag that tells whether the item is the repository or its wiki, as a <see cref="BackupTarget"/> name.</summary>
    public const string TargetTag = "githubbackup.target";

    /// <summary>The tag of the result of an item, as a <see cref="BackupItemStatus"/> name.</summary>
    public const string StatusTag = "githubbackup.status";

    /// <summary>The tag of the operation of an operation span, one of the <c>*Operation</c> constants.</summary>
    public const string OperationTag = "githubbackup.operation";

    /// <summary>The tag of the type of the exception that failed a span, by the OpenTelemetry convention.</summary>
    public const string ErrorTypeTag = "error.type";

    /// <summary>The outcome of a run that processed its work.</summary>
    public const string CompletedOutcome = "completed";

    /// <summary>The outcome of a run that a critical error stopped before it processed anything.</summary>
    public const string StoppedOutcome = "stopped";

    /// <summary>The outcome of a run that was cancelled.</summary>
    public const string CancelledOutcome = "cancelled";

    /// <summary>The outcome of a run that an unexpected exception ended.</summary>
    public const string FailedOutcome = "failed";

    /// <summary>The name of the counter of repositories and wikis by status.</summary>
    public const string RepositoriesMetric = "githubbackup.repositories";

    /// <summary>The name of the histogram of the time spent on each repository and wiki.</summary>
    public const string RepositoryDurationMetric = "githubbackup.repository.duration";

    /// <summary>The name of the histogram of the time a run takes.</summary>
    public const string RunDurationMetric = "githubbackup.run.duration";

    /// <summary>The name of the histogram of the sizes of the archives created.</summary>
    public const string ArchiveSizeMetric = "githubbackup.archive.size";

    /// <summary>Creates the spans.</summary>
    private readonly ActivitySource _activitySource = new(SourceName);

    /// <summary>Owns the instruments.</summary>
    private readonly Meter _meter;

    /// <summary>Counts the repositories and wikis of each run by status.</summary>
    private readonly Counter<long> _repositories;

    /// <summary>Records the time spent on each repository and wiki.</summary>
    private readonly Histogram<double> _repositoryDuration;

    /// <summary>Records the time each run takes.</summary>
    private readonly Histogram<double> _runDuration;

    /// <summary>Records the size of each archive created.</summary>
    private readonly Histogram<long> _archiveSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackupTelemetry"/> class.
    /// </summary>
    /// <param name="meterFactory">Creates the meter, so each container, and each test, has its own instruments.</param>
    public BackupTelemetry(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(SourceName);
        _repositories = _meter.CreateCounter<long>(
            RepositoriesMetric, "{repository}", "The repositories and wikis of each run, by status.");
        _repositoryDuration = _meter.CreateHistogram<double>(
            RepositoryDurationMetric, "s", "The time spent on each repository and wiki.");
        _runDuration = _meter.CreateHistogram<double>(RunDurationMetric, "s", "The time each run takes.");
        _archiveSize = _meter.CreateHistogram<long>(ArchiveSizeMetric, "By", "The size of each archive created.");
    }

    /// <summary>
    /// Starts the span of a run.
    /// </summary>
    /// <param name="dryRun">Whether the run only shows what a backup run would do.</param>
    /// <returns>The span, or <see langword="null"/> when nobody listens.</returns>
    public Activity? StartRun(bool dryRun) =>
        _activitySource.StartActivity(RunSpanName)?.SetTag(DryRunTag, dryRun);

    /// <summary>
    /// Starts the span of the discovery as a child of the current span.
    /// </summary>
    /// <returns>The span, or <see langword="null"/> when nobody listens.</returns>
    public Activity? StartDiscovery() => _activitySource.StartActivity(DiscoverySpanName);

    /// <summary>
    /// Starts the span of a repository or wiki as a child of the current span.
    /// </summary>
    /// <param name="fullName">The repository as <c>owner/name</c> or the wiki as <c>owner/name.wiki</c>.</param>
    /// <param name="target">Whether the item is the repository or its wiki.</param>
    /// <returns>The span, or <see langword="null"/> when nobody listens.</returns>
    public Activity? StartRepository(string fullName, BackupTarget target) =>
        _activitySource.StartActivity(RepositorySpanName)
            ?.SetTag(RepositoryTag, fullName)
            .SetTag(TargetTag, target.ToString());

    /// <summary>
    /// Starts the span of an operation as a child of the current span.
    /// </summary>
    /// <param name="operation">The operation, one of the <c>*Operation</c> constants.</param>
    /// <returns>The span, or <see langword="null"/> when nobody listens.</returns>
    public Activity? StartOperation(string operation) =>
        _activitySource.StartActivity(operation)?.SetTag(OperationTag, operation);

    /// <summary>
    /// Runs <paramref name="action"/> inside the span of an operation and marks the span failed when it throws.
    /// </summary>
    /// <typeparam name="T">The result of the operation.</typeparam>
    /// <param name="operation">The operation, one of the <c>*Operation</c> constants.</param>
    /// <param name="action">The operation.</param>
    /// <returns>The result of <paramref name="action"/>.</returns>
    public async Task<T> TraceOperationAsync<T>(string operation, Func<Task<T>> action)
    {
        using var activity = StartOperation(operation);
        try
        {
            return await action();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetError(activity, exception);
            throw;
        }
    }

    /// <summary>
    /// Counts a repository or wiki of a finished run and records the time spent on it.
    /// </summary>
    /// <param name="item">The result of the item.</param>
    /// <param name="dryRun">Whether the run only showed what a backup run would do.</param>
    public void RecordItem(BackupItemResult item, bool dryRun)
    {
        var tags = new TagList
        {
            { StatusTag, item.Status.ToString() },
            { TargetTag, item.Target.ToString() },
            { DryRunTag, dryRun },
        };
        _repositories.Add(1, tags);
        _repositoryDuration.Record(item.Duration.TotalSeconds, tags);
    }

    /// <summary>
    /// Records the time a run took.
    /// </summary>
    /// <param name="outcome">The outcome, one of the <c>*Outcome</c> constants.</param>
    /// <param name="dryRun">Whether the run only showed what a backup run would do.</param>
    /// <param name="duration">The time the run took.</param>
    public void RecordRun(string outcome, bool dryRun, TimeSpan duration) =>
        _runDuration.Record(
            duration.TotalSeconds,
            new KeyValuePair<string, object?>(RunOutcomeTag, outcome),
            new KeyValuePair<string, object?>(DryRunTag, dryRun));

    /// <summary>
    /// Records the size of an archive created.
    /// </summary>
    /// <param name="sizeBytes">The size of the archive in bytes.</param>
    /// <param name="target">Whether the archive holds the repository or its wiki.</param>
    public void RecordArchiveSize(long sizeBytes, BackupTarget target) =>
        _archiveSize.Record(sizeBytes, new KeyValuePair<string, object?>(TargetTag, target.ToString()));

    /// <summary>
    /// Tags a span with the result of its item and marks it failed for <see cref="BackupItemStatus.Failed"/>.
    /// </summary>
    /// <param name="activity">The span, or <see langword="null"/>.</param>
    /// <param name="status">The result of the item.</param>
    /// <param name="exception">The exception that failed the item, or <see langword="null"/>.</param>
    public static void SetStatus(Activity? activity, BackupItemStatus status, Exception? exception = null)
    {
        activity?.SetTag(StatusTag, status.ToString());
        if (status == BackupItemStatus.Failed)
        {
            SetError(activity, exception);
        }
    }

    /// <summary>
    /// Marks a span failed with the type of the exception, without its message.
    /// </summary>
    /// <param name="activity">The span, or <see langword="null"/>.</param>
    /// <param name="exception">The exception, or <see langword="null"/> when the failure has none.</param>
    public static void SetError(Activity? activity, Exception? exception)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error);
        if (exception is not null)
        {
            activity.SetTag(ErrorTypeTag, exception.GetType().FullName);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _activitySource.Dispose();
        _meter.Dispose();
    }
}
