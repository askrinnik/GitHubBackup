using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.Tools;

/// <summary>
/// Defines the log entries of <see cref="ToolLocator"/>.
/// </summary>
internal static partial class ToolLocatorLog
{
    /// <summary>
    /// Logs that a tool was found and reported its version.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="tool">The name of the tool.</param>
    /// <param name="version">The version the tool reported.</param>
    /// <param name="path">The path of the executable that ran.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "{Tool} {Version} found at {Path}")]
    public static partial void ToolFound(ILogger logger, string tool, string version, string path);
}
