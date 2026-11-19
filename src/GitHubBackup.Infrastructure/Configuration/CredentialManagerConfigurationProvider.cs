using GitHubBackup.Core.Telemetry;
using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.Configuration;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Provides <see cref="OpenTelemetryOptions.HeadersKey"/> from the headers saved in Windows Credential Manager and no
/// other key.
/// </summary>
/// <remarks>
/// No saved headers, or a value that is empty or white space, gives no key. A store that cannot be read does not stop
/// the start: the provider gives no key and adds the failure, which never contains the value, to the warnings.
/// </remarks>
/// <param name="readHeaders">Reads the saved headers; throws <see cref="OtlpHeadersStoreException"/> on a failure.</param>
/// <param name="warnings">Receives the failure to read the store.</param>
internal sealed class CredentialManagerConfigurationProvider(Func<string?> readHeaders, ConfigurationWarnings warnings)
    : ConfigurationProvider
{
    /// <inheritdoc />
    public override void Load()
    {
        Data.Clear();
        string? headers;
        try
        {
            headers = readHeaders();
        }
        catch (OtlpHeadersStoreException exception)
        {
            warnings.Add($"{OpenTelemetryOptions.HeadersKey} is not read from Windows Credential Manager. {exception.Message}");
            return;
        }

        if (!string.IsNullOrWhiteSpace(headers))
        {
            Data[OpenTelemetryOptions.HeadersKey] = headers;
        }
    }
}
