using GitHubBackup.Core.Telemetry;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Stands in for the <see cref="IOtlpHeadersStore"/> of a host built without a Windows Credential Manager target:
/// nothing is saved, so there is nothing to read or delete, and a write fails.
/// </summary>
internal sealed class UnavailableOtlpHeadersStore : IOtlpHeadersStore
{
    /// <inheritdoc />
    public string Location => "no OTLP header store";

    /// <inheritdoc />
    public Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task WriteAsync(string headers, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new OtlpHeadersStoreException(
            $"Cannot save {OpenTelemetryOptions.HeadersKey}: this host has no Windows Credential Manager target.");
    }

    /// <inheritdoc />
    public Task DeleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
