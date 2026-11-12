using GitHubBackup.Core.Git;
using GitHubBackup.Core.Processes;

namespace GitHubBackup.Core.Changes;

/// <summary>
/// Decides whether a repository or wiki needs a new archive and brings its clone up to date on the way.
/// </summary>
/// <remarks>
/// A new archive is needed when no archive exists, its file is missing, or a branch, a tag or the branch to archive
/// changed. A missing clone is cloned, so a clone deleted from disk does not produce an archive by itself. When the
/// last archive is intact, the clone is checked with a read of the server's refs and is fetched only when they
/// differ.
/// </remarks>
public interface IChangeDetector
{
    /// <summary>
    /// Clones or updates the clone as needed and decides whether it must be archived.
    /// </summary>
    /// <param name="request">The clone, the server URL, the archive folder and the last archive.</param>
    /// <param name="cancellationToken">Cancels the git commands.</param>
    /// <returns>The decision, its reason, the branch checked out and the refs of the server.</returns>
    /// <exception cref="ArgumentException">
    /// The archive folder is not fully qualified, or <see cref="IGitClient"/> rejects the request.
    /// </exception>
    /// <exception cref="GitBranchNotFoundException">
    /// The server has branches, but neither the requested branch nor the default branch is among them.
    /// </exception>
    /// <exception cref="GitRepositoryNotFoundException">The server has no repository at the remote URL.</exception>
    /// <exception cref="GitCommandException">A git command exits with a non-zero code.</exception>
    /// <exception cref="ProcessStartException">git cannot be started.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<ChangeDetection> DetectAsync(ChangeDetectionRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Decides what <see cref="DetectAsync"/> would decide, by the same rules, without cloning, fetching or changing
    /// the clone.
    /// </summary>
    /// <remarks>
    /// Without a clone the result is <see cref="ChangeDecision.CloneNeeded"/> and git is not called. Otherwise the
    /// refs of the server stand in for the refs the clone would hold after an update, so
    /// <see cref="ChangeDetection.Refs"/> holds the server's branches and tags.
    /// </remarks>
    /// <param name="request">The clone, the server URL, the archive folder and the last archive.</param>
    /// <param name="cancellationToken">Cancels the git commands.</param>
    /// <returns>The decision, its reason, the branch an update would check out and the refs of the server.</returns>
    /// <exception cref="ArgumentException">
    /// The archive folder is not fully qualified, or <see cref="IGitClient"/> rejects the request.
    /// </exception>
    /// <exception cref="GitBranchNotFoundException">
    /// The server has branches, but neither the requested branch nor the default branch is among them.
    /// </exception>
    /// <exception cref="GitRepositoryNotFoundException">The server has no repository at the remote URL.</exception>
    /// <exception cref="GitCommandException">A git command exits with a non-zero code.</exception>
    /// <exception cref="ProcessStartException">git cannot be started.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<ChangeDetection> PreviewAsync(ChangeDetectionRequest request, CancellationToken cancellationToken);
}
