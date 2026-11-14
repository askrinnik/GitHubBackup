using GitHubBackup.Core.State;

namespace GitHubBackup.Core.History;

/// <summary>
/// Reads the archive history of a repository from its state file and checks which archive files are on the disk.
/// </summary>
public interface IArchiveHistoryProvider
{
    /// <summary>
    /// Reads the archives of a repository and its wiki.
    /// </summary>
    /// <remarks>Only the presence of each archive file is checked; its content is never read.</remarks>
    /// <param name="archiveFolder">The absolute archive folder of the repository.</param>
    /// <param name="repositoryId">The GitHub id of the repository, which the state file must hold.</param>
    /// <param name="fullName">The <c>owner/name</c> of the repository.</param>
    /// <param name="cancellationToken">Cancels reading the state file.</param>
    /// <returns>The archive history; without archives when the state file or the folder does not exist.</returns>
    /// <exception cref="BackupStateException">
    /// The state file cannot be read or holds the state of another repository.
    /// </exception>
    Task<ArchiveHistory> LoadAsync(
        string archiveFolder, long repositoryId, string fullName, CancellationToken cancellationToken);
}
