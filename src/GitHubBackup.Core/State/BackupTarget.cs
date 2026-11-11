namespace GitHubBackup.Core.State;

/// <summary>
/// Selects the section of the state file that describes the archives of one backup target.
/// </summary>
public enum BackupTarget
{
    /// <summary>The repository itself.</summary>
    Repository,

    /// <summary>The wiki of the repository, archived and decided on independently of the repository.</summary>
    Wiki,
}
