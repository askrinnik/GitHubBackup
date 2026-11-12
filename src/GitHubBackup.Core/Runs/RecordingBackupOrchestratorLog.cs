using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Runs;

/// <summary>
/// Defines the log entries of <see cref="RecordingBackupOrchestrator"/>.
/// </summary>
internal static partial class RecordingBackupOrchestratorLog
{
    /// <summary>
    /// Logs that the run was added to the run history.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exitCode">The exit code the run finished with.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "The run was recorded in the run history with exit code {ExitCode}")]
    public static partial void RunRecorded(ILogger logger, ExitCode exitCode);

    /// <summary>
    /// Logs that the run cannot be added to the run history; the run keeps its exit code.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="databasePath">The path of the run history database.</param>
    /// <param name="exception">The reason.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "The run was not recorded in the run history database {DatabasePath}")]
    public static partial void RunNotRecorded(ILogger logger, string databasePath, Exception exception);
}
