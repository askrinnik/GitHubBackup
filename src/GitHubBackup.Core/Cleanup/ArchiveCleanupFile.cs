using GitHubBackup.Core.State;

namespace GitHubBackup.Core.Cleanup;

/// <summary>
/// An archive file that the cleanup moves to the Recycle Bin.
/// </summary>
/// <param name="Target">Whether the archive holds the repository or its wiki.</param>
/// <param name="File">The file name of the archive in the archive folder.</param>
/// <param name="FullPath">The absolute path of the archive file.</param>
/// <param name="SizeBytes">The size of the file on the disk in bytes.</param>
/// <param name="IsInState">
/// Whether <c>.backup-state.json</c> records the archive, so its record is removed once the file is in the Recycle Bin.
/// </param>
public sealed record ArchiveCleanupFile(
    BackupTarget Target, string File, string FullPath, long SizeBytes, bool IsInState);
