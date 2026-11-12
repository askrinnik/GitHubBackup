using System.ComponentModel;
using GitHubBackup.Core.Authentication;
using Meziantou.Framework.Win32;

namespace GitHubBackup.Infrastructure.Authentication;

/// <summary>
/// Keeps the token as a generic credential in Windows Credential Manager.
/// </summary>
/// <remarks>
/// The credential persists on the local machine for the current Windows user, so a token saved once is found by every
/// later run under the same account, scheduled ones included. The Credential Manager API is synchronous; its calls are
/// local and short, so they run on the calling thread.
/// </remarks>
/// <param name="target">The name of the credential: <see cref="DefaultTarget"/>, or a unique name in tests.</param>
internal sealed class CredentialManagerTokenStore(string target) : ITokenStore
{
    /// <summary>The name of the credential that holds the token of the application.</summary>
    public const string DefaultTarget = "GitHubBackup:https://github.com";

    /// <summary>The user name stored with the credential; Credential Manager requires one.</summary>
    internal const string UserName = "GitHubBackup";

    /// <summary>Gets the name of the credential.</summary>
    public string Target => target;

    /// <inheritdoc />
    public string Location => $"Windows Credential Manager ({target})";

    /// <inheritdoc />
    public Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var credential = CredentialManager.ReadCredential(target, CredentialType.Generic);
            return Task.FromResult(credential?.Password);
        }
        catch (Win32Exception exception)
        {
            throw new TokenStoreException(
                $"Cannot read the credential '{target}' from Windows Credential Manager: {exception.Message}", exception);
        }
    }

    /// <inheritdoc />
    public Task WriteAsync(string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            CredentialManager.WriteCredential(
                target, UserName, token, CredentialPersistence.LocalMachine, CredentialType.Generic);
            return Task.CompletedTask;
        }
        catch (Win32Exception exception)
        {
            throw new TokenStoreException(
                $"Cannot save the credential '{target}' to Windows Credential Manager: {exception.Message}", exception);
        }
        catch (ArgumentException exception)
        {
            // Credential Manager rejects a secret longer than its size limit (such as a pasted file); the message of
            // the library's exception is not shown because it could echo the rejected value.
            throw new TokenStoreException(
                $"Cannot save the credential '{target}' to Windows Credential Manager: the token is too long or invalid.",
                exception);
        }
    }
}
