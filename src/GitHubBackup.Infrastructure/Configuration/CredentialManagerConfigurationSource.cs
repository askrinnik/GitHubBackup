using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.Configuration;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Adds the OTLP headers saved in Windows Credential Manager to the configuration as
/// <see cref="OpenTelemetryOptions.HeadersKey"/>.
/// </summary>
/// <param name="readHeaders">
/// Reads the saved headers, <see langword="null"/> when none are saved; throws
/// <see cref="Core.Telemetry.OtlpHeadersStoreException"/> when the store cannot be read.
/// </param>
/// <param name="warnings">Receives the failure to read the store, which the host logs when it starts.</param>
internal sealed class CredentialManagerConfigurationSource(Func<string?> readHeaders, ConfigurationWarnings warnings)
    : IConfigurationSource
{
    /// <summary>
    /// Creates the source over the credential <paramref name="target"/>.
    /// </summary>
    /// <param name="target">The name of the credential that holds the headers.</param>
    /// <param name="warnings">Receives the failure to read the credential.</param>
    public CredentialManagerConfigurationSource(string target, ConfigurationWarnings warnings)
        : this(new CredentialManagerOtlpHeadersStore(target).Read, warnings)
    {
    }

    /// <inheritdoc />
    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new CredentialManagerConfigurationProvider(readHeaders, warnings);
}
