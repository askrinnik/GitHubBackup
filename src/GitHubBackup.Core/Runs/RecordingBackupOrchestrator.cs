using GitHubBackup.Core.History;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Runs;

/// <summary>
/// Runs a backup through another <see cref="IBackupOrchestrator"/> and adds the finished run to the run history.
/// </summary>
/// <remarks>
/// Every run the inner orchestrator finishes is recorded, a stopped or cancelled one too; a run cancelled before the
/// lock is taken is recorded as cancelled without items before the cancellation is rethrown. The run is written with
/// <see cref="CancellationToken.None"/>, so a cancellation that ends the run does not lose its record. A history that
/// cannot be written is logged and reported in <see cref="BackupRunResult.HistoryWarning"/>: the archives are what the
/// backup protects, so the failure changes neither the result nor its exit code.
/// </remarks>
/// <param name="inner">Runs the backup.</param>
/// <param name="historyStore">Stores the finished run.</param>
/// <param name="runContext">Supplies the identifier of the run, the same as in the log.</param>
/// <param name="timeProvider">Supplies the local start and finish times of the run.</param>
/// <param name="logger">The logger.</param>
public sealed class RecordingBackupOrchestrator(
    IBackupOrchestrator inner,
    IRunHistoryStore historyStore,
    IRunContext runContext,
    TimeProvider timeProvider,
    ILogger<RecordingBackupOrchestrator> logger) : IBackupOrchestrator
{
    /// <summary>The start of the warning shown when the run is not recorded in the run history.</summary>
    public const string HistoryWarningPrefix = "The run was not recorded in the run history. ";

    /// <inheritdoc />
    public async Task<BackupRunResult> RunAsync(BackupRunRequest request, CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetLocalNow();
        BackupRunResult result;
        try
        {
            result = await inner.RunAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RecordAsync(request, new([], [], []) { IsCancelled = true, IsDryRun = request.DryRun }, startedAt);
            throw;
        }

        var warning = await RecordAsync(request, result, startedAt);
        return warning is null ? result : result with { HistoryWarning = warning };
    }

    /// <summary>
    /// Adds the run to the run history.
    /// </summary>
    /// <param name="request">The request the run was started with.</param>
    /// <param name="result">The result of the run.</param>
    /// <param name="startedAt">The local time the run started.</param>
    /// <returns>The warning to show when the run is not recorded, or <see langword="null"/> when it is.</returns>
    private async Task<string?> RecordAsync(BackupRunRequest request, BackupRunResult result, DateTimeOffset startedAt)
    {
        var entry = RunHistoryEntryBuilder.Build(runContext.RunId, startedAt, timeProvider.GetLocalNow(), request, result);
        try
        {
            await historyStore.AddAsync(entry, CancellationToken.None);
        }
        catch (RunHistoryException exception)
        {
            RecordingBackupOrchestratorLog.RunNotRecorded(logger, exception.DatabasePath, exception);
            return HistoryWarningPrefix + exception.Message;
        }

        RecordingBackupOrchestratorLog.RunRecorded(logger, entry.ExitCode);
        return null;
    }
}
