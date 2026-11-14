using System.IO.Abstractions;
using GitHubBackup.Core.Clones;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.Processes;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Discovery;

/// <inheritdoc />
/// <remarks>
/// A deleted clone is not cloned again here: the backup step that follows finds no <c>.git</c> in the new folder and
/// clones it.
/// </remarks>
/// <param name="fileSystem">Moves and deletes the clone folders.</param>
/// <param name="gitClient">Points the moved clone at the new URL.</param>
/// <param name="cloneGuard">Checks the clone for local work before it is moved.</param>
/// <param name="logger">The logger.</param>
public sealed class CloneRelocator(
    IFileSystem fileSystem, IGitClient gitClient, ICloneGuard cloneGuard, ILogger<CloneRelocator> logger)
    : ICloneRelocator
{
    /// <summary>
    /// Walks the whole clone, including the hidden <c>.git</c> folder, but does not follow junctions or symbolic
    /// links, so clearing <see cref="FileAttributes.ReadOnly"/> never reaches files outside the clone.
    /// </summary>
    private static readonly EnumerationOptions _clearReadOnlyEnumeration = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <inheritdoc />
    public async Task<CloneRelocation> RelocateAsync(CloneRelocationRequest request, CancellationToken cancellationToken)
    {
        var (stop, move) = await CheckAsync(request, cancellationToken);
        if (stop is not null)
        {
            return stop;
        }

        var (oldFolder, newFolder) = (request.OldFolder, request.NewFolder);
        if (move && TryMove(oldFolder, newFolder) is { } moveFailure)
        {
            return request.DeleteOnFailure
                ? Delete(oldFolder)
                : new(
                    CloneRelocationResult.MoveFailed,
                    $"The clone cannot be moved from '{oldFolder}' to '{newFolder}': {moveFailure.Message} Nothing was deleted; move the folder yourself, then run the backup again.");
        }

        try
        {
            await gitClient.SetRemoteUrlAsync(
                new GitRepositoryRequest { GitPath = request.GitPath, ClonePath = newFolder },
                request.RemoteUrl,
                cancellationToken);
        }
        catch (Exception exception) when (exception is GitCommandException or ProcessStartException)
        {
            CloneRelocatorLog.RemoteUrlNotSet(logger, newFolder, exception);
            return request.DeleteOnFailure
                ? Delete(newFolder)
                : new(
                    CloneRelocationResult.RemoteUrlNotSet,
                    $"The origin of the clone '{newFolder}' cannot be set to '{request.RemoteUrl}': {exception.Message}");
        }

        CloneRelocatorLog.Moved(logger, oldFolder, newFolder);
        return CloneRelocation.Moved;
    }

    /// <inheritdoc />
    public async Task<CloneRelocation> PreviewAsync(CloneRelocationRequest request, CancellationToken cancellationToken)
    {
        var (stop, _) = await CheckAsync(request, cancellationToken);
        return stop ?? CloneRelocation.Moved;
    }

    /// <summary>
    /// Runs every check that comes before the move: the old folder, the target and the local work of the clone.
    /// </summary>
    /// <param name="request">The relocation.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>
    /// The outcome that ends the relocation before anything changes, or <see langword="null"/> to go on; and whether
    /// the folder has to move, which is not the case when both folders are the same.
    /// </returns>
    private async Task<(CloneRelocation? Stop, bool Move)> CheckAsync(
        CloneRelocationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (oldFolder, newFolder) = (request.OldFolder, request.NewFolder);
        if (!fileSystem.Directory.Exists(oldFolder))
        {
            return (CloneRelocation.NotPresent, false);
        }

        if (fileSystem.DirectoryInfo.New(oldFolder).Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            CloneRelocatorLog.CloneIsLink(logger, oldFolder);
            return (new(
                CloneRelocationResult.DeleteFailed,
                $"The clone '{oldFolder}' is a junction or a symbolic link, so it is neither moved nor deleted."), false);
        }

        // A folder without .git is not a clone this program made or adopted, so it is neither moved nor changed.
        if (!fileSystem.Directory.Exists(fileSystem.Path.Combine(oldFolder, ".git")))
        {
            CloneRelocatorLog.NotAClone(logger, oldFolder);
            return (CloneRelocation.NotPresent, false);
        }

        if (await CheckOriginAsync(request, cancellationToken) is { } foreign)
        {
            return (foreign, false);
        }

        // Windows compares paths without regard to case, so folders that differ only in case are one folder, and
        // changing only origin touches no file of the working tree.
        if (string.Equals(oldFolder, newFolder, StringComparison.OrdinalIgnoreCase))
        {
            return (null, false);
        }

        // Whatever occupies the target may belong to another repository or to the user, so both folders stay as they
        // are until the user clears the target.
        if (fileSystem.Directory.Exists(newFolder) || fileSystem.File.Exists(newFolder))
        {
            CloneRelocatorLog.TargetExists(logger, oldFolder, newFolder);
            return (new(
                CloneRelocationResult.TargetOccupied,
                $"The clone cannot be moved from '{oldFolder}' to '{newFolder}': the target folder already exists. Nothing was moved or deleted; remove or rename one of the folders, then run the backup again."), false);
        }

        var check = await CheckLocalWorkAsync(request, cancellationToken);
        return (check, true);
    }

    /// <summary>
    /// Checks that the clone in the old folder is a clone of the repository, under its current or its previous URL.
    /// </summary>
    /// <remarks>
    /// A working copy of another repository in that folder must keep its place and its <c>origin</c>.
    /// </remarks>
    /// <param name="request">The relocation.</param>
    /// <param name="cancellationToken">Cancels the git command.</param>
    /// <returns>The outcome that stops the relocation, or <see langword="null"/> when the origin matches.</returns>
    private async Task<CloneRelocation?> CheckOriginAsync(
        CloneRelocationRequest request, CancellationToken cancellationToken)
    {
        string? origin;
        try
        {
            origin = await gitClient.GetOriginUrlAsync(
                new GitRepositoryRequest { GitPath = request.GitPath, ClonePath = request.OldFolder }, cancellationToken);
        }
        catch (Exception exception) when (exception is GitCommandException or ProcessStartException)
        {
            CloneRelocatorLog.CheckFailed(logger, request.OldFolder, exception);
            return new(
                CloneRelocationResult.CheckFailed,
                $"The origin of the clone '{request.OldFolder}' cannot be read, so it is not moved to '{request.NewFolder}': {exception.Message}");
        }

        if (origin is not null
            && (OriginUrl.Matches(origin, request.RemoteUrl)
                || (request.PreviousRemoteUrl is { } previous && OriginUrl.Matches(origin, previous))))
        {
            return null;
        }

        // The origin is not quoted: a URL the user set may carry credentials.
        CloneRelocatorLog.ForeignOrigin(logger, request.OldFolder);
        return new(
            CloneRelocationResult.ForeignOrigin,
            $"The clone '{request.OldFolder}' has an origin that does not point at '{request.RemoteUrl}', so it is neither moved nor changed. Point origin at that URL or move the folder yourself, then run the backup again.");
    }

    /// <summary>
    /// Checks the clone in the old folder for local work that a move would take away from where the user left it.
    /// </summary>
    /// <param name="request">The relocation.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The outcome that stops the move, or <see langword="null"/> when the clone holds no local work.</returns>
    private async Task<CloneRelocation?> CheckLocalWorkAsync(
        CloneRelocationRequest request, CancellationToken cancellationToken)
    {
        var git = new GitRepositoryRequest
        {
            GitPath = request.GitPath,
            ClonePath = request.OldFolder,
            Branch = request.Branch,
            Token = request.Token,
        };
        CloneCheckResult check;
        try
        {
            check = await cloneGuard.CheckAsync(git, null, null, cancellationToken);
        }
        catch (Exception exception) when (exception is GitCommandException or ProcessStartException)
        {
            CloneRelocatorLog.CheckFailed(logger, request.OldFolder, exception);
            return new(
                CloneRelocationResult.CheckFailed,
                $"The clone '{request.OldFolder}' cannot be checked for local changes, so it is not moved to '{request.NewFolder}': {exception.Message}");
        }

        return check.Passed ? null : new(CloneRelocationResult.LocalChanges, check.Message);
    }

    /// <summary>
    /// Moves a clone folder to a folder that the caller has checked does not exist, creating its parent.
    /// </summary>
    /// <param name="oldFolder">The clone folder.</param>
    /// <param name="newFolder">The target folder.</param>
    /// <returns>The failure, or <see langword="null"/> when the folder was moved.</returns>
    private Exception? TryMove(string oldFolder, string newFolder)
    {
        try
        {
            var parent = fileSystem.Path.GetDirectoryName(newFolder);
            if (parent is not null)
            {
                fileSystem.Directory.CreateDirectory(parent);
            }

            fileSystem.Directory.Move(oldFolder, newFolder);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            CloneRelocatorLog.MoveFailed(logger, oldFolder, newFolder, exception);
            return exception;
        }
    }

    /// <summary>
    /// Deletes a clone folder, including the read-only object files git creates.
    /// </summary>
    /// <param name="folder">The clone folder.</param>
    /// <returns>
    /// <see cref="CloneRelocationResult.DeletedForReclone"/>, or <see cref="CloneRelocationResult.DeleteFailed"/> when
    /// the folder could not be deleted.
    /// </returns>
    private CloneRelocation Delete(string folder)
    {
        try
        {
            foreach (var file in fileSystem.Directory.EnumerateFiles(folder, "*", _clearReadOnlyEnumeration))
            {
                var attributes = fileSystem.File.GetAttributes(file);
                if (attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    fileSystem.File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
            }

            fileSystem.Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            CloneRelocatorLog.DeleteFailed(logger, folder, exception);
            return new(
                CloneRelocationResult.DeleteFailed,
                $"The clone '{folder}' could not be moved and could not be deleted: {exception.Message}");
        }

        CloneRelocatorLog.DeletedForReclone(logger, folder);
        return new(
            CloneRelocationResult.DeletedForReclone,
            $"The clone '{folder}' could not be moved and was deleted; the next backup clones the repository again.");
    }
}
