using GitHubBackup.Core.Authentication;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.Authentication;

/// <summary>
/// Reads the token that <c>set-token</c> saved in the <see cref="ITokenStore"/>.
/// </summary>
/// <remarks>
/// A store that cannot be read does not stop the run: the failure is logged and the chain moves on to the next source.
/// </remarks>
/// <param name="store">The store that holds the saved token.</param>
/// <param name="logger">Logs a failure to read the store.</param>
internal sealed class CredentialManagerTokenSource(ITokenStore store, ILogger<CredentialManagerTokenSource> logger)
    : ITokenSource
{
    /// <inheritdoc />
    public string Name => store.Location;

    /// <inheritdoc />
    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await store.ReadAsync(cancellationToken);
        }
        catch (TokenStoreException exception)
        {
            TokenSourceLog.StoreReadFailed(logger, Name, exception.Message);
            return null;
        }
    }
}
