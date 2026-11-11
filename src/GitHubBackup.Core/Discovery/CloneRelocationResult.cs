namespace GitHubBackup.Core.Discovery;

/// <summary>
/// Reports what <see cref="ICloneRelocator"/> did with the clone of a renamed repository.
/// </summary>
public enum CloneRelocationResult
{
    /// <summary>There was no clone under the old name; the next backup clones under the new one.</summary>
    NotPresent,

    /// <summary>The clone is under the new name and its <c>origin</c> points at the new URL.</summary>
    Moved,

    /// <summary>The clone could not be moved or repointed and was deleted; the next backup clones it again.</summary>
    DeletedForReclone,

    /// <summary>The clone could not be moved and could not be deleted; it is left where it was.</summary>
    DeleteFailed,
}
