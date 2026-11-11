using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Authentication;

/// <summary>
/// Defines the log entries of <see cref="TokenProvider"/>.
/// </summary>
internal static partial class TokenProviderLog
{
    /// <summary>
    /// Logs the source that supplied the token.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="source">The source name.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "GitHub token found in {Source}")]
    public static partial void TokenFound(ILogger logger, string source);

    /// <summary>
    /// Logs that no source has a token.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "No GitHub token found: anonymous mode, only public repositories are visible and the limit is 60 requests per hour")]
    public static partial void AnonymousMode(ILogger logger);
}
