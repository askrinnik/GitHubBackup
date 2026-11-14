namespace GitHubBackup.Core.Discovery;

/// <summary>
/// Reports what <see cref="ICloneRelocator"/> did with a clone that has to move to another folder.
/// </summary>
public enum CloneRelocationResult
{
    /// <summary>There was no clone in the old folder; the next backup clones into the new one.</summary>
    NotPresent,

    /// <summary>The clone is in the new folder and its <c>origin</c> points at the new URL.</summary>
    Moved,

    /// <summary>The clone could not be moved or repointed and was deleted; the next backup clones it again.</summary>
    DeletedForReclone,

    /// <summary>The clone could not be moved and could not be deleted; it is left where it was.</summary>
    DeleteFailed,

    /// <summary>
    /// The new folder already exists, so nothing was moved or deleted. The user must clear it before the repository
    /// can be backed up, because the backup would otherwise reuse whatever it holds.
    /// </summary>
    TargetOccupied,

    /// <summary>The clone holds local work, so it stays in the old folder untouched.</summary>
    LocalChanges,

    /// <summary>The clone could not be checked for local work, so it stays in the old folder untouched.</summary>
    CheckFailed,

    /// <summary>The move failed, for example between volumes; the clone stays in the old folder and is not deleted.</summary>
    MoveFailed,

    /// <summary>The clone is in the new folder, but its <c>origin</c> could not be changed; it is not deleted.</summary>
    RemoteUrlNotSet,

    /// <summary>
    /// The <c>origin</c> of the clone in the old folder points at another repository, so the folder is neither moved
    /// nor changed.
    /// </summary>
    ForeignOrigin,
}
