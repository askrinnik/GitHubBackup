using GitHubBackup.Core.Runs;

namespace GitHubBackup.App.Runs;

/// <summary>
/// Starts a backup run of the desktop application through the same orchestrator the console application uses.
/// </summary>
public interface IBackupRunService
{
    /// <summary>
    /// Runs a backup as a new run with its own identifier, off the UI thread.
    /// </summary>
    /// <param name="filter">The repositories the run covers.</param>
    /// <param name="dryRun">Whether the run only shows what a backup run would do.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The result of the run.</returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled before the orchestrator took the lock.
    /// </exception>
    Task<BackupRunResult> RunAsync(BackupRunFilter filter, bool dryRun, CancellationToken cancellationToken);
}
