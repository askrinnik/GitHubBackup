using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.Archiving;

/// <summary>
/// Defines the log entries of <see cref="SevenZipArchiver"/>.
/// </summary>
internal static partial class SevenZipArchiverLog
{
    /// <summary>
    /// Logs that an archive is about to be created.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="source">The clone folder.</param>
    /// <param name="archive">The temporary archive file.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Archiving {Source} into {Archive}")]
    public static partial void Creating(ILogger logger, string source, string archive);

    /// <summary>
    /// Logs that the new archive is about to be tested.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="archive">The temporary archive file.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Verifying {Archive}")]
    public static partial void Verifying(ILogger logger, string archive);

    /// <summary>
    /// Logs that the test of the archive is switched off.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="archive">The temporary archive file.</param>
    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "Verification of {Archive} is skipped: Backup:VerifyArchive is off")]
    public static partial void VerificationSkipped(ILogger logger, string archive);

    /// <summary>
    /// Logs that the finished archive is about to be moved to the archive folder.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="archive">The temporary archive file.</param>
    /// <param name="destination">The final path of the archive.</param>
    /// <param name="sameVolume">Whether the move is a rename on one volume.</param>
    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Information,
        Message = "Moving {Archive} to {Destination} (same volume: {SameVolume})")]
    public static partial void Moving(ILogger logger, string archive, string destination, bool sameVolume);

    /// <summary>
    /// Logs that a temporary file could not be deleted.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The file that stays on disk.</param>
    /// <param name="error">The reason.</param>
    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Cannot delete the temporary file {Path}: {Error}")]
    public static partial void CleanupFailed(ILogger logger, string path, string error);

    /// <summary>
    /// Logs that the archive is in place but its temporary source could not be deleted.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The temporary archive that stays on disk.</param>
    /// <param name="error">The reason.</param>
    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Warning,
        Message = "The archive is in place, but the temporary file {Path} was not deleted: {Error}")]
    public static partial void SourceNotDeleted(ILogger logger, string path, string error);
}
