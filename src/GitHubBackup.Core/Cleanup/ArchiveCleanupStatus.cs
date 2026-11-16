namespace GitHubBackup.Core.Cleanup;

/// <summary>
/// Tells how the cleanup of old archives ended.
/// </summary>
public enum ArchiveCleanupStatus
{
    /// <summary>The cleanup ran; individual files or the state file can still have failed.</summary>
    Completed,

    /// <summary>A backup run holds the run lock, so the cleanup changed nothing.</summary>
    RunLockHeld,

    /// <summary>The archives changed since the user confirmed the plan, so the cleanup changed nothing.</summary>
    PlanChanged,
}
