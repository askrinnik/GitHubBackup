using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.Git;

/// <summary>
/// Defines the log entries of <see cref="GitClient"/>.
/// </summary>
internal static partial class GitClientLog
{
    /// <summary>
    /// Logs that a repository is about to be cloned.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="url">The remote URL, with secrets masked.</param>
    /// <param name="path">The clone folder.</param>
    /// <param name="branch">The branch to check out.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Cloning {Url} into {Path}, branch {Branch}")]
    public static partial void Cloning(ILogger logger, string url, string path, string branch);

    /// <summary>
    /// Logs that a clone is about to be updated from its server.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The clone folder.</param>
    /// <param name="branch">The branch to check out.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Updating {Path}, branch {Branch}")]
    public static partial void Updating(ILogger logger, string path, string branch);
}
