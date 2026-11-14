using System.IO.Abstractions;
using GitHubBackup.Core.State;

namespace GitHubBackup.Core.History;

/// <summary>
/// Builds the archive history of a repository from <see cref="IBackupStateStore"/> and the archive folder.
/// </summary>
/// <param name="stateStore">Reads the state file of the repository.</param>
/// <param name="fileSystem">Checks the archive folder and the archive files.</param>
public sealed class ArchiveHistoryProvider(IBackupStateStore stateStore, IFileSystem fileSystem) : IArchiveHistoryProvider
{
    /// <inheritdoc />
    public async Task<ArchiveHistory> LoadAsync(
        string archiveFolder, long repositoryId, string fullName, CancellationToken cancellationToken)
    {
        var state = await stateStore.LoadAsync(archiveFolder, repositoryId, fullName, cancellationToken);
        var archives = CreateEntries(archiveFolder, BackupTarget.Repository, state.Repository)
            .Concat(CreateEntries(archiveFolder, BackupTarget.Wiki, state.Wiki))
            .OrderByDescending(entry => entry.CreatedAt)
            .ToList();
        return new(archiveFolder, fileSystem.Directory.Exists(archiveFolder), archives);
    }

    /// <summary>
    /// Creates the entries of the archives of one section.
    /// </summary>
    /// <remarks>
    /// The changed refs of an archive are those that differ from the previous archive of the same section, the rule a
    /// run applies when it records the archive, so the wiki is never compared with the repository.
    /// </remarks>
    /// <param name="archiveFolder">The archive folder of the repository.</param>
    /// <param name="target">The target the section belongs to.</param>
    /// <param name="section">The archives of the target, oldest first.</param>
    /// <returns>One entry per archive, oldest first.</returns>
    private IEnumerable<ArchiveHistoryEntry> CreateEntries(
        string archiveFolder, BackupTarget target, BackupStateSection section)
    {
        ArchiveRecord? previous = null;
        foreach (var record in section.Archives)
        {
            var fullPath = fileSystem.Path.Combine(archiveFolder, record.File);

            // File.Exists reads only the directory entry, so a cloud placeholder counts as present and its content is
            // not downloaded.
            yield return new(
                target,
                record.File,
                fullPath,
                record.CreatedAt,
                record.Branch,
                record.HeadCommit,
                record.SizeBytes,
                record.Refs.GetChangedRefs(previous?.Refs),
                fileSystem.File.Exists(fullPath));
            previous = record;
        }
    }
}
