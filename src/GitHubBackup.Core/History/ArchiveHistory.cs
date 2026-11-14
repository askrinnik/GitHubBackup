namespace GitHubBackup.Core.History;

/// <summary>
/// The archives of one repository and its wiki, read from the state file in its archive folder.
/// </summary>
/// <param name="ArchiveFolder">The absolute archive folder of the repository.</param>
/// <param name="FolderExists">Whether the archive folder exists.</param>
/// <param name="Archives">The archives of the repository and the wiki, the most recently created first.</param>
public sealed record ArchiveHistory(string ArchiveFolder, bool FolderExists, IReadOnlyList<ArchiveHistoryEntry> Archives);
