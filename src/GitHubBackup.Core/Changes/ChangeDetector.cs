using System.IO.Abstractions;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.State;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Changes;

/// <summary>
/// Implements <see cref="IChangeDetector"/> on top of <see cref="IGitClient"/>.
/// </summary>
/// <param name="gitClient">Clones and updates the clone and reads the refs of the server.</param>
/// <param name="fileSystem">Checks that the clone and the last archive exist; no file is read.</param>
/// <param name="logger">The logger.</param>
public sealed class ChangeDetector(IGitClient gitClient, IFileSystem fileSystem, ILogger<ChangeDetector> logger)
    : IChangeDetector
{
    /// <inheritdoc />
    public async Task<ChangeDetection> DetectAsync(ChangeDetectionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateArchiveFolder(request);

        var last = request.LastArchive;
        var archiveMissing = IsArchiveMissing(request);
        var cloneExists = CloneExists(request);

        GitSyncResult sync;
        if (!cloneExists)
        {
            sync = await gitClient.CloneAsync(request.Git, request.RemoteUrl, cancellationToken);
        }
        else
        {
            if (last is not null && !archiveMissing)
            {
                var unchanged = await TryConfirmUnchangedAsync(request.Git, last, cancellationToken);
                if (unchanged is not null)
                {
                    return Log(request, unchanged);
                }
            }

            sync = await gitClient.UpdateAsync(request.Git, cancellationToken);
        }

        return Log(request, Decide(sync, last, archiveMissing));
    }

    /// <inheritdoc />
    public async Task<ChangeDetection> PreviewAsync(ChangeDetectionRequest request, CancellationToken cancellationToken)
    {
        ValidateArchiveFolder(request);

        if (!CloneExists(request))
        {
            return Log(
                request,
                new(ChangeDecision.CloneNeeded, ChangeReason.NoClone, null, false, RefSnapshot.Empty));
        }

        var git = request.Git;
        var remote = await gitClient.GetRemoteRefsAsync(git, cancellationToken);
        var sync = remote.HasBranches
            ? await ChooseBranchAsync(git, remote, cancellationToken)
            : new GitSyncResult(null, false, remote);
        return Log(request, Decide(sync, request.LastArchive, IsArchiveMissing(request)));
    }

    /// <summary>
    /// Chooses the branch an update would check out: the requested branch when the server has it, otherwise the
    /// server's default branch.
    /// </summary>
    /// <param name="git">The clone and the requested branch.</param>
    /// <param name="remote">The refs of the server, with at least one branch.</param>
    /// <param name="cancellationToken">Cancels the git command.</param>
    /// <returns>The state an update would leave the clone in.</returns>
    /// <exception cref="ArgumentException">The requested or the default branch is not a valid branch name.</exception>
    /// <exception cref="GitBranchNotFoundException">Neither the requested branch nor the default branch exists.</exception>
    private async Task<GitSyncResult> ChooseBranchAsync(
        GitRepositoryRequest git, RefSnapshot remote, CancellationToken cancellationToken)
    {
        if (git.Branch is not null)
        {
            ValidateBranch(git.Branch, git);
        }

        if (git.Branch is not null && remote.ContainsBranch(git.Branch))
        {
            return new(git.Branch, false, remote);
        }

        var defaultBranch = await gitClient.GetDefaultBranchAsync(git, cancellationToken);
        if (defaultBranch is null || !remote.ContainsBranch(defaultBranch))
        {
            var requested = git.Branch is null ? "No branch was requested" : $"Branch '{git.Branch}' does not exist";
            var serverDefault = defaultBranch is null
                ? "the server's HEAD names no existing branch"
                : $"the server's default branch '{defaultBranch}' does not exist";
            throw new GitBranchNotFoundException(
                $"{requested} on the server of '{git.ClonePath}', and {serverDefault}.", git.Branch, defaultBranch);
        }

        ValidateBranch(defaultBranch, git);
        return new(defaultBranch, git.Branch is not null, remote);
    }

    /// <summary>
    /// Rejects a branch name that an update would refuse, so a preview reports the same failure as a backup run.
    /// </summary>
    /// <param name="branch">The branch name to check.</param>
    /// <param name="git">The clone the branch belongs to.</param>
    /// <exception cref="ArgumentException">The branch is empty or starts with <c>-</c>.</exception>
    private static void ValidateBranch(string branch, GitRepositoryRequest git)
    {
        if (string.IsNullOrWhiteSpace(branch))
        {
            throw new ArgumentException($"The branch for '{git.ClonePath}' must not be empty.", nameof(git));
        }

        if (branch.StartsWith('-'))
        {
            throw new ArgumentException(
                $"The branch '{branch}' for '{git.ClonePath}' must not start with '-'.", nameof(git));
        }
    }

    /// <summary>
    /// Rejects a request whose archive folder is not fully qualified.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <exception cref="ArgumentException">The archive folder is not fully qualified.</exception>
    private void ValidateArchiveFolder(ChangeDetectionRequest request)
    {
        if (!fileSystem.Path.IsPathFullyQualified(request.ArchiveFolder))
        {
            throw new ArgumentException("The archive folder must be a fully qualified path.", nameof(request));
        }
    }

    /// <summary>
    /// Returns whether there is no last archive or its file is missing from the archive folder.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns><see langword="true"/> when no usable last archive exists.</returns>
    private bool IsArchiveMissing(ChangeDetectionRequest request) =>
        request.LastArchive is not { } last
        || !fileSystem.File.Exists(fileSystem.Path.Combine(request.ArchiveFolder, last.File));

    /// <summary>
    /// Returns whether the clone folder holds a git repository.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns><see langword="true"/> when the clone has a <c>.git</c> folder.</returns>
    private bool CloneExists(ChangeDetectionRequest request) =>
        fileSystem.Directory.Exists(fileSystem.Path.Combine(request.Git.ClonePath, ".git"));

    /// <summary>
    /// Compares the refs of the server and the branch to check out with the last archive, without fetching.
    /// </summary>
    /// <param name="git">The clone and the requested branch.</param>
    /// <param name="last">The last archive, whose file exists.</param>
    /// <param name="cancellationToken">Cancels the git commands.</param>
    /// <returns>
    /// The <see cref="ChangeDecision.UpToDate"/> result, or <see langword="null"/> when the clone must be updated to
    /// decide.
    /// </returns>
    private async Task<ChangeDetection?> TryConfirmUnchangedAsync(
        GitRepositoryRequest git, ArchiveRecord last, CancellationToken cancellationToken)
    {
        var remote = await gitClient.GetRemoteRefsAsync(git, cancellationToken);
        if (!remote.Equals(last.Refs))
        {
            return null;
        }

        string? branch;
        var fallback = false;
        if (git.Branch is not null && remote.ContainsBranch(git.Branch))
        {
            branch = git.Branch;
        }
        else
        {
            branch = await gitClient.GetDefaultBranchAsync(git, cancellationToken);
            if (branch is null || !remote.ContainsBranch(branch))
            {
                return null;
            }

            fallback = git.Branch is not null;
        }

        return string.Equals(branch, last.Branch, StringComparison.Ordinal)
            ? new ChangeDetection(ChangeDecision.UpToDate, ChangeReason.Unchanged, branch, fallback, remote)
            : null;
    }

    /// <summary>
    /// Decides from the synchronized clone and the last archive.
    /// </summary>
    /// <param name="sync">The state of the clone after cloning or updating.</param>
    /// <param name="last">The last archive, or <see langword="null"/> when there is none.</param>
    /// <param name="archiveMissing">Whether the file of <paramref name="last"/> is missing or there is no archive.</param>
    /// <returns>The decision.</returns>
    private static ChangeDetection Decide(GitSyncResult sync, ArchiveRecord? last, bool archiveMissing)
    {
        var (decision, reason) = Classify(sync, last, archiveMissing);
        return new ChangeDetection(decision, reason, sync.Branch, sync.BranchFallback, sync.Refs);
    }

    /// <summary>
    /// Applies the decision rules in order: empty repository, no archive, missing file, changed refs, changed branch.
    /// </summary>
    /// <param name="sync">The state of the clone after cloning or updating.</param>
    /// <param name="last">The last archive, or <see langword="null"/> when there is none.</param>
    /// <param name="archiveMissing">Whether the file of <paramref name="last"/> is missing or there is no archive.</param>
    /// <returns>The decision and its reason.</returns>
    private static (ChangeDecision Decision, ChangeReason Reason) Classify(
        GitSyncResult sync, ArchiveRecord? last, bool archiveMissing)
    {
        if (sync.IsEmpty)
        {
            return (ChangeDecision.NothingToArchive, ChangeReason.EmptyRepository);
        }

        if (last is null)
        {
            return (ChangeDecision.ArchiveNeeded, ChangeReason.NoArchive);
        }

        if (archiveMissing)
        {
            return (ChangeDecision.ArchiveNeeded, ChangeReason.ArchiveFileMissing);
        }

        if (!sync.Refs.Equals(last.Refs))
        {
            return (ChangeDecision.ArchiveNeeded, ChangeReason.RefsChanged);
        }

        return string.Equals(sync.Branch, last.Branch, StringComparison.Ordinal)
            ? (ChangeDecision.UpToDate, ChangeReason.Unchanged)
            : (ChangeDecision.ArchiveNeeded, ChangeReason.BranchChanged);
    }

    /// <summary>
    /// Logs the decision.
    /// </summary>
    /// <param name="request">The request that was checked.</param>
    /// <param name="detection">The result.</param>
    /// <returns><paramref name="detection"/>.</returns>
    private ChangeDetection Log(ChangeDetectionRequest request, ChangeDetection detection)
    {
        ChangeDetectorLog.Decided(logger, request.Git.ClonePath, detection.Decision, detection.Reason, detection.Branch);
        return detection;
    }
}
