using System.IO.Abstractions;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.Processes;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Discovery;

/// <inheritdoc />
/// <remarks>
/// Both folders lie under the same clone root, so the move is a rename on one volume and costs no copy. A deleted
/// clone is not cloned again here: the backup step that follows finds no <c>.git</c> under the new name and clones it.
/// </remarks>
/// <param name="fileSystem">Moves and deletes the clone folders.</param>
/// <param name="gitClient">Points the moved clone at the new URL.</param>
/// <param name="logger">The logger.</param>
public sealed class CloneRelocator(IFileSystem fileSystem, IGitClient gitClient, ILogger<CloneRelocator> logger)
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
    public async Task<CloneRelocationResult> RelocateAsync(
        string gitPath, string oldFolder, string newFolder, string remoteUrl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!fileSystem.Directory.Exists(oldFolder))
        {
            return CloneRelocationResult.NotPresent;
        }

        if (fileSystem.DirectoryInfo.New(oldFolder).Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            CloneRelocatorLog.CloneIsLink(logger, oldFolder);
            return CloneRelocationResult.DeleteFailed;
        }

        // Windows compares paths without regard to case, so a rename that only changes the case keeps the folder
        // usable as it is.
        if (!string.Equals(oldFolder, newFolder, StringComparison.OrdinalIgnoreCase))
        {
            // Whatever occupies the target may belong to another repository, which a later backup would reuse, so
            // both folders stay as they are until the user clears the target.
            if (fileSystem.Directory.Exists(newFolder) || fileSystem.File.Exists(newFolder))
            {
                CloneRelocatorLog.TargetExists(logger, oldFolder, newFolder);
                return CloneRelocationResult.TargetOccupied;
            }

            if (!TryMove(oldFolder, newFolder))
            {
                return Delete(oldFolder);
            }
        }

        try
        {
            await gitClient.SetRemoteUrlAsync(
                new GitRepositoryRequest { GitPath = gitPath, ClonePath = newFolder }, remoteUrl, cancellationToken);
        }
        catch (Exception exception) when (exception is GitCommandException or ProcessStartException)
        {
            CloneRelocatorLog.RemoteUrlNotSet(logger, newFolder, exception);
            return Delete(newFolder);
        }

        CloneRelocatorLog.Moved(logger, oldFolder, newFolder);
        return CloneRelocationResult.Moved;
    }

    /// <summary>
    /// Moves a clone folder to a folder that the caller has checked does not exist, creating its parent.
    /// </summary>
    /// <param name="oldFolder">The clone folder.</param>
    /// <param name="newFolder">The target folder.</param>
    /// <returns><see langword="true"/> when the folder was moved.</returns>
    private bool TryMove(string oldFolder, string newFolder)
    {
        try
        {
            var parent = fileSystem.Path.GetDirectoryName(newFolder);
            if (parent is not null)
            {
                fileSystem.Directory.CreateDirectory(parent);
            }

            fileSystem.Directory.Move(oldFolder, newFolder);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            CloneRelocatorLog.MoveFailed(logger, oldFolder, newFolder, exception);
            return false;
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
    private CloneRelocationResult Delete(string folder)
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
            return CloneRelocationResult.DeleteFailed;
        }

        CloneRelocatorLog.DeletedForReclone(logger, folder);
        return CloneRelocationResult.DeletedForReclone;
    }
}
