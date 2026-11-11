namespace GitHubBackup.Core.GitHub;

/// <summary>
/// Reports that GitHub refused access to a resource (HTTP 403) for a reason other than the rate limit.
/// </summary>
/// <param name="message">The message, naming the resource.</param>
/// <param name="resource">The API resource the request was for.</param>
public sealed class GitHubAccessDeniedException(string message, string resource)
    : GitHubApiException(message, resource, 403);
