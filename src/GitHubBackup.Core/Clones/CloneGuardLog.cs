using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Clones;

/// <summary>
/// Defines the log entries of <see cref="CloneGuard"/>.
/// </summary>
internal static partial class CloneGuardLog
{
    /// <summary>
    /// Logs that a clone holds no local work.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The clone folder.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "The clone {Path} has no local changes")]
    public static partial void Passed(ILogger logger, string path);

    /// <summary>
    /// Logs that a clone holds local work, so it is left unchanged.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The clone folder.</param>
    /// <param name="problems">The problems found, separated by commas.</param>
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "The clone {Path} has local changes and is left unchanged: {Problems}")]
    public static partial void LocalChangesFound(ILogger logger, string path, string problems);
}
