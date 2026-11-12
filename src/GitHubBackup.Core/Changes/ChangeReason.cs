namespace GitHubBackup.Core.Changes;

/// <summary>
/// Explains the <see cref="ChangeDecision"/> of <see cref="IChangeDetector"/>.
/// </summary>
public enum ChangeReason
{
    /// <summary>No archive has been recorded yet.</summary>
    NoArchive,

    /// <summary>The last recorded archive file is missing from the archive folder.</summary>
    ArchiveFileMissing,

    /// <summary>A branch or tag was added, removed or moved since the last archive.</summary>
    RefsChanged,

    /// <summary>The branch to archive differs from the branch of the last archive.</summary>
    BranchChanged,

    /// <summary>Branches, tags, the branch to archive and the archive file are unchanged.</summary>
    Unchanged,

    /// <summary>The server repository has no branch.</summary>
    EmptyRepository,

    /// <summary>There is no clone, so a preview cannot compare anything without cloning.</summary>
    NoClone,
}
