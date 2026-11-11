namespace GitHubBackup.Core.Runs;

/// <summary>
/// The outcome of a backup run: one result per repository, the warnings about <c>backup-config.json</c> and the
/// errors that stopped the run before any repository was processed.
/// </summary>
/// <param name="Items">The results of the repositories, in the order they were processed.</param>
/// <param name="ConfigWarnings">The warnings found while validating <c>backup-config.json</c>.</param>
/// <param name="CriticalErrors">The errors that stopped the run, one sentence each; empty when the run went through.</param>
public sealed record BackupRunResult(
    IReadOnlyList<BackupItemResult> Items, IReadOnlyList<string> ConfigWarnings, IReadOnlyList<string> CriticalErrors)
{
    /// <summary>
    /// Gets the exit code of the run: <see cref="ExitCode.Critical"/> when the run was stopped,
    /// <see cref="ExitCode.RepositoriesFailed"/> when a repository is <see cref="BackupItemStatus.Failed"/>, and
    /// <see cref="ExitCode.Success"/> otherwise.
    /// </summary>
    public ExitCode ExitCode =>
        CriticalErrors.Count > 0 ? ExitCode.Critical
        : Items.Any(item => item.Status == BackupItemStatus.Failed) ? ExitCode.RepositoriesFailed
        : ExitCode.Success;

    /// <summary>
    /// Creates the result of a run that was stopped before any repository was processed.
    /// </summary>
    /// <param name="errors">The errors that stopped the run.</param>
    /// <param name="configWarnings">The warnings about <c>backup-config.json</c> found before the run stopped.</param>
    /// <returns>The result without items.</returns>
    public static BackupRunResult Stopped(IReadOnlyList<string> errors, IReadOnlyList<string> configWarnings) =>
        new([], configWarnings, errors);

    /// <summary>
    /// Returns how many repositories finished with <paramref name="status"/>.
    /// </summary>
    /// <param name="status">The status to count.</param>
    /// <returns>The number of items with that status.</returns>
    public int Count(BackupItemStatus status) => Items.Count(item => item.Status == status);
}
