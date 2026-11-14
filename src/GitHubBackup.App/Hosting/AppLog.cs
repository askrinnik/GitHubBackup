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

    /// <summary>
    /// Logs that a backup run started from the main window failed with an unexpected exception.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The exception, logged with its stack trace.</param>
    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "The backup run started from the main window failed")]
    public static partial void BackupRunFailed(ILogger logger, Exception exception);

    /// <summary>
    /// Logs that the repository tree of the main window cannot be read.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The exception, logged with its stack trace.</param>
    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "The repository tree cannot be read")]
    public static partial void RepositoryTreeLoadFailed(ILogger logger, Exception exception);

    /// <summary>
    /// Logs that the settings editor cannot read <c>backup-config.json</c>.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The exception, logged with its stack trace.</param>
    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "The settings editor cannot read the backup configuration")]
    public static partial void SettingsLoadFailed(ILogger logger, Exception exception);

    /// <summary>
    /// Logs that the settings editor cannot write a settings file.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The exception, logged with its stack trace.</param>
    [LoggerMessage(EventId = 7, Level = LogLevel.Error, Message = "The settings editor cannot write the settings")]
    public static partial void SettingsSaveFailed(ILogger logger, Exception exception);

    /// <summary>
    /// Logs that the token check of the settings editor failed unexpectedly.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The exception, logged with its stack trace.</param>
    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "The token check of the settings editor failed")]
    public static partial void TokenCheckFailed(ILogger logger, Exception exception);

    /// <summary>
    /// Logs that the history screen cannot read the run history.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The exception, logged with its stack trace.</param>
    [LoggerMessage(EventId = 9, Level = LogLevel.Warning, Message = "The history screen cannot read the run history")]
    public static partial void RunHistoryLoadFailed(ILogger logger, Exception exception);

    /// <summary>
    /// Logs that the history screen cannot read the repositories of the backup configuration.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The exception, logged with its stack trace.</param>
    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "The history screen cannot read the repositories")]
    public static partial void HistoryRepositoriesLoadFailed(ILogger logger, Exception exception);

    /// <summary>
    /// Logs that the history screen cannot read the archive history of a repository.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="repository">The repository as <c>owner/name</c>.</param>
    /// <param name="exception">The exception, logged with its stack trace.</param>
    [LoggerMessage(EventId = 11, Level = LogLevel.Warning, Message = "The history screen cannot read the archive history of {Repository}")]
    public static partial void ArchiveHistoryLoadFailed(ILogger logger, string repository, Exception exception);

    /// <summary>
    /// Logs that Windows Explorer cannot be started.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The folder or file Explorer was to show.</param>
    /// <param name="exception">The exception, logged with its stack trace.</param>
    [LoggerMessage(EventId = 12, Level = LogLevel.Error, Message = "Windows Explorer cannot be started for {Path}")]
    public static partial void ExplorerFailed(ILogger logger, string path, Exception exception);
}
