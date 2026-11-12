namespace GitHubBackup.Core.History;

/// <summary>
/// Defines the decision taken on a discovery event.
/// </summary>
public enum DiscoveryEventDecision
{
    /// <summary>The new repository was added to <c>backup-config.json</c>.</summary>
    Added,

    /// <summary>The new repository was left out for this run and is proposed again in the next one.</summary>
    Skipped,

    /// <summary>The new repository was excluded and is not proposed again.</summary>
    Excluded,

    /// <summary>The clone and archive folders of the renamed repository were moved to the new name.</summary>
    FolderRenamed,

    /// <summary>The renamed repository was given new folders and the old ones were kept.</summary>
    OldFolderKept,
}
