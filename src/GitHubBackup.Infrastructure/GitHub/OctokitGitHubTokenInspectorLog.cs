using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.GitHub;

/// <summary>
/// Defines the log entries of <see cref="OctokitGitHubTokenInspector"/>.
/// </summary>
internal static partial class OctokitGitHubTokenInspectorLog
{
    /// <summary>
    /// Logs the short description of a check before its request.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="description">The description of the check.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "GitHub API: {Description}")]
    public static partial void Requesting(ILogger logger, string description);

    /// <summary>
    /// Logs that a check completed successfully.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="resource">The API resource.</param>
    /// <param name="duration">The time from the request to the response.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "GitHub API {Resource} completed after {Duration}")]
    public static partial void RequestCompleted(ILogger logger, string resource, TimeSpan duration);

    /// <summary>
    /// Logs the rate limit state the server reported to a check.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="remaining">The number of requests left in the current window.</param>
    /// <param name="limit">The number of requests allowed in the current window.</param>
    /// <param name="resetAt">The time the window resets.</param>
    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Debug,
        Message = "GitHub API rate limit: {Remaining}/{Limit} remaining, resets at {ResetAt}")]
    public static partial void RateLimit(ILogger logger, int remaining, int limit, DateTimeOffset resetAt);
}
