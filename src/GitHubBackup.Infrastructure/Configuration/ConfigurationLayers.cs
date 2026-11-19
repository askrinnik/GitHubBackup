using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.Configuration;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Tells the values of <see cref="OpenTelemetryOptions.HeadersKey"/> in
/// <see cref="GitHubBackupHostBuilder.AppSettingsFileName"/> and in Windows Credential Manager apart from the value that
/// takes effect.
/// </summary>
/// <remarks>
/// The providers are asked one by one, never through a debug view of the configuration, because a debug view prints
/// every value, secrets included.
/// </remarks>
internal static class ConfigurationLayers
{
    /// <summary>
    /// Returns the headers of the application settings file and of Windows Credential Manager, and whether a provider
    /// added after both sets the key too.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>
    /// The value of the file and the value of Credential Manager, each <see langword="null"/> when that source does
    /// not set the key, and whether a later provider sets it, also to an empty value. A configuration that is not an
    /// <see cref="IConfigurationRoot"/>, or has neither provider, gives no values and counts any value of the key as
    /// overriding, so that a value from another source is never taken for a saved one.
    /// </returns>
    public static (string? FileValue, string? CredentialValue, bool Overridden) FindHeadersLayers(
        IConfiguration configuration)
    {
        const string key = OpenTelemetryOptions.HeadersKey;
        if (configuration is not IConfigurationRoot root)
        {
            return (null, null, configuration[key] is not null);
        }

        var providers = root.Providers.ToList();
        var fileIndex = providers.FindIndex(provider => provider is FileConfigurationProvider file
            && string.Equals(file.Source.Path, GitHubBackupHostBuilder.AppSettingsFileName, StringComparison.OrdinalIgnoreCase));
        var credentialIndex = providers.FindIndex(provider => provider is CredentialManagerConfigurationProvider);
        var lastIndex = Math.Max(fileIndex, credentialIndex);
        if (lastIndex < 0)
        {
            return (null, null, configuration[key] is not null);
        }

        var fileValue = fileIndex >= 0 && providers[fileIndex].TryGet(key, out var file) ? file : null;
        var credentialValue = credentialIndex >= 0 && providers[credentialIndex].TryGet(key, out var credential)
            ? credential
            : null;
        var overridden = providers.Skip(lastIndex + 1).Any(provider => provider.TryGet(key, out _));
        return (fileValue, credentialValue, overridden);
    }
}
