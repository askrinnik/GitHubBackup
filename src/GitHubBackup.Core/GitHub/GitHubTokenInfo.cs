namespace GitHubBackup.Core.GitHub;

/// <summary>
/// What the GitHub API reports about a token: the account it signs in as, its scopes and the rate limit.
/// </summary>
/// <param name="Login">The login of the authenticated account, or <see langword="null"/> for anonymous access.</param>
/// <param name="Scopes">
/// The OAuth scopes of the token, or <see langword="null"/> when the server sends no scopes, as for a fine-grained
/// token or anonymous access.
/// </param>
/// <param name="RateLimit">The rate limit, or <see langword="null"/> when the server reports none.</param>
public sealed record GitHubTokenInfo(string? Login, IReadOnlyList<string>? Scopes, GitHubRateLimit? RateLimit)
{
    /// <summary>Gets a value indicating whether the information describes access without a token.</summary>
    public bool IsAnonymous => Login is null;
}
