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
        if (!fileSystem.Path.IsPathFullyQualified(request.ArchiveFolder))
        {
            throw new ArgumentException("The archive folder must be a fully qualified path.", nameof(request));
        }

        var last = request.LastArchive;
        var archiveMissing = last is null || !fileSystem.File.Exists(fileSystem.Path.Combine(request.ArchiveFolder, last.File));
        var cloneExists = fileSystem.Directory.Exists(fileSystem.Path.Combine(request.Git.ClonePath, ".git"));

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
