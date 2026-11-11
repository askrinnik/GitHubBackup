using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.GitHub;

/// <summary>
/// Defines the log entries of <see cref="OctokitGitHubApi"/>.
/// </summary>
internal static partial class OctokitGitHubApiLog
{
    /// <summary>
    /// Logs the rate limit state the server reported with the last response.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="remaining">The number of requests left in the current window.</param>
    /// <param name="limit">The number of requests allowed in the current window.</param>
    /// <param name="resetAt">The time the window resets.</param>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "GitHub API rate limit: {Remaining}/{Limit} remaining, resets at {ResetAt}")]
    public static partial void RateLimit(ILogger logger, int remaining, int limit, DateTimeOffset resetAt);

    /// <summary>
    /// Logs that the repositories of the authenticated user are not requested because no token is configured.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "No GitHub token is configured; the repositories of the authenticated user are not requested")]
    public static partial void CurrentUserRepositoriesSkipped(ILogger logger);
}
