using System.ComponentModel;
using Meziantou.Framework.Win32;

namespace GitHubBackup.Infrastructure.Authentication;

/// <summary>
/// Reads, writes and deletes a secret kept as a generic credential in Windows Credential Manager, and turns every
/// failure into an exception whose message names the credential and never contains the secret.
/// </summary>
/// <remarks>
/// The Credential Manager API is synchronous; its calls are local and short, so they run on the calling thread.
/// </remarks>
internal static class CredentialManagerSecret
{
    /// <summary>The user name stored with every credential; Credential Manager requires one.</summary>
    public const string UserName = "GitHubBackup";

    /// <summary>
    /// Reads the secret of the credential <paramref name="target"/>.
    /// </summary>
    /// <param name="target">The name of the credential.</param>
    /// <param name="createException">Creates the exception thrown for a failure from its message and cause.</param>
    /// <returns>The secret, or <see langword="null"/> when there is no such credential.</returns>
    public static string? Read(string target, Func<string, Exception, Exception> createException)
    {
        try
        {
            return CredentialManager.ReadCredential(target, CredentialType.Generic)?.Password;
        }
        catch (Win32Exception exception)
        {
            throw createException(
                $"Cannot read the credential '{target}' from Windows Credential Manager: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Saves <paramref name="secret"/> as the credential <paramref name="target"/>, replacing an earlier one.
    /// </summary>
    /// <param name="target">The name of the credential.</param>
    /// <param name="secret">The secret.</param>
    /// <param name="secretName">What the secret is, such as <c>token</c>, for the message about a rejected value.</param>
    /// <param name="createException">Creates the exception thrown for a failure from its message and cause.</param>
    public static void Write(
        string target, string secret, string secretName, Func<string, Exception, Exception> createException)
    {
        try
        {
            CredentialManager.WriteCredential(
                target, UserName, secret, CredentialPersistence.LocalMachine, CredentialType.Generic);
        }
        catch (Win32Exception exception)
        {
            throw createException(
                $"Cannot save the credential '{target}' to Windows Credential Manager: {exception.Message}", exception);
        }
        catch (ArgumentException exception)
        {
            // Credential Manager rejects a secret longer than its size limit (such as a pasted file); the message of
            // the library's exception is not shown because it could echo the rejected value.
            throw createException(
                $"Cannot save the credential '{target}' to Windows Credential Manager: the {secretName} is too long or invalid.",
                exception);
        }
    }

    /// <summary>
    /// Deletes the credential <paramref name="target"/>; a credential that does not exist is not an error.
    /// </summary>
    /// <param name="target">The name of the credential.</param>
    /// <param name="createException">Creates the exception thrown for a failure from its message and cause.</param>
    public static void Delete(string target, Func<string, Exception, Exception> createException)
    {
        try
        {
            CredentialManager.TryDeleteCredential(target, CredentialType.Generic);
        }
        catch (Win32Exception exception)
        {
            throw createException(
                $"Cannot delete the credential '{target}' from Windows Credential Manager: {exception.Message}", exception);
        }
    }
}
