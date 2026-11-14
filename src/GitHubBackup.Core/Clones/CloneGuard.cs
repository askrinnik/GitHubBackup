using GitHubBackup.Core.Git;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Clones;

/// <inheritdoc />
/// <param name="gitClient">Reads the local state of the clone and the branches of the server.</param>
/// <param name="logger">The logger.</param>
public sealed class CloneGuard(IGitClient gitClient, ILogger<CloneGuard> logger) : ICloneGuard
{
    /// <inheritdoc />
    public async Task<CloneCheckResult> CheckAsync(
        GitRepositoryRequest git, RefSnapshot? remoteRefs, string? acceptedBranch, CancellationToken cancellationToken)
    {
        var status = await gitClient.GetWorkingCopyStatusAsync(git, knownRemoteRefs: null, cancellationToken);

        // A clone without a commit has no branch work to lose, and an update has to leave its unborn branch.
        var expected = status.HasCommits ? await GetExpectedBranchAsync(git, remoteRefs, cancellationToken) : null;
        return await EvaluateAsync(git, status, expected, acceptedBranch, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CloneCheckResult> CheckLocalAsync(
        GitRepositoryRequest git,
        string? expectedBranch,
        string? acceptedBranch,
        RefSnapshot? knownRemoteRefs,
        CancellationToken cancellationToken)
    {
        var status = await gitClient.GetWorkingCopyStatusAsync(git, knownRemoteRefs, cancellationToken);
        return await EvaluateAsync(
            git, status, status.HasCommits ? expectedBranch : null, acceptedBranch, knownRemoteRefs, cancellationToken);
    }

    /// <summary>
    /// Turns the local state of the clone into the list of problems and logs the outcome.
    /// </summary>
    /// <param name="git">The clone.</param>
    /// <param name="status">The local state of the clone.</param>
    /// <param name="expected">The branch an update would check out, or <see langword="null"/> to skip the branch.</param>
    /// <param name="acceptedBranch">The branch of the last archive, accepted besides the expected one, or <see langword="null"/>.</param>
    /// <param name="knownRemoteRefs">
    /// The remote-tracking branches before a fetch, or <see langword="null"/> for the current ones.
    /// </param>
    /// <param name="cancellationToken">Cancels the git commands.</param>
    /// <returns>The result.</returns>
    private async Task<CloneCheckResult> EvaluateAsync(
        GitRepositoryRequest git,
        WorkingCopyStatus status,
        string? expected,
        string? acceptedBranch,
        RefSnapshot? knownRemoteRefs,
        CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        if (status.HasUncommittedChanges)
        {
            problems.Add(CloneCheckResult.UncommittedChanges);
        }

        if (status.HasUntrackedFiles)
        {
            problems.Add(CloneCheckResult.UntrackedFiles);
        }

        var expectedBranchCommits = 0;
        if (expected is not null)
        {
            if (status.Branch is not { } branch)
            {
                problems.Add(CloneCheckResult.DetachedHead(expected));
            }
            else if (!string.Equals(branch, expected, StringComparison.Ordinal))
            {
                if (!string.Equals(branch, acceptedBranch, StringComparison.Ordinal))
                {
                    problems.Add(CloneCheckResult.OtherBranch(branch, expected));
                }
                else
                {
                    // The update recreates the expected branch from origin, so commits that only a local branch of
                    // that name holds would be lost even though the checked-out branch is the accepted one.
                    expectedBranchCommits = await gitClient.GetUnpushedBranchCommitsAsync(
                        git, expected, knownRemoteRefs, cancellationToken);
                }
            }
        }

        if (status.UnpushedCommits > 0)
        {
            problems.Add(CloneCheckResult.UnpushedCommits(status.UnpushedCommits));
        }

        if (expectedBranchCommits > 0)
        {
            problems.Add(CloneCheckResult.UnpushedBranchCommits(expectedBranchCommits, expected!));
        }

        var result = new CloneCheckResult(git.ClonePath, problems) { ExpectedBranch = expected };
        if (result.Passed)
        {
            CloneGuardLog.Passed(logger, git.ClonePath);
        }
        else
        {
            CloneGuardLog.LocalChangesFound(logger, git.ClonePath, string.Join(", ", problems));
        }

        return result;
    }

    /// <summary>
    /// Returns the branch an update would check out: the configured branch when the server has it, otherwise the
    /// server's default branch.
    /// </summary>
    /// <param name="git">The clone and the configured branch.</param>
    /// <param name="remoteRefs">The branches and tags of the server, or <see langword="null"/> to read them.</param>
    /// <param name="cancellationToken">Cancels the git commands.</param>
    /// <returns>
    /// The branch, or <see langword="null"/> when the server has no branch or names no existing default branch; the
    /// update fails on its own in that case and changes nothing.
    /// </returns>
    private async Task<string?> GetExpectedBranchAsync(
        GitRepositoryRequest git, RefSnapshot? remoteRefs, CancellationToken cancellationToken)
    {
        var remote = remoteRefs ?? await gitClient.GetRemoteRefsAsync(git, cancellationToken);
        if (!remote.HasBranches)
        {
            return null;
        }

        if (git.Branch is not null && remote.ContainsBranch(git.Branch))
        {
            return git.Branch;
        }

        var defaultBranch = await gitClient.GetDefaultBranchAsync(git, cancellationToken);
        return defaultBranch is not null && remote.ContainsBranch(defaultBranch) ? defaultBranch : null;
    }
}
