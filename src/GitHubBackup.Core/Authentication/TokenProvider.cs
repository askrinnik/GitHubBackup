using GitHubBackup.Core.Security;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Authentication;

/// <summary>
/// Walks the registered <see cref="ITokenSource"/> links in order and takes the first non-blank token.
/// </summary>
/// <param name="sources">The token sources, in priority order.</param>
/// <param name="secretMasker">The masker that learns the token that is found.</param>
/// <param name="logger">The logger.</param>
public sealed class TokenProvider(
    IEnumerable<ITokenSource> sources,
    ISecretMasker secretMasker,
    ILogger<TokenProvider> logger) : ITokenProvider
{
    /// <inheritdoc />
    public async Task<GitHubToken> GetTokenAsync(CancellationToken cancellationToken)
    {
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var value = await source.GetTokenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var token = value.Trim();
            secretMasker.Register(token);
            TokenProviderLog.TokenFound(logger, source.Name);
            return new GitHubToken(token, source.Name);
        }

        TokenProviderLog.AnonymousMode(logger);
        return GitHubToken.Anonymous;
    }
}
