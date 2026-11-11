namespace GitHubBackup.Core.Git;

/// <summary>
/// Describes the state of a clone after <see cref="IGitClient.CloneAsync"/> or <see cref="IGitClient.UpdateAsync"/>.
/// </summary>
/// <param name="Branch">
/// The branch checked out, or <see langword="null"/> when the server has no branch and nothing was checked out.
/// </param>
/// <param name="BranchFallback">
/// Whether the requested branch is missing on the server and the server's default branch was checked out instead;
/// the caller reports this as a warning.
/// </param>
/// <param name="Refs">The branches and tags of the clone after the synchronization.</param>
public sealed record GitSyncResult(string? Branch, bool BranchFallback, RefSnapshot Refs)
{
    /// <summary>
    /// Gets a value indicating whether the server has no branch, so the clone holds nothing to archive.
    /// </summary>
    public bool IsEmpty => Branch is null;
}
