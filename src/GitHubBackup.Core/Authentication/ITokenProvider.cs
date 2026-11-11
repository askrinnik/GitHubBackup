namespace GitHubBackup.Core.Authentication;

/// <summary>
/// Finds the GitHub token by walking the chain of <see cref="ITokenSource"/> links.
/// </summary>
public interface ITokenProvider
{
    /// <summary>
    /// Returns the token of the first source that has one, or the anonymous token when none has.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the lookup.</param>
    /// <returns>The token found, or <see cref="GitHubToken.Anonymous"/>.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
    Task<GitHubToken> GetTokenAsync(CancellationToken cancellationToken);
}
