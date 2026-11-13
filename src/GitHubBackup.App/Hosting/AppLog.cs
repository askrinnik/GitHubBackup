using Microsoft.Extensions.Logging;

namespace GitHubBackup.App.Hosting;

/// <summary>
/// Defines the log entries of the WPF application.
/// </summary>
internal static partial class AppLog
{
    /// <summary>
    /// Logs that the WPF application has started.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="version">The version of the executable.</param>
    /// <param name="environment">The host environment name.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "GitHubBackup.App {Version} started in the {Environment} environment")]
    public static partial void Started(ILogger logger, string? version, string environment);

    /// <summary>
    /// Logs that the WPF application has stopped.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "GitHubBackup.App stopped")]
    public static partial void Stopped(ILogger logger);

    /// <summary>
    /// Logs that the host did not start because the configuration is invalid.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="errors">The configuration errors, one per line.</param>
    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "GitHubBackup.App did not start because of configuration errors: {Errors}")]
    public static partial void ConfigurationInvalid(ILogger logger, string errors);
}
