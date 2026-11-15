using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace GitHubBackup.Infrastructure.Hosting;

/// <summary>
/// Creates the <see cref="ISecretMasker"/> of a host, which knows the secrets the configuration sets: the token and
/// the OTLP headers.
/// </summary>
public static class HostSecretMasker
{
    /// <summary>
    /// Creates a masker with the value of <see cref="GitHubOptions.TokenKey"/> and the value of
    /// <see cref="OpenTelemetryOptions.HeadersKey"/> registered when the configuration sets them to something other
    /// than empty or white space.
    /// </summary>
    /// <remarks>
    /// The header list is registered as a whole and each header value on its own, also when the list is malformed or
    /// telemetry is disabled. A value is registered in its percent-decoded form too, because the OTLP exporter decodes
    /// it before sending. Header names are not secrets and stay readable.
    /// </remarks>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The masker.</returns>
    public static ISecretMasker Create(IConfiguration configuration)
    {
        var masker = new SecretMasker();
        RegisterIfSet(masker, configuration[GitHubOptions.TokenKey]);

        var headers = configuration[OpenTelemetryOptions.HeadersKey];
        if (!string.IsNullOrWhiteSpace(headers))
        {
            masker.Register(headers);
            foreach (var value in OtlpHeaders.SegmentValues(headers))
            {
                masker.Register(value);
                RegisterIfSet(masker, Uri.UnescapeDataString(value));
            }
        }

        return masker;
    }

    /// <summary>
    /// Registers <paramref name="secret"/> with <paramref name="masker"/> unless it is <see langword="null"/>, empty
    /// or white space.
    /// </summary>
    /// <param name="masker">The masker to extend.</param>
    /// <param name="secret">The candidate secret.</param>
    private static void RegisterIfSet(SecretMasker masker, string? secret)
    {
        if (!string.IsNullOrWhiteSpace(secret))
        {
            masker.Register(secret);
        }
    }
}
