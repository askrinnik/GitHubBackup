using GitHubBackup.Core.Telemetry;
using GitHubBackup.Infrastructure.Authentication;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Keeps the OTLP headers as a generic credential in Windows Credential Manager, where
/// <see cref="CredentialManagerConfigurationSource"/> reads them at start-up.
/// </summary>
/// <param name="target">The name of the credential: <see cref="DefaultTarget"/>, or a unique name in tests.</param>
internal sealed class CredentialManagerOtlpHeadersStore(string target) : IOtlpHeadersStore
{
    /// <summary>The name of the credential that holds the OTLP headers of the application.</summary>
    public const string DefaultTarget = "GitHubBackup:OpenTelemetry:Headers";

    /// <summary>Gets the name of the credential.</summary>
    public string Target => target;

    /// <inheritdoc />
    public string Location => $"Windows Credential Manager ({target})";

    /// <inheritdoc />
    public Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Read());
    }

    /// <inheritdoc />
    public Task WriteAsync(string headers, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CredentialManagerSecret.Write(target, headers, "header list", CreateException);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CredentialManagerSecret.Delete(target, CreateException);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads the saved headers on the calling thread, for the configuration provider, which loads synchronously.
    /// </summary>
    /// <returns>The headers, or <see langword="null"/> when none are saved.</returns>
    /// <exception cref="OtlpHeadersStoreException">Credential Manager cannot be read.</exception>
    public string? Read() => CredentialManagerSecret.Read(target, CreateException);

    /// <summary>
    /// Creates the exception the store throws for a failure of Credential Manager.
    /// </summary>
    /// <param name="message">The message, which never contains the headers.</param>
    /// <param name="innerException">The failure of Credential Manager.</param>
    /// <returns>The exception.</returns>
    private static OtlpHeadersStoreException CreateException(string message, Exception innerException) =>
        new(message, innerException);
}
