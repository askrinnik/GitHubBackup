using GitHubBackup.Core.State;

namespace GitHubBackup.Core.History;

/// <summary>
/// One archive of a repository or its wiki, as the archive history shows it.
/// </summary>
/// <param name="Target">Whether the archive holds the repository or its wiki.</param>
/// <param name="File">The file name of the archive in the archive folder.</param>
/// <param name="FullPath">The absolute path of the archive file.</param>
/// <param name="CreatedAt">The local time the archive was created, with its offset from UTC.</param>
/// <param name="Branch">
/// The branch checked out in the archived clone, or <see langword="null"/> for a file without a record, whose name does
/// not carry the branch.
/// </param>
/// <param name="HeadCommit">
/// The full id of the commit checked out in the archived clone; the hash from the name for a file without a record.
/// </param>
/// <param name="SizeBytes">
/// The size of the archive file in bytes when it was created; the size on the disk for a file without a record.
/// </param>
/// <param name="ChangedRefs">
/// The refs that differ from the previous archive of the same target; every ref of the first archive.
/// </param>
/// <param name="FileExists">
/// Whether the archive file is in the archive folder; a cloud placeholder whose content is not on the disk counts as
/// present.
/// </param>
/// <param name="IsInState">
/// Whether <c>.backup-state.json</c> records the archive; <see langword="false"/> for an archive file in the folder
/// without a record.
/// </param>
/// <param name="DiskSizeBytes">
/// The size of the archive file on the disk, or <see langword="null"/> when the file is missing. A cloud placeholder
/// reports its logical size without its content being downloaded.
/// </param>
public sealed record ArchiveHistoryEntry(
    BackupTarget Target,
    string File,
    string FullPath,
    DateTimeOffset CreatedAt,
    string? Branch,
    string HeadCommit,
    long SizeBytes,
    IReadOnlyList<string> ChangedRefs,
    bool FileExists,
    bool IsInState,
    long? DiskSizeBytes);
