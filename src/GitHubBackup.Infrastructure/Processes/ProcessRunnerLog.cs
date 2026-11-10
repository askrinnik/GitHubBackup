using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.Processes;

/// <summary>
/// Defines the log entries of <see cref="ProcessRunner"/>.
/// </summary>
internal static partial class ProcessRunnerLog
{
    /// <summary>
    /// Logs that a process is about to start.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="tool">The name of the tool.</param>
    /// <param name="commandLine">The command line, with secrets masked.</param>
    /// <param name="workingDirectory">The folder the process runs in.</param>
    /// <param name="environmentVariables">The names of the environment variables set for the process.</param>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Starting {Tool}: {CommandLine} in {WorkingDirectory} with environment variables [{EnvironmentVariables}]")]
    public static partial void Starting(
        ILogger logger, string tool, string commandLine, string workingDirectory, string environmentVariables);

    /// <summary>
    /// Logs one line the process wrote.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="tool">The name of the tool.</param>
    /// <param name="stream">The stream the line came from: <c>stdout</c> or <c>stderr</c>.</param>
    /// <param name="line">The line, with secrets masked.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "{Tool} {Stream}: {Line}")]
    public static partial void OutputLine(ILogger logger, string tool, string stream, string line);

    /// <summary>
    /// Logs that the process exited.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="level">
    /// <see cref="LogLevel.Information"/> for exit code 0, <see cref="LogLevel.Warning"/> otherwise.
    /// </param>
    /// <param name="tool">The name of the tool.</param>
    /// <param name="exitCode">The exit code.</param>
    /// <param name="duration">The time from the start to the exit.</param>
    [LoggerMessage(EventId = 3, Message = "{Tool} exited with code {ExitCode} after {Duration}")]
    public static partial void Exited(ILogger logger, LogLevel level, string tool, int exitCode, TimeSpan duration);

    /// <summary>
    /// Logs that the run was cancelled and the process tree was killed.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="tool">The name of the tool.</param>
    /// <param name="duration">The time from the start to the end of the killed process.</param>
    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "{Tool} was cancelled and killed after {Duration}")]
    public static partial void Cancelled(ILogger logger, string tool, TimeSpan duration);

    /// <summary>
    /// Logs that the process tree could not be killed.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="tool">The name of the tool.</param>
    /// <param name="error">The message of the failure, with secrets masked.</param>
    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Cannot kill {Tool} and the processes it started: {Error}")]
    public static partial void KillFailed(ILogger logger, string tool, string error);

    /// <summary>
    /// Logs that a cancelled run closed the output of the process because it stayed open after the kill, held by a
    /// process outside the killed tree.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="tool">The name of the tool.</param>
    /// <param name="timeout">How long the run waited for the output to close.</param>
    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "{Tool} output stayed open {Timeout} after the kill; stopped reading it")]
    public static partial void OutputAbandoned(ILogger logger, string tool, TimeSpan timeout);
}
