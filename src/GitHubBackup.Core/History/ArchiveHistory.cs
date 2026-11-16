namespace GitHubBackup.Core.History;

/// <summary>
/// The archives of one repository and its wiki: the records of the state file in its archive folder and the archive
/// files in that folder without a record.
/// </summary>
/// <param name="ArchiveFolder">The absolute archive folder of the repository.</param>
/// <param name="FolderExists">Whether the archive folder exists.</param>
/// <param name="Archives">The archives of the repository and the wiki, the most recently created first.</param>
public sealed record ArchiveHistory(string ArchiveFolder, bool FolderExists, IReadOnlyList<ArchiveHistoryEntry> Archives)
{
    /// <summary>Gets the number of archives the state file records.</summary>
    public int InStateCount => Archives.Count(entry => entry.IsInState);

    /// <summary>Gets the number of archive files in the folder, with and without a record.</summary>
    public int OnDiskCount => Archives.Count(entry => entry.FileExists);

    /// <summary>Gets the number of records whose archive file is not in the folder.</summary>
    public int MissingCount => Archives.Count(entry => entry.IsInState && !entry.FileExists);

    /// <summary>Gets the number of archive files in the folder without a record.</summary>
    public int NotInStateCount => Archives.Count(entry => !entry.IsInState);

    /// <summary>Gets the total size in bytes of the archive files in the folder.</summary>
    public long SizeOnDiskBytes => Archives.Sum(entry => entry.DiskSizeBytes ?? 0);

    /// <summary>
    /// Gets the creation time of the most recent archive the state file records for the repository or its wiki, or
    /// <see langword="null"/> when it records none.
    /// </summary>
    public DateTimeOffset? LastArchiveAt =>
        Archives.Where(entry => entry.IsInState).Select(entry => (DateTimeOffset?)entry.CreatedAt).Max();
}
