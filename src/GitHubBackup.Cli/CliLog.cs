using GitHubBackup.Core;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Cli;

/// <summary>
/// Defines the log entries of the console application.
/// </summary>
internal static partial class CliLog
{
    /// <summary>
    /// Logs that a run of the console application has started.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="version">The version of the executable.</param>
    /// <param name="environment">The host environment name.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "GitHubBackup.Cli {Version} started in the {Environment} environment")]
    public static partial void RunStarted(ILogger logger, string? version, string environment);

    /// <summary>
    /// Logs that a run of the console application has finished.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exitCode">The exit code of the process.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "GitHubBackup.Cli finished with exit code {ExitCode}")]
    public static partial void RunFinished(ILogger logger, ExitCode exitCode);
}
