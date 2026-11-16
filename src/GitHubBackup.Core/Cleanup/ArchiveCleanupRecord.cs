using GitHubBackup.Core.State;

namespace GitHubBackup.Core.Cleanup;

/// <summary>
/// A record of <c>.backup-state.json</c> that the cleanup removes.
/// </summary>
/// <param name="Target">The section the record belongs to.</param>
/// <param name="File">The file name the record names.</param>
public sealed record ArchiveCleanupRecord(BackupTarget Target, string File);
