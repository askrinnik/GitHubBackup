using GitHubBackup.App.Discovery;
using GitHubBackup.Core.Runs;

namespace GitHubBackup.App.Runs;

/// <summary>
/// Runs the backup with <see cref="RunMode.Ui"/> and a decision handler of the desktop application created for the run.
/// </summary>
/// <remarks>
/// The orchestrator runs on the thread pool: the core does not use <c>ConfigureAwait(false)</c>, so awaited from the
/// UI thread its continuations would run on that thread and freeze the window during a run. The token is not passed
/// to <see cref="Task.Run(Func{Task}, CancellationToken)"/>: a token cancelled before the task starts would skip the
/// orchestrator, and the run would then not be recorded in the run history. The orchestrator receives a token linked
/// to the caller's, which the decision handler cancels when the user cancels the discovery dialog, so that run is
/// recorded as cancelled like one the caller cancelled.
/// </remarks>
/// <param name="orchestrator">Runs the backup.</param>
/// <param name="runContext">Starts a new run identifier for each run, so the run history keeps each run.</param>
/// <param name="handlerFactory">Creates the handler that decides about new and renamed repositories.</param>
internal sealed class BackupRunService(
    IBackupOrchestrator orchestrator,
    IRunContext runContext,
    IDiscoveryDecisionHandlerFactory handlerFactory) : IBackupRunService
{
    /// <inheritdoc />
    public async Task<BackupRunResult> RunAsync(
        BackupRunFilter filter, bool dryRun, IProgress<BackupProgress>? progress, CancellationToken cancellationToken)
    {
        runContext.BeginRun();
        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var request = new BackupRunRequest(filter, handlerFactory.Create(run.Cancel))
        {
            DryRun = dryRun,
            Mode = RunMode.Ui,
            Progress = progress,
        };
        var token = run.Token;
        return await Task.Run(() => orchestrator.RunAsync(request, token));
    }
}
