namespace GitHubBackup.Core.GitHub;

/// <summary>
/// Asks the configured GitHub API what a given token can do, independently of the token the backup uses.
/// </summary>
public interface IGitHubTokenInspector
{
    /// <summary>
    /// Reports the account, the scopes and the rate limit of <paramref name="token"/>.
    /// </summary>
    /// <param name="token">The token to check, or <see langword="null"/> to check anonymous access.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>What the server reports about the token.</returns>
    /// <exception cref="GitHubAuthenticationException">The server rejects the token.</exception>
    /// <exception cref="GitHubApiException">Any other failure of the request.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
    Task<GitHubTokenInfo> InspectAsync(string? token, CancellationToken cancellationToken);
}
