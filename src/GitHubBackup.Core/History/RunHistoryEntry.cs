using GitHubBackup.Core.Runs;

namespace GitHubBackup.Core.History;

/// <summary>
/// One backup run as the run history keeps it.
/// </summary>
/// <param name="RunId">The identifier of the run, the same as <see cref="IRunContext.RunId"/> in the log.</param>
/// <param name="StartedAt">The local time the run started, with its offset.</param>
/// <param name="FinishedAt">The local time the run finished, with its offset.</param>
/// <param name="Mode">How the run was started.</param>
/// <param name="IsDryRun">Whether the run only reported what a backup would do.</param>
/// <param name="ExitCode">The exit code the run finished with.</param>
public sealed record RunHistoryEntry(
    Guid RunId, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, RunMode Mode, bool IsDryRun, ExitCode ExitCode)
{
    /// <summary>Gets the login of the account the run was limited to, or <see langword="null"/>.</summary>
    public string? AccountFilter { get; init; }

    /// <summary>Gets the repository the run was limited to as <c>owner/name</c>, or <see langword="null"/>.</summary>
    public string? RepositoryFilter { get; init; }

    /// <summary>Gets the repositories and wikis of the run, in the order they were processed.</summary>
    public IReadOnlyList<RunHistoryItem> Items { get; init; } = [];

    /// <summary>Gets the discovery events of the run, in the order they were found.</summary>
    public IReadOnlyList<DiscoveryHistoryEvent> DiscoveryEvents { get; init; } = [];

    /// <summary>
    /// Returns the number of items that finished with <paramref name="status"/>.
    /// </summary>
    /// <param name="status">The status to count.</param>
    /// <returns>The number of items.</returns>
    public int Count(BackupItemStatus status) => Items.Count(item => item.Status == status);
}
