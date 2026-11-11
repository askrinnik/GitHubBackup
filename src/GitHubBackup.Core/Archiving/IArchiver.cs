using GitHubBackup.Core.Processes;

namespace GitHubBackup.Core.Archiving;

/// <summary>
/// Packs a clone into a ZIP archive, tests the archive and places it in the archive folder.
/// </summary>
/// <remarks>
/// The archive is built under a temporary name in <see cref="ArchiveRequest.TemporaryFolder"/> and reaches the
/// archive folder only when it is complete and, when <c>Backup:VerifyArchive</c> is on, tested. When a step fails or
/// is cancelled, no file remains in either folder. An existing file of the same name is never overwritten.
/// </remarks>
public interface IArchiver
{
    /// <summary>
    /// Creates, verifies and moves the archive.
    /// </summary>
    /// <param name="request">The tool, the folders and the file name.</param>
    /// <param name="cancellationToken">Cancels the work; the temporary files are removed.</param>
    /// <returns>The path and the size of the finished archive.</returns>
    /// <exception cref="ArgumentException">
    /// A path is not fully qualified, the file name contains a folder, or the source folder does not exist.
    /// </exception>
    /// <exception cref="ArchiveCommandException">7-Zip exits with a non-zero code.</exception>
    /// <exception cref="IOException">The archive cannot be moved, or a file of the same name exists.</exception>
    /// <exception cref="ProcessStartException">7-Zip cannot be started.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<ArchiveResult> CreateAsync(ArchiveRequest request, CancellationToken cancellationToken);
}
