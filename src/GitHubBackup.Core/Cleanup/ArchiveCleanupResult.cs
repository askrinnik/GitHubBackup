namespace GitHubBackup.Core.Cleanup;

/// <summary>
/// What the cleanup of old archives of one repository did.
/// </summary>
/// <param name="Status">How the cleanup ended.</param>
/// <param name="RecycledFiles">The archive files moved to the Recycle Bin.</param>
/// <param name="RemovedRecords">
/// The records removed from <c>.backup-state.json</c>; empty when the state file was not written.
/// </param>
/// <param name="Errors">The files that stayed and a failure to write the state file, one sentence each.</param>
public sealed record ArchiveCleanupResult(
    ArchiveCleanupStatus Status,
    IReadOnlyList<ArchiveCleanupFile> RecycledFiles,
    IReadOnlyList<ArchiveCleanupRecord> RemovedRecords,
    IReadOnlyList<string> Errors)
{
    /// <summary>Gets the result of a cleanup that did not start because a backup run holds the run lock.</summary>
    public static ArchiveCleanupResult RunLockHeld { get; } = new(ArchiveCleanupStatus.RunLockHeld, [], [], []);

    /// <summary>Gets the result of a cleanup that did not start because its plan is out of date.</summary>
    public static ArchiveCleanupResult PlanChanged { get; } = new(ArchiveCleanupStatus.PlanChanged, [], [], []);
}
