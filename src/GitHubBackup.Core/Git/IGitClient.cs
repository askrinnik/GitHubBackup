using GitHubBackup.Core.Processes;

namespace GitHubBackup.Core.Git;

/// <summary>
/// Creates and updates the local clone of a repository from the server, reads the branches and tags of the server and
/// of the clone, and reads the local state of the clone.
/// </summary>
/// <remarks>
/// Every git command logs its output and fails by its exit code only. The token reaches git only through the
/// environment of each process that talks to the server, never through arguments, the remote URL or files inside the
/// clone, and no hook of the repository or the user runs.
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
    /// <exception cref="GitRepositoryNotFoundException">The server has no repository at the remote URL.</exception>
    /// <exception cref="GitCommandException">A git command exits with a non-zero code or lists refs that cannot be read.</exception>
    /// <exception cref="ProcessStartException">git cannot be started.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<GitSyncResult> CloneAsync(GitRepositoryRequest request, string remoteUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches every branch and tag from <c>origin</c>, pruning what the server deleted, and resets the clone to the
    /// branch; local changes and untracked files are discarded, and so are ignored files unless
    /// <see cref="GitRepositoryRequest.KeepIgnoredFiles"/> is set.
    /// </summary>
    /// <remarks>The caller checks the clone first, because the update overwrites whatever work it holds.</remarks>
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
    /// <exception cref="GitRepositoryNotFoundException">The server has no repository at the remote URL.</exception>
    /// <exception cref="GitCommandException">A git command exits with a non-zero code or lists refs that cannot be read.</exception>
    /// <exception cref="ProcessStartException">git cannot be started or the clone folder does not exist.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<GitSyncResult> UpdateAsync(GitRepositoryRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches every branch and tag from <c>origin</c>, pruning what the server deleted, without changing the working
    /// tree or the local branches: the first step of <see cref="UpdateAsync"/>.
    /// </summary>
    /// <param name="request">The git executable, the existing clone folder, the branch and the token.</param>
    /// <param name="cancellationToken">Cancels the fetch and kills the running git process.</param>
    /// <returns>A task that completes when the remote-tracking branches and the tags match the server.</returns>
    /// <exception cref="ArgumentException">The clone path is not fully qualified or the branch is not valid.</exception>
    /// <exception cref="GitRepositoryNotFoundException">The server has no repository at the remote URL.</exception>
    /// <exception cref="GitCommandException">The git command exits with a non-zero code.</exception>
    /// <exception cref="ProcessStartException">git cannot be started or the clone folder does not exist.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task FetchAsync(GitRepositoryRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Resets the clone to the branch as the last fetch left <c>origin</c>: the steps of <see cref="UpdateAsync"/>
    /// after the fetch.
    /// </summary>
    /// <param name="request">The git executable, the existing clone folder, the branch and the token.</param>
    /// <param name="requiredBranch">
    /// The branch to check out, chosen before the fetch, or <see langword="null"/> to choose the requested or the
    /// default branch as <see cref="UpdateAsync"/> does.
    /// </param>
    /// <param name="cancellationToken">Cancels the steps and kills the running git process.</param>
    /// <returns>The branch checked out and the branches and tags of the clone.</returns>
    /// <exception cref="ArgumentException">
    /// The clone path is not fully qualified, or a branch is empty or starts with <c>-</c>.
    /// </exception>
    /// <exception cref="GitBranchNotFoundException">
    /// The fetched refs do not hold <paramref name="requiredBranch"/>, or, without it, the server has branches but
    /// neither the requested branch nor the default branch is among them.
    /// </exception>
    /// <exception cref="GitCommandException">A git command exits with a non-zero code or lists refs that cannot be read.</exception>
    /// <exception cref="ProcessStartException">git cannot be started or the clone folder does not exist.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<GitSyncResult> SyncAsync(GitRepositoryRequest request, string? requiredBranch, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the branches and tags of <c>origin</c> from the server without changing the clone.
    /// </summary>
    /// <param name="request">The git executable, the existing clone folder and the token; the branch is ignored.</param>
    /// <param name="cancellationToken">Cancels the command and kills the running git process.</param>
    /// <returns>The server's branches and tags; other server refs, such as pull requests, are left out.</returns>
    /// <exception cref="ArgumentException">The clone path is not fully qualified.</exception>
    /// <exception cref="GitRepositoryNotFoundException">The server has no repository at the remote URL.</exception>
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
    /// <exception cref="GitRepositoryNotFoundException">The server has no repository at the remote URL.</exception>
    /// <exception cref="GitCommandException">The git command exits with a non-zero code.</exception>
    /// <exception cref="ProcessStartException">git cannot be started or the clone folder does not exist.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<string?> GetDefaultBranchAsync(GitRepositoryRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Points <c>origin</c> of an existing clone at <paramref name="remoteUrl"/>, without the network.
    /// </summary>
    /// <param name="request">The git executable and the existing clone folder; the branch and the token are ignored.</param>
    /// <param name="remoteUrl">The new URL of the repository on the server, without credentials.</param>
    /// <param name="cancellationToken">Cancels the command and kills the running git process.</param>
    /// <returns>A task that completes when the clone's configuration is changed.</returns>
    /// <exception cref="ArgumentException">
    /// The clone path is not fully qualified, or <paramref name="remoteUrl"/> is empty or starts with <c>-</c>.
    /// </exception>
    /// <exception cref="GitCommandException">The git command exits with a non-zero code.</exception>
    /// <exception cref="ProcessStartException">git cannot be started or the clone folder does not exist.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task SetRemoteUrlAsync(GitRepositoryRequest request, string remoteUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the URL of <c>origin</c> from the configuration of an existing clone, without the network.
    /// </summary>
    /// <param name="request">The git executable and the existing clone folder; the branch and the token are ignored.</param>
    /// <param name="cancellationToken">Cancels the command and kills the running git process.</param>
    /// <returns>The URL, or <see langword="null"/> when the clone has no remote named <c>origin</c>.</returns>
    /// <exception cref="ArgumentException">The clone path is not fully qualified.</exception>
    /// <exception cref="GitCommandException">The git command fails for another reason.</exception>
    /// <exception cref="ProcessStartException">git cannot be started or the clone folder does not exist.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<string?> GetOriginUrlAsync(GitRepositoryRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the branch, the uncommitted changes, the untracked files and the unpushed commits of an existing clone,
    /// without the network and without changing the index or any ref.
    /// </summary>
    /// <param name="request">The git executable and the existing clone folder; the branch and the token are ignored.</param>
    /// <param name="knownRemoteRefs">
    /// The remote-tracking branches as <see cref="GetLocalRefsAsync"/> read them before a fetch, to count unpushed
    /// commits against them; <see langword="null"/> counts against the current ones.
    /// </param>
    /// <param name="cancellationToken">Cancels the commands and kills the running git process.</param>
    /// <returns>The local state of the clone.</returns>
    /// <exception cref="ArgumentException">The clone path is not fully qualified.</exception>
    /// <exception cref="GitCommandException">A git command exits with a non-zero code or writes output that cannot be read.</exception>
    /// <exception cref="ProcessStartException">git cannot be started or the clone folder does not exist.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<WorkingCopyStatus> GetWorkingCopyStatusAsync(
        GitRepositoryRequest request, RefSnapshot? knownRemoteRefs, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the files and folders of an existing clone and of its checked-out submodules that the ignore rules match.
    /// </summary>
    /// <param name="request">The git executable and the existing clone folder; the branch and the token are ignored.</param>
    /// <param name="cancellationToken">Cancels the commands and kills the running git process.</param>
    /// <returns>
    /// The paths relative to the clone folder, with <c>\</c> as the separator; a folder whose whole content is ignored
    /// appears once, without its content. A path that would lead outside the clone is never returned.
    /// </returns>
    /// <exception cref="ArgumentException">The clone path is not fully qualified.</exception>
    /// <exception cref="GitCommandException">A git command exits with a non-zero code.</exception>
    /// <exception cref="ProcessStartException">git cannot be started or the clone folder does not exist.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<IReadOnlyList<string>> GetIgnoredPathsAsync(GitRepositoryRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Counts the commits of the local branch <paramref name="branch"/> that the clone has not seen on <c>origin</c>,
    /// without the network.
    /// </summary>
    /// <param name="request">The git executable and the existing clone folder; the branch and the token are ignored.</param>
    /// <param name="branch">The local branch; it must not be empty or start with <c>-</c>.</param>
    /// <param name="knownRemoteRefs">
    /// The remote-tracking branches as they were before a fetch, or <see langword="null"/> for the current ones.
    /// </param>
    /// <param name="cancellationToken">Cancels the commands and kills the running git process.</param>
    /// <returns>
    /// The commits not in <c>origin/&lt;branch&gt;</c>, or in no remote-tracking branch of <c>origin</c> when that one
    /// is missing; <c>0</c> when the clone has no such local branch.
    /// </returns>
    /// <exception cref="ArgumentException">The clone path is not fully qualified or the branch is not valid.</exception>
    /// <exception cref="GitCommandException">A git command fails or writes output that cannot be read.</exception>
    /// <exception cref="ProcessStartException">git cannot be started or the clone folder does not exist.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<int> GetUnpushedBranchCommitsAsync(
        GitRepositoryRequest request, string branch, RefSnapshot? knownRemoteRefs, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the entries of the index of an existing clone and of its checked-out submodules that are symbolic links.
    /// </summary>
    /// <param name="request">The git executable and the existing clone folder; the branch and the token are ignored.</param>
    /// <param name="cancellationToken">Cancels the commands and kills the running git process.</param>
    /// <returns>
    /// The paths relative to the clone folder, with <c>\</c> as the separator; a path that would lead outside the clone
    /// is never returned.
    /// </returns>
    /// <exception cref="ArgumentException">The clone path is not fully qualified.</exception>
    /// <exception cref="GitCommandException">A git command exits with a non-zero code.</exception>
    /// <exception cref="ProcessStartException">git cannot be started or the clone folder does not exist.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<IReadOnlyList<string>> GetSymbolicLinkPathsAsync(GitRepositoryRequest request, CancellationToken cancellationToken);
}
