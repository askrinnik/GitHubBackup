namespace GitHubBackup.Core.Authentication;

/// <summary>
/// The place where the application keeps the GitHub token the user saved.
/// </summary>
public interface ITokenStore
{
    /// <summary>
    /// Gets a description of where the token is kept, for messages to the user; it never contains the token.
    /// </summary>
    string Location { get; }

    /// <summary>
    /// Reads the saved token.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The token, or <see langword="null"/> when none is saved.</returns>
    /// <exception cref="TokenStoreException">The store cannot be read.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
    Task<string?> ReadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves <paramref name="token"/>, replacing a token saved earlier.
    /// </summary>
    /// <param name="token">The token to save.</param>
    /// <param name="cancellationToken">The token that cancels the write.</param>
    /// <returns>A task that completes when the token is saved.</returns>
    /// <exception cref="TokenStoreException">The store cannot be written.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
    Task WriteAsync(string token, CancellationToken cancellationToken);
}
