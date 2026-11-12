namespace GitHubBackup.Core.Runs;

/// <summary>
/// Runs a backup of the repositories listed in <c>backup-config.json</c>.
/// </summary>
/// <remarks>
/// The run loads the configuration, checks the tools, obtains the token and empties the folder of temporary archives.
/// It then discovers new, missing and renamed repositories, applies the decisions about them to the configuration,
/// and processes the repositories of the updated configuration one by one, so a repository confirmed in this run is
/// backed up in this run. A failure of one repository is recorded in its result and the run goes on; an invalid
/// configuration, a missing tool or a failure of discovery stops the run before any repository is processed and is
/// reported in <see cref="BackupRunResult.CriticalErrors"/>, not thrown.
/// <para>
/// A dry run (<see cref="BackupRunRequest.DryRun"/>) takes the same steps up to the decisions but changes nothing: it
/// leaves the folder of temporary archives as it is, reports what discovery found in
/// <see cref="BackupRunResult.PlannedDiscovery"/> without deciding about it, skips the repositories missing on GitHub,
/// treats the returned ones as active, and for each repository and wiki only reads the refs of the server to report
/// <see cref="BackupItemStatus.WouldClone"/>, <see cref="BackupItemStatus.WouldArchive"/> with the name of the
/// archive a backup run would create, or <see cref="BackupItemStatus.UpToDate"/>.
/// </para>
/// </remarks>
public interface IBackupOrchestrator
{
    /// <summary>
    /// Runs the backup.
    /// </summary>
    /// <param name="request">The repositories the run covers and the handler of discovery decisions.</param>
    /// <param name="cancellationToken">Cancels the run; the repository being processed is stopped and no further one is started.</param>
    /// <returns>The results of the repositories, the changes made by discovery and the errors that stopped the run.</returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled, or the decision handler cancelled.
    /// </exception>
    Task<BackupRunResult> RunAsync(BackupRunRequest request, CancellationToken cancellationToken);
}
