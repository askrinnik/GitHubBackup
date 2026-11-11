namespace GitHubBackup.Core.BackupConfiguration;

/// <summary>
/// Names the list of <c>exclude</c> masks in <c>backup-config.json</c> that excluded a repository.
/// </summary>
public enum ExclusionScope
{
    /// <summary>The mask is in <see cref="AccountConfig.Exclude"/> and is matched against the repository name.</summary>
    Account,

    /// <summary>The mask is in <see cref="BackupConfig.Exclude"/> and is matched against <c>owner/name</c>.</summary>
    Global,
}
