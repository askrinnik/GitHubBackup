using GitHubBackup.Core.State;

namespace GitHubBackup.Core.Cleanup;

/// <summary>
/// Removes the old archives of one repository and its wiki: moves their files to the Recycle Bin and removes their
/// records from <c>.backup-state.json</c>.
/// </summary>
public interface IArchiveCleanupService
{
    /// <summary>
    /// Builds the plan the user confirms, without taking the run lock.
    /// </summary>
    /// <param name="archiveFolder">The absolute archive folder of the repository.</param>
    /// <param name="repositoryId">The GitHub id of the repository, which the state file must hold.</param>
    /// <param name="fullName">The <c>owner/name</c> of the repository.</param>
    /// <param name="cancellationToken">Cancels reading the files.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="BackupStateException">
    /// The state file cannot be read or holds the state of another repository.
    /// </exception>
    /// <exception cref="IOException">The archive folder cannot be listed.</exception>
    /// <exception cref="UnauthorizedAccessException">The archive folder cannot be listed.</exception>
    Task<ArchiveCleanupPlan> PrepareAsync(
        string archiveFolder, long repositoryId, string fullName, CancellationToken cancellationToken);

    /// <summary>
    /// Runs a confirmed plan under the run lock.
    /// </summary>
    /// <remarks>
    /// The plan is built again under the lock; when it differs from <paramref name="confirmedPlan"/>, nothing changes.
    /// A file that cannot be moved keeps its record; the state file is written once, after the files are moved, and
    /// only when a record is removed.
    /// </remarks>
    /// <param name="archiveFolder">The absolute archive folder of the repository.</param>
    /// <param name="repositoryId">The GitHub id of the repository, which the state file must hold.</param>
    /// <param name="fullName">The <c>owner/name</c> of the repository.</param>
    /// <param name="confirmedPlan">The plan the user confirmed.</param>
    /// <param name="cancellationToken">Cancels before the next file is moved.</param>
    /// <returns>What the cleanup did.</returns>
    /// <exception cref="BackupStateException">
    /// The state file cannot be read under the lock or holds the state of another repository; nothing changed.
    /// </exception>
    /// <exception cref="IOException">The archive folder cannot be listed under the lock; nothing changed.</exception>
    /// <exception cref="UnauthorizedAccessException">
    /// The archive folder cannot be listed under the lock; nothing changed.
    /// </exception>
    Task<ArchiveCleanupResult> ExecuteAsync(
        string archiveFolder,
        long repositoryId,
        string fullName,
        ArchiveCleanupPlan confirmedPlan,
        CancellationToken cancellationToken);
}
