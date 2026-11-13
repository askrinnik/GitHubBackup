namespace GitHubBackup.Core.Overview;

/// <summary>
/// Names the state of a repository in <c>backup-config.json</c> as the overview shows it.
/// </summary>
public enum RepositoryOverviewStatus
{
    /// <summary>The repository is active and no <c>exclude</c> mask matches it, so a run backs it up.</summary>
    Active,

    /// <summary>The repository is marked unavailable because GitHub no longer returns it, so a run skips it.</summary>
    Unavailable,

    /// <summary>An <c>exclude</c> mask matches the repository, so a run skips it whatever its status.</summary>
    Excluded,
}
