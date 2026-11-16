namespace GitHubBackup.Core.State;

/// <summary>
/// Reads and writes the state file <see cref="BackupState.FileName"/> in the archive folder of a repository.
/// </summary>
public interface IBackupStateStore
{
    /// <summary>
    /// Reads the state of a repository.
    /// </summary>
    /// <param name="archiveFolder">The archive folder of the repository.</param>
    /// <param name="repositoryId">The GitHub id of the repository, used when there is no state file.</param>
    /// <param name="fullName">The <c>owner/name</c> of the repository, used when there is no state file.</param>
    /// <param name="cancellationToken">Cancels reading the file.</param>
    /// <returns>
    /// The state in the file, or <see cref="BackupState.CreateEmpty"/> when the file or the folder does not exist.
    /// </returns>
    /// <exception cref="BackupStateException">
    /// The file cannot be read, is not valid JSON of the state format, has an unsupported schema version, or holds
    /// the state of a repository with another id than <paramref name="repositoryId"/>.
    /// </exception>
    Task<BackupState> LoadAsync(string archiveFolder, long repositoryId, string fullName, CancellationToken cancellationToken);

    /// <summary>
    /// Records an archive that is already in its final place in the archive folder.
    /// </summary>
    /// <remarks>
    /// The file is replaced atomically and the previous version is kept next to it, so a failure leaves the previous
    /// file intact.
    /// </remarks>
    /// <param name="archiveFolder">The archive folder of the repository.</param>
    /// <param name="repositoryId">The GitHub id of the repository.</param>
    /// <param name="fullName">The current <c>owner/name</c> of the repository, written to the file.</param>
    /// <param name="target">The section the archive belongs to.</param>
    /// <param name="record">The archive to record.</param>
    /// <param name="cancellationToken">Cancels reading and writing the file.</param>
    /// <returns>The state as written.</returns>
    /// <exception cref="BackupStateException">
    /// The archive file named by <paramref name="record"/> is not in <paramref name="archiveFolder"/>, or the existing
    /// state file cannot be read or belongs to a repository with another id than <paramref name="repositoryId"/>;
    /// such a file is never overwritten.
    /// </exception>
    Task<BackupState> AppendAsync(
        string archiveFolder,
        long repositoryId,
        string fullName,
        BackupTarget target,
        ArchiveRecord record,
        CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the existing state file of a repository with <paramref name="state"/>.
    /// </summary>
    /// <remarks>
    /// The file is replaced atomically and the previous version is kept next to it, so a failure leaves the previous
    /// file intact.
    /// </remarks>
    /// <param name="archiveFolder">The archive folder of the repository.</param>
    /// <param name="state">The complete new state; its repository id must match the existing file.</param>
    /// <param name="cancellationToken">Cancels reading and writing the file.</param>
    /// <returns>A task that completes when the file holds <paramref name="state"/>.</returns>
    /// <exception cref="BackupStateException">
    /// There is no state file to replace, or the existing file cannot be read or belongs to a repository with another
    /// id than <paramref name="state"/>; such a file is never overwritten.
    /// </exception>
    /// <exception cref="IOException">The new file cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The new file cannot be written.</exception>
    Task ReplaceAsync(string archiveFolder, BackupState state, CancellationToken cancellationToken);
}
