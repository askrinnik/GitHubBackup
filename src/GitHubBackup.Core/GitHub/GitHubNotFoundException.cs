namespace GitHubBackup.Core.GitHub;

/// <summary>
/// Reports that a resource does not exist or is not visible with the current credentials (HTTP 404).
/// </summary>
/// <remarks>GitHub answers 404, not 403, for a private repository the token cannot see.</remarks>
/// <param name="message">The message, naming the resource.</param>
/// <param name="resource">The API resource the request was for.</param>
public sealed class GitHubNotFoundException(string message, string resource)
    : GitHubApiException(message, resource, 404);
