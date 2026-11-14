using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.Clones;

/// <summary>
/// Defines the log entries of <see cref="JsonCloneLocationStore"/>.
/// </summary>
internal static partial class CloneLocationLog
{
    /// <summary>
    /// Logs that the file of clone folders was read.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The path of the file.</param>
    /// <param name="count">The number of repositories recorded.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Loaded the clone folders {Path} with {Count} repositories")]
    public static partial void Loaded(ILogger logger, string path, int count);

    /// <summary>
    /// Logs that the file of clone folders does not exist yet.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The path of the file.</param>
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "The clone folders {Path} do not exist; no earlier clone folder is known")]
    public static partial void NotFound(ILogger logger, string path);

    /// <summary>
    /// Logs that the file of clone folders cannot be read, so no earlier clone folder is known and no clone is moved.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The path of the file.</param>
    /// <param name="reason">Why the file cannot be read.</param>
    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Cannot read the clone folders {Path} ({Reason}); no earlier clone folder is known and no clone is moved")]
    public static partial void NotRead(ILogger logger, string path, string reason);

    /// <summary>
    /// Logs that the file of clone folders was written.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The path of the file.</param>
    /// <param name="count">The number of repositories recorded.</param>
    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Recorded the clone folders of {Count} repositories in {Path}")]
    public static partial void Saved(ILogger logger, string path, int count);
}
