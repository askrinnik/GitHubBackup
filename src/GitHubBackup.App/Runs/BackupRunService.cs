using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.Runs;

namespace GitHubBackup.App.Runs;

/// <summary>
/// Runs the backup with <see cref="RunMode.Ui"/> and the decision handler of the desktop application.
/// </summary>
/// <remarks>
/// The orchestrator runs on the thread pool: the core does not use <c>ConfigureAwait(false)</c>, so awaited from the
/// UI thread its continuations would run on that thread and freeze the window during a run.
/// </remarks>
/// <param name="orchestrator">Runs the backup.</param>
/// <param name="runContext">Starts a new run identifier for each run, so the run history keeps each run.</param>
/// <param name="decisionHandler">Decides about new and renamed repositories.</param>
internal sealed class BackupRunService(
    IBackupOrchestrator orchestrator,
    IRunContext runContext,
    IDiscoveryDecisionHandler decisionHandler) : IBackupRunService
{
    /// <inheritdoc />
    public Task<BackupRunResult> RunAsync(BackupRunFilter filter, bool dryRun, CancellationToken cancellationToken)
    {
        runContext.BeginRun();
        var request = new BackupRunRequest(filter, decisionHandler) { DryRun = dryRun, Mode = RunMode.Ui };
        return Task.Run(() => orchestrator.RunAsync(request, cancellationToken), cancellationToken);
    }
}
