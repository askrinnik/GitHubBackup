namespace GitHubBackup.Core.Authentication;

/// <summary>
/// One link of the token chain: a place the GitHub token can come from.
/// </summary>
public interface ITokenSource
{
    /// <summary>
    /// Gets the name that identifies the source in the log. It never contains the token.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Looks for the token in this source.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the lookup.</param>
    /// <returns>The token, or <see langword="null"/> when the source has none.</returns>
    Task<string?> GetTokenAsync(CancellationToken cancellationToken);
}
