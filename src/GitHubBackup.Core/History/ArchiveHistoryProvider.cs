using System.IO.Abstractions;
using GitHubBackup.Core.Archiving;
using GitHubBackup.Core.State;

namespace GitHubBackup.Core.History;

/// <summary>
/// Builds the archive history of a repository from <see cref="IBackupStateStore"/> and the archive folder.
/// </summary>
/// <param name="stateStore">Reads the state file of the repository.</param>
/// <param name="fileSystem">Checks the archive folder and the archive files.</param>
/// <param name="nameBuilder">Recognises the archive files of the repository by their names.</param>
public sealed class ArchiveHistoryProvider(
    IBackupStateStore stateStore, IFileSystem fileSystem, IArchiveNameBuilder nameBuilder) : IArchiveHistoryProvider
{
    /// <inheritdoc />
    public async Task<ArchiveHistory> LoadAsync(
        string archiveFolder, long repositoryId, string fullName, CancellationToken cancellationToken)
    {
        var state = await stateStore.LoadAsync(archiveFolder, repositoryId, fullName, cancellationToken);
        var folderExists = fileSystem.Directory.Exists(archiveFolder);
        var recorded = CreateEntries(archiveFolder, BackupTarget.Repository, state.Repository)
            .Concat(CreateEntries(archiveFolder, BackupTarget.Wiki, state.Wiki))
            .ToList();
        var unrecorded = folderExists ? CreateUnrecordedEntries(archiveFolder, fullName, recorded) : [];
        var archives = recorded.Concat(unrecorded).OrderByDescending(entry => entry.CreatedAt).ToList();
        return new(archiveFolder, folderExists, archives);
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

            // File.Exists and FileInfo.Length read only the directory entry, so a cloud placeholder counts as present
            // with its logical size and its content is not downloaded.
            var fileExists = fileSystem.File.Exists(fullPath);
            yield return new(
                target,
                record.File,
                fullPath,
                record.CreatedAt,
                record.Branch,
                record.HeadCommit,
                record.SizeBytes,
                record.Refs.GetChangedRefs(previous?.Refs),
                fileExists,
                IsInState: true,
                fileExists ? fileSystem.FileInfo.New(fullPath).Length : null);
            previous = record;
        }
    }

    /// <summary>
    /// Creates the entries of the archive files directly in the folder that no record names.
    /// </summary>
    /// <remarks>
    /// Only a file whose name <see cref="IArchiveNameBuilder.TryParse"/> accepts for this repository or its wiki is an
    /// archive file; any other file in the folder is not part of the history.
    /// </remarks>
    /// <param name="archiveFolder">The existing archive folder of the repository.</param>
    /// <param name="fullName">The <c>owner/name</c> of the repository.</param>
    /// <param name="recorded">The entries of the records of the state file.</param>
    /// <returns>One entry per archive file without a record.</returns>
    private List<ArchiveHistoryEntry> CreateUnrecordedEntries(
        string archiveFolder, string fullName, IReadOnlyList<ArchiveHistoryEntry> recorded)
    {
        var separator = fullName.IndexOf('/', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return [];
        }

        var owner = fullName[..separator];
        var repository = fullName[(separator + 1)..];

        // Windows compares file names without regard to case, so a record matches its file in any case.
        var recordedFiles = recorded.Select(entry => entry.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entries = new List<ArchiveHistoryEntry>();
        foreach (var fullPath in fileSystem.Directory.EnumerateFiles(archiveFolder))
        {
            var file = fileSystem.Path.GetFileName(fullPath);
            if (recordedFiles.Contains(file) || !nameBuilder.TryParse(file, owner, repository, out var parsed))
            {
                continue;
            }

            var size = fileSystem.FileInfo.New(fullPath).Length;
            entries.Add(new(
                parsed.Target,
                file,
                fullPath,
                parsed.CreatedAt,
                Branch: null,
                parsed.ShortHash,
                size,
                [],
                FileExists: true,
                IsInState: false,
                size));
        }

        return entries;
    }
}
