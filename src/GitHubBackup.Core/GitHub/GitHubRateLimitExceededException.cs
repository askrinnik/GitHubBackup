namespace GitHubBackup.Core.GitHub;

/// <summary>
/// Reports that the GitHub API rate limit is exhausted, either the hourly limit or a secondary limit.
/// </summary>
/// <param name="message">The message, naming the resource and the time the limit resets.</param>
/// <param name="resource">The API resource the request was for.</param>
/// <param name="statusCode">The HTTP status code of the response, 403 or 429.</param>
/// <param name="resetAt">The time the limit resets, or <see langword="null"/> when the server did not say.</param>
/// <param name="limit">The number of requests allowed in the current window, or 0 when unknown.</param>
/// <param name="remaining">The number of requests left in the current window.</param>
public sealed class GitHubRateLimitExceededException(
    string message,
    string resource,
    int statusCode,
    DateTimeOffset? resetAt,
    int limit,
    int remaining) : GitHubApiException(message, resource, statusCode)
{
    /// <summary>Gets the time the limit resets, or <see langword="null"/> when the server did not say.</summary>
    public DateTimeOffset? ResetAt { get; } = resetAt;

    /// <summary>Gets the number of requests allowed in the current window, or 0 when unknown.</summary>
    public int Limit { get; } = limit;

    /// <summary>Gets the number of requests left in the current window.</summary>
    public int Remaining { get; } = remaining;
}
