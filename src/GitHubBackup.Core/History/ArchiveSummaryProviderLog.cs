using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.History;

/// <summary>
/// Defines the log entries of <see cref="ArchiveSummaryProvider"/>.
/// </summary>
internal static partial class ArchiveSummaryProviderLog
{
    /// <summary>
    /// Logs that the run history cannot be read, so the wiki status of every repository is unknown.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The reason.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "The run history cannot be read; the wiki status of every repository is unknown")]
    public static partial void RunHistoryUnreadable(ILogger logger, Exception exception);
}
