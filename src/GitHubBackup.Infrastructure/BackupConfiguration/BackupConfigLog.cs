using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.BackupConfiguration;

/// <summary>
/// Defines the log entries of <see cref="JsonBackupConfigStore"/>.
/// </summary>
internal static partial class BackupConfigLog
{
    /// <summary>
    /// Logs a validation warning found while reading the configuration file.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The path of the configuration file.</param>
    /// <param name="warning">The warning.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Backup configuration {Path}: {Warning}")]
    public static partial void ValidationWarning(ILogger logger, string path, string warning);

    /// <summary>
    /// Logs that the configuration file was read.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The path of the configuration file.</param>
    /// <param name="accounts">The number of tracked accounts.</param>
    /// <param name="repositories">The number of standalone repositories.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Loaded the backup configuration {Path} with {Accounts} accounts and {Repositories} standalone repositories")]
    public static partial void Loaded(ILogger logger, string path, int accounts, int repositories);

    /// <summary>
    /// Logs that the configuration file was replaced.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The path of the configuration file.</param>
    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Saved the backup configuration {Path}")]
    public static partial void Saved(ILogger logger, string path);
}
