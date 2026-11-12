using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.History;

/// <summary>
/// Defines the log entries of <see cref="HistoryDatabaseInitializer"/>.
/// </summary>
internal static partial class HistoryDatabaseLog
{
    /// <summary>
    /// Logs that the run history database is ready for the run.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The path of the database file.</param>
    /// <param name="appliedMigrations">The number of migrations applied at this start; zero when the schema was current.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "The run history database {Path} is ready; applied {AppliedMigrations} migrations")]
    public static partial void Ready(ILogger logger, string path, int appliedMigrations);
}
