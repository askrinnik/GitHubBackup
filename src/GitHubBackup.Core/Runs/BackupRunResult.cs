using GitHubBackup.Core.Discovery;

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
    /// Gets what the decisions about the discovered repositories changed, or <see langword="null"/> when discovery was
    /// skipped or found nothing.
    /// </summary>
    public DiscoveryApplyResult? Discovery { get; init; }

    /// <summary>Gets whether the run was a dry run, which changed nothing.</summary>
    public bool IsDryRun { get; init; }

    /// <summary>
    /// Gets what discovery found in a dry run, which shows the findings without deciding or saving anything, or
    /// <see langword="null"/> when the run was not a dry run or discovery was skipped.
    /// </summary>
    public DiscoveryResult? PlannedDiscovery { get; init; }

    /// <summary>
    /// Gets whether the run was cancelled before it finished; <see cref="Items"/> then holds what was processed before
    /// the cancellation, the interrupted item and the items not started.
    /// </summary>
    public bool IsCancelled { get; init; }

    /// <summary>
    /// Gets the exit code of the run: <see cref="ExitCode.Critical"/> when the run was stopped or cancelled,
    /// <see cref="ExitCode.RepositoriesFailed"/> when a repository is <see cref="BackupItemStatus.Failed"/>,
    /// <see cref="ExitCode.DiscoveryEvents"/> when discovery found new, missing or renamed repositories, in a dry run
    /// too, and
    /// <see cref="ExitCode.Success"/> otherwise.
    /// </summary>
    /// <remarks>
    /// An unavailable entry that GitHub reports again is not an event: it only returns the run to its normal state.
    /// </remarks>
    public ExitCode ExitCode =>
        CriticalErrors.Count > 0 || IsCancelled ? ExitCode.Critical
        : Items.Any(item => item.Status == BackupItemStatus.Failed) ? ExitCode.RepositoriesFailed
        : HasDiscoveryEvents(Discovery) || HasDiscoveryEvents(PlannedDiscovery) ? ExitCode.DiscoveryEvents
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

    /// <summary>
    /// Returns whether discovery reported a new, missing or renamed repository.
    /// </summary>
    /// <param name="discovery">What the decisions changed, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when there was at least one such repository.</returns>
    private static bool HasDiscoveryEvents(DiscoveryApplyResult? discovery) =>
        discovery is not null
        && (discovery.Added.Count > 0
            || discovery.Skipped.Count > 0
            || discovery.Excluded.Count > 0
            || discovery.MarkedUnavailable.Count > 0
            || discovery.Renamed.Count > 0);

    /// <summary>
    /// Returns whether a dry run's discovery found a new, missing or renamed repository.
    /// </summary>
    /// <param name="discovery">What discovery found, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when there was at least one such repository.</returns>
    private static bool HasDiscoveryEvents(DiscoveryResult? discovery) =>
        discovery is not null
        && (discovery.NewInAccounts.Count > 0
            || discovery.NewStandalone.Count > 0
            || discovery.Missing.Count > 0
            || discovery.Renamed.Count > 0);
}
