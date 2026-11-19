using GitHubBackup.Core.Authentication;

namespace GitHubBackup.Infrastructure.Authentication;

/// <summary>
/// Keeps the token as a generic credential in Windows Credential Manager.
/// </summary>
/// <remarks>
/// The credential persists on the local machine for the current Windows user, so a token saved once is found by every
/// later run under the same account, scheduled ones included.
/// </remarks>
/// <param name="target">The name of the credential: <see cref="DefaultTarget"/>, or a unique name in tests.</param>
internal sealed class CredentialManagerTokenStore(string target) : ITokenStore
{
    /// <summary>The name of the credential that holds the token of the application.</summary>
    public const string DefaultTarget = "GitHubBackup:https://github.com";

    /// <summary>The user name stored with the credential; Credential Manager requires one.</summary>
    internal const string UserName = CredentialManagerSecret.UserName;

    /// <summary>Gets the name of the credential.</summary>
    public string Target => target;

    /// <inheritdoc />
    public string Location => $"Windows Credential Manager ({target})";

    /// <inheritdoc />
    public Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CredentialManagerSecret.Read(target, CreateException));
    }

    /// <inheritdoc />
    public Task WriteAsync(string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CredentialManagerSecret.Write(target, token, "token", CreateException);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates the exception the store throws for a failure of Credential Manager.
    /// </summary>
    /// <param name="message">The message, which never contains the token.</param>
    /// <param name="innerException">The failure of Credential Manager.</param>
    /// <returns>The exception.</returns>
    private static TokenStoreException CreateException(string message, Exception innerException) =>
        new(message, innerException);
}
