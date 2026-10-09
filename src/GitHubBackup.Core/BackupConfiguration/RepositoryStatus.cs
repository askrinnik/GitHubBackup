namespace GitHubBackup.Core.BackupConfiguration;

/// <summary>
/// Defines whether a repository listed in <c>backup-config.json</c> is still found on GitHub.
/// </summary>
public enum RepositoryStatus
{
    /// <summary>The repository is backed up on every run.</summary>
    Active = 0,

    /// <summary>The repository was not found on GitHub; its clone and archives are kept.</summary>
    Unavailable = 1,
}
