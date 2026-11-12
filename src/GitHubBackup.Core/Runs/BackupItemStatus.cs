namespace GitHubBackup.Core.Runs;

/// <summary>
/// Defines the outcome of one repository in a backup run.
/// </summary>
public enum BackupItemStatus
{
    /// <summary>A new archive was created.</summary>
    Archived,

    /// <summary>Nothing changed since the last archive, or the repository is empty; no archive was created.</summary>
    UpToDate,

    /// <summary>
    /// The repository was processed, but with a warning, such as the default branch used instead of a missing one; an
    /// archive may have been created.
    /// </summary>
    Warning,

    /// <summary>The repository could not be backed up.</summary>
    Failed,

    /// <summary>The repository was not processed, for example because it is marked unavailable.</summary>
    Skipped,

    /// <summary>A dry run found that a backup run would create a new archive.</summary>
    WouldArchive,

    /// <summary>A dry run found no clone, so a backup run would clone the repository first.</summary>
    WouldClone,
}
