namespace GitHubBackup.Core.Discovery;

/// <summary>
/// Names what happens to the archive folder of a renamed repository whose folder is derived from its name.
/// </summary>
public enum RenameDecision
{
    /// <summary>The archive folder is renamed after the repository, so new archives land next to the old ones.</summary>
    RenameArchiveFolder,

    /// <summary>The archive folder keeps its name and is written to the <c>path</c> of the entry.</summary>
    KeepArchiveFolder,
}
