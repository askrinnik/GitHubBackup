namespace GitHubBackup.Core.GitHub;

/// <summary>
/// Reports that a GitHub API request failed: an unexpected HTTP status, a network failure or a timeout.
/// </summary>
/// <remarks>
/// The derived types single out the outcomes callers decide on. The message never carries the token or a request
/// header.
/// </remarks>
/// <param name="message">The message, naming the resource and the HTTP status when there is one.</param>
/// <param name="resource">The API resource the request was for, such as <c>repos/owner/name</c>.</param>
/// <param name="statusCode">The HTTP status code, or <see langword="null"/> when no response arrived.</param>
public class GitHubApiException(string message, string resource, int? statusCode) : Exception(message)
{
    /// <summary>Gets the API resource the request was for, such as <c>repos/owner/name</c>.</summary>
    public string Resource { get; } = resource;

    /// <summary>Gets the HTTP status code, or <see langword="null"/> when no response arrived.</summary>
    public int? StatusCode { get; } = statusCode;
}
