using GitHubBackup.Core.Processes;

namespace GitHubBackup.Core.Git;

/// <summary>
/// Creates and updates the local clone of a repository as a read-only mirror of the server and reads the branches and
/// tags of the server and of the clone.
/// </summary>
/// <remarks>
/// Every git command logs its output and fails by its exit code only. The token reaches git only through the
/// environment of each process, never through arguments, the remote URL or files inside the clone.
/// <para>
/// Clone and update check out <see cref="GitRepositoryRequest.Branch"/> when the server has it. When the branch is
/// not set they check out the server's default branch; when it is missing on the server they fall back to the
/// default branch and report it in <see cref="GitSyncResult.BranchFallback"/>. A server without any branch is a
/// valid, empty repository: nothing is checked out and <see cref="GitSyncResult.IsEmpty"/> is set.
/// </para>
/// </remarks>
public interface IGitClient
{
    /// <summary>
    /// Clones <paramref name="remoteUrl"/> into <see cref="GitRepositoryRequest.ClonePath"/> with every branch and
    /// tag, checks out the branch, initializes the submodules recursively and fetches the LFS files of that branch
    /// only.
    /// </summary>
    /// <param name="request">The git executable, the clone folder, the branch and the token.</param>
    /// <param name="remoteUrl">The URL of the repository on the server, without credentials.</param>
    /// <param name="cancellationToken">Cancels the clone and kills the running git process.</param>
    /// <returns>The branch checked out and the branches and tags of the new clone.</returns>
    /// <exception cref="ArgumentException">
    /// The clone path is not fully qualified, the branch or the server's default branch is empty or starts with
    /// <c>-</c>, or <paramref name="remoteUrl"/> is empty.
    /// </exception>
    /// <exception cref="GitBranchNotFoundException">
    /// The server has branches, but neither the requested branch nor the default branch is among them.
    /// </exception>
    /// <exception cref="GitCommandException">A git command exits with a non-zero code or lists refs that cannot be read.</exception>
    /// <exception cref="ProcessStartException">git cannot be started.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<GitSyncResult> CloneAsync(GitRepositoryRequest request, string remoteUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches every branch and tag from <c>origin</c>, pruning what the server deleted, and resets the clone to the
    /// branch; local changes, untracked and ignored files are discarded.
    /// </summary>
    /// <param name="request">The git executable, the existing clone folder, the branch and the token.</param>
    /// <param name="cancellationToken">Cancels the update and kills the running git process.</param>
    /// <returns>The branch checked out and the branches and tags of the clone after the update.</returns>
    /// <exception cref="ArgumentException">
    /// The clone path is not fully qualified, or the branch or the server's default branch is empty or starts with
    /// <c>-</c>.
    /// </exception>
    /// <exception cref="GitBranchNotFoundException">
    /// The server has branches, but neither the requested branch nor the default branch is among them.
    /// </exception>
    /// <exception cref="GitCommandException">A git command exits with a non-zero code or lists refs that cannot be read.</exception>
    /// <exception cref="ProcessStartException">git cannot be started or the clone folder does not exist.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<GitSyncResult> UpdateAsync(GitRepositoryRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the branches and tags of <c>origin</c> from the server without changing the clone.
    /// </summary>
    /// <param name="request">The git executable, the existing clone folder and the token; the branch is ignored.</param>
    /// <param name="cancellationToken">Cancels the command and kills the running git process.</param>
    /// <returns>The server's branches and tags; other server refs, such as pull requests, are left out.</returns>
    /// <exception cref="ArgumentException">The clone path is not fully qualified.</exception>
    /// <exception cref="GitCommandException">The git command exits with a non-zero code or lists refs that cannot be read.</exception>
    /// <exception cref="ProcessStartException">git cannot be started or the clone folder does not exist.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<RefSnapshot> GetRemoteRefsAsync(GitRepositoryRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the remote-tracking branches of <c>origin</c> and the tags stored in the clone, without the network.
    /// </summary>
    /// <param name="request">The git executable and the existing clone folder; the branch and the token are ignored.</param>
    /// <param name="cancellationToken">Cancels the command and kills the running git process.</param>
    /// <returns>The clone's view of the server's branches and tags.</returns>
    /// <exception cref="ArgumentException">The clone path is not fully qualified.</exception>
    /// <exception cref="GitCommandException">The git command exits with a non-zero code or lists refs that cannot be read.</exception>
    /// <exception cref="ProcessStartException">git cannot be started or the clone folder does not exist.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<RefSnapshot> GetLocalRefsAsync(GitRepositoryRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Asks the server which branch its <c>HEAD</c> points at.
    /// </summary>
    /// <param name="request">The git executable, the existing clone folder and the token; the branch is ignored.</param>
    /// <param name="cancellationToken">Cancels the command and kills the running git process.</param>
    /// <returns>
    /// The default branch name, or <see langword="null"/> when the server's <c>HEAD</c> does not resolve to an
    /// existing branch, as in an empty repository, or names a branch that is empty, starts with <c>-</c> or contains
    /// a control character; a server-supplied name is never returned in that case.
    /// </returns>
    /// <exception cref="ArgumentException">The clone path is not fully qualified.</exception>
    /// <exception cref="GitCommandException">The git command exits with a non-zero code.</exception>
    /// <exception cref="ProcessStartException">git cannot be started or the clone folder does not exist.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<string?> GetDefaultBranchAsync(GitRepositoryRequest request, CancellationToken cancellationToken);
}
