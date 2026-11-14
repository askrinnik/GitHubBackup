namespace GitHubBackup.Core.GitHub;

/// <summary>
/// The state of the GitHub API rate limit as the server reports it.
/// </summary>
/// <param name="Limit">The number of requests allowed in the current window.</param>
/// <param name="Remaining">The number of requests left in the current window.</param>
/// <param name="ResetsAt">The time the window ends and the limit is restored.</param>
public sealed record GitHubRateLimit(int Limit, int Remaining, DateTimeOffset ResetsAt);
