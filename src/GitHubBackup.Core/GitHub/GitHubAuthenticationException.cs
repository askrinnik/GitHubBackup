namespace GitHubBackup.Core.GitHub;

/// <summary>
/// Reports that GitHub rejected the credentials (HTTP 401): the token is invalid, expired or revoked.
/// </summary>
/// <param name="message">The message, naming the resource.</param>
/// <param name="resource">The API resource the request was for.</param>
public sealed class GitHubAuthenticationException(string message, string resource)
    : GitHubApiException(message, resource, 401);
