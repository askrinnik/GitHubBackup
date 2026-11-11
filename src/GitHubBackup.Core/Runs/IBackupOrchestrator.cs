namespace GitHubBackup.Core.Runs;

/// <summary>
/// Runs a backup of the repositories listed in <c>backup-config.json</c>.
/// </summary>
/// <remarks>
/// The run loads the configuration, checks the tools, obtains the token, empties the folder of temporary archives and
/// then processes the repositories one by one. A failure of one repository is recorded in its result and the run goes
/// on; an invalid configuration or a missing tool stops the run before any repository or file is touched and is
/// reported in <see cref="BackupRunResult.CriticalErrors"/>, not thrown.
/// </remarks>
public interface IBackupOrchestrator
{
    /// <summary>
    /// Runs the backup.
    /// </summary>
    /// <param name="cancellationToken">Cancels the run; the repository being processed is stopped and no further one is started.</param>
    /// <returns>The results of the repositories and the errors that stopped the run.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<BackupRunResult> RunAsync(CancellationToken cancellationToken);
}
