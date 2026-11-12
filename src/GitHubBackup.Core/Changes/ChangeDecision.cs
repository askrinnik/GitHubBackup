namespace GitHubBackup.Core.Changes;

/// <summary>
/// Tells the caller what to do with a repository or wiki after <see cref="IChangeDetector"/> checked its clone.
/// </summary>
public enum ChangeDecision
{
    /// <summary>The last archive still matches the clone; no new archive is needed.</summary>
    UpToDate,

    /// <summary>The clone differs from the last archive, or there is none; the clone must be archived.</summary>
    ArchiveNeeded,

    /// <summary>The server repository is empty, so there is nothing to archive.</summary>
    NothingToArchive,

    /// <summary>
    /// There is no clone yet, so nothing can be decided without cloning; only
    /// <see cref="IChangeDetector.PreviewAsync"/> returns it.
    /// </summary>
    CloneNeeded,
}
