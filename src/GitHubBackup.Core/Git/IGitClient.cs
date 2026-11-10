using GitHubBackup.Core.Processes;

namespace GitHubBackup.Core.Git;

/// <summary>
/// Creates and updates the local clone of a repository as a read-only mirror of the server.
/// </summary>
/// <remarks>
/// Every git command logs its output and fails by its exit code only. The token reaches git only through the
/// environment of each process, never through arguments, the remote URL or files inside the clone.
/// </remarks>
public interface IGitClient
{
    /// <summary>
    /// Clones <paramref name="remoteUrl"/> into <see cref="GitRepositoryRequest.ClonePath"/> with every branch and
    /// tag, checks out <see cref="GitRepositoryRequest.Branch"/>, initializes the submodules recursively and fetches
    /// the LFS files of that branch only.
    /// </summary>
    /// <param name="request">The git executable, the clone folder, the branch and the token.</param>
    /// <param name="remoteUrl">The URL of the repository on the server, without credentials.</param>
    /// <param name="cancellationToken">Cancels the clone and kills the running git process.</param>
    /// <returns>A task that completes when the clone is checked out.</returns>
    /// <exception cref="ArgumentException">
    /// The clone path is not fully qualified, the branch is empty or starts with <c>-</c>, or
    /// <paramref name="remoteUrl"/> is empty.
    /// </exception>
    /// <exception cref="GitCommandException">A git command exits with a non-zero code.</exception>
    /// <exception cref="ProcessStartException">git cannot be started.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task CloneAsync(GitRepositoryRequest request, string remoteUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches every branch and tag from <c>origin</c>, pruning what the server deleted, and resets the clone to
    /// <see cref="GitRepositoryRequest.Branch"/>; local changes, untracked and ignored files are discarded.
    /// </summary>
    /// <param name="request">The git executable, the existing clone folder, the branch and the token.</param>
    /// <param name="cancellationToken">Cancels the update and kills the running git process.</param>
    /// <returns>A task that completes when the clone matches the server.</returns>
    /// <exception cref="ArgumentException">
    /// The clone path is not fully qualified, or the branch is empty or starts with <c>-</c>.
    /// </exception>
    /// <exception cref="GitCommandException">A git command exits with a non-zero code.</exception>
    /// <exception cref="ProcessStartException">git cannot be started or the clone folder does not exist.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task UpdateAsync(GitRepositoryRequest request, CancellationToken cancellationToken);
}
