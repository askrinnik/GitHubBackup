using GitHubBackup.Core.Cleanup;
using GitHubBackup.Core.Overview;

namespace GitHubBackup.Core.History;

/// <summary>
/// One repository of the configuration with the archive history of the repository and its wiki, or the reason the
/// history cannot be read.
/// </summary>
/// <param name="Repository">The repository with its account and archive folder.</param>
/// <param name="History">
/// The archives of the repository and its wiki, or <see langword="null"/> when <paramref name="Error"/> is set.
/// </param>
/// <param name="Error">
/// Why the state file or the archive folder cannot be read, or <see langword="null"/> when <paramref name="History"/>
/// is set.
/// </param>
/// <param name="CleanupPlan">
/// What the cleanup of old archives would remove, or <see langword="null"/> when <paramref name="Error"/> is set.
/// </param>
/// <param name="WikiStatus">The state of the wiki according to the latest run of the repository.</param>
public sealed record RepositoryArchiveSummary(
    RepositoryOverviewEntry Repository,
    ArchiveHistory? History,
    Exception? Error,
    ArchiveCleanupPlan? CleanupPlan = null,
    WikiStatus WikiStatus = WikiStatus.Unknown);
