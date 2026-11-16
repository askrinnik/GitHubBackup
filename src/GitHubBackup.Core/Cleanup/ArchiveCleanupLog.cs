using GitHubBackup.Core.State;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Cleanup;

/// <summary>
/// Defines the log entries of <see cref="ArchiveCleanupService"/>.
/// </summary>
internal static partial class ArchiveCleanupLog
{
    /// <summary>
    /// Logs that the cleanup of a repository starts.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="repository">The repository as <c>owner/name</c>.</param>
    /// <param name="files">The number of files to move to the Recycle Bin.</param>
    /// <param name="records">The number of records without a file to remove.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Cleaning up the old archives of {Repository}: {Files} files to the Recycle Bin, {Records} records without a file")]
    public static partial void Started(ILogger logger, string repository, int files, int records);

    /// <summary>
    /// Logs that an archive file went to the Recycle Bin.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="repository">The repository as <c>owner/name</c>.</param>
    /// <param name="target">The target the archive belongs to.</param>
    /// <param name="path">The path of the archive file.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Moved the {Target} archive {Path} of {Repository} to the Recycle Bin")]
    public static partial void FileRecycled(ILogger logger, string repository, BackupTarget target, string path);

    /// <summary>
    /// Logs that an archive file could not go to the Recycle Bin and stays with its record.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="repository">The repository as <c>owner/name</c>.</param>
    /// <param name="path">The path of the archive file.</param>
    /// <param name="error">Why the file stays.</param>
    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "The archive {Path} of {Repository} stays in place: {Error}")]
    public static partial void FileFailed(ILogger logger, string repository, string path, string error);

    /// <summary>
    /// Logs that a record was removed from the state file.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="repository">The repository as <c>owner/name</c>.</param>
    /// <param name="target">The section the record belonged to.</param>
    /// <param name="file">The file name the record named.</param>
    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Removed the {Target} record {File} of {Repository} from the backup state")]
    public static partial void RecordRemoved(ILogger logger, string repository, BackupTarget target, string file);

    /// <summary>
    /// Logs that the state file could not be written, so it keeps the records of the moved files.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="repository">The repository as <c>owner/name</c>.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "The backup state of {Repository} cannot be written after the cleanup; it keeps the records of the moved files")]
    public static partial void StateWriteFailed(ILogger logger, string repository, Exception exception);

    /// <summary>
    /// Logs that the cleanup did not start because a backup run holds the run lock.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="repository">The repository as <c>owner/name</c>.</param>
    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "The cleanup of {Repository} did not start: a backup is running")]
    public static partial void RunLockHeld(ILogger logger, string repository);

    /// <summary>
    /// Logs that the cleanup did not start because the archives changed after the user confirmed the plan.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="repository">The repository as <c>owner/name</c>.</param>
    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message = "The cleanup of {Repository} did not start: the archives changed after the confirmation")]
    public static partial void PlanChanged(ILogger logger, string repository);
}
