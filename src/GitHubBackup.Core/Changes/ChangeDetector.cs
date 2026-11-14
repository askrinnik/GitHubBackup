using System.IO.Abstractions;
using GitHubBackup.Core.Clones;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.State;
using GitHubBackup.Core.Telemetry;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Changes;

/// <summary>
/// Implements <see cref="IChangeDetector"/> on top of <see cref="IGitClient"/>.
/// </summary>
/// <param name="gitClient">Clones and updates the clone and reads the refs of the server and the origin of the clone.</param>
/// <param name="cloneGuard">Checks the clone for local work before it is updated.</param>
/// <param name="fileSystem">Checks that the clone and the last archive exist; no file is read.</param>
/// <param name="telemetry">Traces each git operation as a child of the span of the repository.</param>
/// <param name="logger">The logger.</param>
public sealed class ChangeDetector(
    IGitClient gitClient,
    ICloneGuard cloneGuard,
    IFileSystem fileSystem,
    BackupTelemetry telemetry,
    ILogger<ChangeDetector> logger)
    : IChangeDetector
{
    /// <summary>The name of the folder that holds the repository of a git clone.</summary>
    private const string _gitFolderName = ".git";

    /// <inheritdoc />
    public async Task<ChangeDetection> DetectAsync(ChangeDetectionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateArchiveFolder(request);

        var last = request.LastArchive;
        var archiveMissing = IsArchiveMissing(request);
        var cloneExists = await InspectCloneAsync(request, cancellationToken);

        GitSyncResult sync;
        if (!cloneExists)
        {
            request.OperationProgress?.Report(BackupProgressStage.Cloning);
            sync = await telemetry.TraceOperationAsync(
                BackupTelemetry.CloneOperation,
                () => gitClient.CloneAsync(request.Git, request.RemoteUrl, cancellationToken));
        }
        else
        {
            RefSnapshot? remote = null;
            if (last is not null && !archiveMissing)
            {
                (var unchanged, remote) = await TryConfirmUnchangedAsync(request, last, cancellationToken);
                if (unchanged is not null)
                {
                    return Log(request, unchanged);
                }
            }

            var check = await EnsureNoLocalChangesAsync(request, remote, cancellationToken);
            request.OperationProgress?.Report(BackupProgressStage.Fetching);
            sync = await telemetry.TraceOperationAsync(
                BackupTelemetry.FetchOperation, () => UpdateAsync(request, check, cancellationToken));
        }

        return Log(request, Decide(sync, last, archiveMissing));
    }

    /// <inheritdoc />
    public async Task<ChangeDetection> PreviewAsync(ChangeDetectionRequest request, CancellationToken cancellationToken)
    {
        ValidateArchiveFolder(request);

        if (!await InspectCloneAsync(request, cancellationToken))
        {
            return Log(
                request,
                new(ChangeDecision.CloneNeeded, ChangeReason.NoClone, null, false, RefSnapshot.Empty));
        }

        request.OperationProgress?.Report(BackupProgressStage.CheckingRemote);
        var git = request.Git;
        var remote = await GetRemoteRefsAsync(git, cancellationToken);
        var sync = remote.HasBranches
            ? await ChooseBranchAsync(git, remote, cancellationToken)
            : new GitSyncResult(null, false, remote);
        var detection = Decide(sync, request.LastArchive, IsArchiveMissing(request));

        // Only an unchanged repository skips the update, so every other decision meets the check a backup run makes.
        if (detection.Decision != ChangeDecision.UpToDate)
        {
            await EnsureNoLocalChangesAsync(request, remote, cancellationToken);
        }

        return Log(request, detection);
    }

    /// <summary>
    /// Decides whether the clone folder holds a clone to use, and rejects a folder that must not be touched.
    /// </summary>
    /// <param name="request">The request with the clone folder and the URL of the repository.</param>
    /// <param name="cancellationToken">Cancels reading the origin of the clone.</param>
    /// <returns>
    /// <see langword="true"/> for a clone whose <c>origin</c> points at the repository; <see langword="false"/> when
    /// the folder is missing or empty, so it can be cloned into.
    /// </returns>
    /// <exception cref="CloneFolderException">
    /// The folder holds files but no <c>.git</c> folder, or the clone's <c>origin</c> points elsewhere.
    /// </exception>
    private async Task<bool> InspectCloneAsync(ChangeDetectionRequest request, CancellationToken cancellationToken)
    {
        var folder = request.Git.ClonePath;
        if (!fileSystem.Directory.Exists(folder))
        {
            return false;
        }

        if (!fileSystem.Directory.Exists(fileSystem.Path.Combine(folder, _gitFolderName)))
        {
            if (!fileSystem.Directory.EnumerateFileSystemEntries(folder).Any())
            {
                return false;
            }

            // A linked worktree has a .git file, and anything else may be the user's files; cloning into the folder
            // would fail or mix the clone with them, so the folder is reported and left alone.
            throw new CloneFolderException(
                $"The folder '{folder}' is not empty and holds no git clone, so it is not used. Empty it, move it away or change clonePath, then run the backup again.",
                folder);
        }

        var origin = await gitClient.GetOriginUrlAsync(request.Git, cancellationToken);
        if (origin is null || !OriginUrl.Matches(origin, request.RemoteUrl))
        {
            // The origin is not quoted: a URL the user set may carry credentials.
            throw new CloneFolderException(
                $"The clone '{folder}' has an origin that does not point at '{request.RemoteUrl}', so it is not used. Point origin at that URL or change clonePath, then run the backup again.",
                folder);
        }

        return true;
    }

    /// <summary>
    /// Checks the clone for local work an update would overwrite.
    /// </summary>
    /// <param name="request">The request with the clone, the configured branch and the last archive.</param>
    /// <param name="remote">The refs of the server when they were just read, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the git commands.</param>
    /// <returns>The passed check, whose expected branch the repeated check reuses.</returns>
    /// <exception cref="LocalChangesException">The clone holds local work.</exception>
    private async Task<CloneCheckResult> EnsureNoLocalChangesAsync(
        ChangeDetectionRequest request, RefSnapshot? remote, CancellationToken cancellationToken)
    {
        var check = await cloneGuard.CheckAsync(request.Git, remote, request.LastArchive?.Branch, cancellationToken);
        return check.Passed ? check : throw new LocalChangesException(check);
    }

    /// <summary>
    /// Fetches, checks the clone again without the network and only then resets it to the server.
    /// </summary>
    /// <remarks>
    /// A fetch can take minutes, and the user may save or commit in the working copy meanwhile; the second check
    /// catches that work right before checkout, reset and clean would overwrite it. It counts unpushed commits against
    /// the remote-tracking branches as they were before the fetch, so commits the server dropped by a force-push are
    /// not mistaken for the user's work, and the update checks out exactly the branch both checks expected.
    /// </remarks>
    /// <param name="request">The request with the clone, the configured branch and the last archive.</param>
    /// <param name="check">The check that passed before the fetch.</param>
    /// <param name="cancellationToken">Cancels the git commands.</param>
    /// <returns>The state of the clone after the update.</returns>
    /// <exception cref="LocalChangesException">The clone holds local work after the fetch.</exception>
    private async Task<GitSyncResult> UpdateAsync(
        ChangeDetectionRequest request, CloneCheckResult check, CancellationToken cancellationToken)
    {
        var beforeFetch = await gitClient.GetLocalRefsAsync(request.Git, cancellationToken);
        await gitClient.FetchAsync(request.Git, cancellationToken);
        var recheck = await cloneGuard.CheckLocalAsync(
            request.Git, check.ExpectedBranch, request.LastArchive?.Branch, beforeFetch, cancellationToken);
        if (!recheck.Passed)
        {
            throw new LocalChangesException(recheck);
        }

        return await gitClient.SyncAsync(request.Git, check.ExpectedBranch, cancellationToken);
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

        var defaultBranch = await GetDefaultBranchAsync(git, cancellationToken);
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
    /// Reads the refs of the server inside a <see cref="BackupTelemetry.LsRemoteOperation"/> span.
    /// </summary>
    /// <param name="git">The clone whose server is asked.</param>
    /// <param name="cancellationToken">Cancels the git command.</param>
    /// <returns>The branches and tags of the server.</returns>
    private Task<RefSnapshot> GetRemoteRefsAsync(GitRepositoryRequest git, CancellationToken cancellationToken) =>
        telemetry.TraceOperationAsync(
            BackupTelemetry.LsRemoteOperation, () => gitClient.GetRemoteRefsAsync(git, cancellationToken));

    /// <summary>
    /// Asks the server for its default branch inside a <see cref="BackupTelemetry.LsRemoteOperation"/> span.
    /// </summary>
    /// <param name="git">The clone whose server is asked.</param>
    /// <param name="cancellationToken">Cancels the git command.</param>
    /// <returns>The default branch, or <see langword="null"/> when the server's <c>HEAD</c> names no branch.</returns>
    private Task<string?> GetDefaultBranchAsync(GitRepositoryRequest git, CancellationToken cancellationToken) =>
        telemetry.TraceOperationAsync(
            BackupTelemetry.LsRemoteOperation, () => gitClient.GetDefaultBranchAsync(git, cancellationToken));

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
    /// Compares the refs of the server and the branch to check out with the last archive, without fetching.
    /// </summary>
    /// <param name="request">The request, with the clone, the requested branch and the receiver of progress.</param>
    /// <param name="last">The last archive, whose file exists.</param>
    /// <param name="cancellationToken">Cancels the git commands.</param>
    /// <returns>
    /// The <see cref="ChangeDecision.UpToDate"/> result, or <see langword="null"/> when the clone must be updated to
    /// decide, and the refs of the server.
    /// </returns>
    private async Task<(ChangeDetection? Unchanged, RefSnapshot Remote)> TryConfirmUnchangedAsync(
        ChangeDetectionRequest request, ArchiveRecord last, CancellationToken cancellationToken)
    {
        request.OperationProgress?.Report(BackupProgressStage.CheckingRemote);
        var git = request.Git;
        var remote = await GetRemoteRefsAsync(git, cancellationToken);
        if (!remote.Equals(last.Refs))
        {
            return (null, remote);
        }

        string? branch;
        var fallback = false;
        if (git.Branch is not null && remote.ContainsBranch(git.Branch))
        {
            branch = git.Branch;
        }
        else
        {
            branch = await GetDefaultBranchAsync(git, cancellationToken);
            if (branch is null || !remote.ContainsBranch(branch))
            {
                return (null, remote);
            }

            fallback = git.Branch is not null;
        }

        return string.Equals(branch, last.Branch, StringComparison.Ordinal)
            ? (new ChangeDetection(ChangeDecision.UpToDate, ChangeReason.Unchanged, branch, fallback, remote), remote)
            : (null, remote);
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
