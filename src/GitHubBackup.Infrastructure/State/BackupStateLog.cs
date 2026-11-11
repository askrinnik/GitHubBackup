using GitHubBackup.Core.State;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.State;

/// <summary>
/// Defines the log entries of <see cref="JsonBackupStateStore"/>.
/// </summary>
internal static partial class BackupStateLog
{
    /// <summary>
    /// Logs that the state file was read.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The path of the state file.</param>
    /// <param name="repositoryArchives">The number of archives of the repository.</param>
    /// <param name="wikiArchives">The number of archives of the wiki.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Loaded the backup state {Path} with {RepositoryArchives} repository archives and {WikiArchives} wiki archives")]
    public static partial void Loaded(ILogger logger, string path, int repositoryArchives, int wikiArchives);

    /// <summary>
    /// Logs that there is no state file, so the repository has no archives yet.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The path of the missing state file.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "The backup state {Path} does not exist; starting with no archives")]
    public static partial void NotFound(ILogger logger, string path);

    /// <summary>
    /// Logs that an archive was recorded in the state file.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The path of the state file.</param>
    /// <param name="target">The section the archive was added to.</param>
    /// <param name="file">The file name of the archive.</param>
    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Recorded the {Target} archive {File} in the backup state {Path}")]
    public static partial void ArchiveAppended(ILogger logger, string path, BackupTarget target, string file);
}
