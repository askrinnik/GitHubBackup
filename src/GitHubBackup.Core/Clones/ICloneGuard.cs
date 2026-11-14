using GitHubBackup.Core.Git;
using GitHubBackup.Core.Processes;

namespace GitHubBackup.Core.Clones;

/// <summary>
/// Checks an existing clone for local work before anything changes its working tree, its branches or its folder.
/// </summary>
/// <remarks>
/// The check only reads: it never fetches, so a clone with local work stays exactly as the user left it. The branch
/// an update would check out comes from the server through <c>git ls-remote</c>; unpushed commits are counted against
/// the remote-tracking branches the clone already has.
/// </remarks>
public interface ICloneGuard
{
    /// <summary>
    /// Finds the local work in the clone of <see cref="GitRepositoryRequest.ClonePath"/>: uncommitted changes,
    /// untracked files, another branch or a detached <c>HEAD</c>, and unpushed commits, also those of the local branch
    /// an update would reset when the accepted branch is checked out.
    /// </summary>
    /// <param name="git">
    /// The git executable, the existing clone folder, the configured branch, or <see langword="null"/> for the
    /// server's default branch, and the token.
    /// </param>
    /// <param name="remoteRefs">
    /// The branches and tags of the server when the caller has just read them, or <see langword="null"/> to read them.
    /// </param>
    /// <param name="acceptedBranch">
    /// A branch accepted besides the expected one, because the program itself checked it out for the last archive, so
    /// a change of the configured branch can still switch the clone; <see langword="null"/> accepts no other branch.
    /// </param>
    /// <param name="cancellationToken">Cancels the git commands.</param>
    /// <returns>The problems found, in a fixed order.</returns>
    /// <exception cref="GitCommandException">A git command fails.</exception>
    /// <exception cref="ProcessStartException">git cannot be started.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<CloneCheckResult> CheckAsync(
        GitRepositoryRequest git, RefSnapshot? remoteRefs, string? acceptedBranch, CancellationToken cancellationToken);

    /// <summary>
    /// Repeats the check of <see cref="CheckAsync"/> with a branch already determined, without the network, so the
    /// clone can be checked again right before its working tree changes.
    /// </summary>
    /// <param name="git">The git executable and the existing clone folder.</param>
    /// <param name="expectedBranch">
    /// The branch an update would check out, as <see cref="CloneCheckResult.ExpectedBranch"/> holds it, or
    /// <see langword="null"/> to skip the branch.
    /// </param>
    /// <param name="acceptedBranch">A branch accepted besides the expected one, or <see langword="null"/>.</param>
    /// <param name="knownRemoteRefs">
    /// The remote-tracking branches as they were before a fetch, so commits the server dropped by a force-push do not
    /// count as unpushed; <see langword="null"/> counts against the current ones.
    /// </param>
    /// <param name="cancellationToken">Cancels the git commands.</param>
    /// <returns>The problems found, in a fixed order.</returns>
    /// <exception cref="GitCommandException">A git command fails.</exception>
    /// <exception cref="ProcessStartException">git cannot be started.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<CloneCheckResult> CheckLocalAsync(
        GitRepositoryRequest git,
        string? expectedBranch,
        string? acceptedBranch,
        RefSnapshot? knownRemoteRefs,
        CancellationToken cancellationToken);
}
