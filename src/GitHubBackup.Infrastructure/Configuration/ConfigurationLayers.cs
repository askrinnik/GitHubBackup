using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.Configuration;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Tells the value of a key in <see cref="GitHubBackupHostBuilder.AppSettingsFileName"/> apart from the value that
/// takes effect.
/// </summary>
/// <remarks>
/// The providers are asked one by one, never through a debug view of the configuration, because a debug view prints
/// every value, secrets included.
/// </remarks>
internal static class ConfigurationLayers
{
    /// <summary>
    /// Returns the value of <paramref name="key"/> in the application settings file and whether a provider added
    /// after the file sets the key too.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="key">The configuration key.</param>
    /// <returns>
    /// The value of the file, <see langword="null"/> when the file does not set the key, and whether a later provider
    /// sets it, also to an empty value. A configuration that is not an <see cref="IConfigurationRoot"/>, or has no
    /// provider for the file, gives no file value and counts any value of the key as overriding, so that a value from
    /// another source is never taken for the file's.
    /// </returns>
    public static (string? FileValue, bool Overridden) FindAppSettingsValue(IConfiguration configuration, string key)
    {
        if (configuration is not IConfigurationRoot root)
        {
            return (null, configuration[key] is not null);
        }

        var providers = root.Providers.ToList();
        var fileIndex = providers.FindIndex(provider => provider is FileConfigurationProvider file
            && string.Equals(file.Source.Path, GitHubBackupHostBuilder.AppSettingsFileName, StringComparison.OrdinalIgnoreCase));
        if (fileIndex < 0)
        {
            return (null, configuration[key] is not null);
        }

        var fileValue = providers[fileIndex].TryGet(key, out var value) ? value : null;
        var overridden = providers.Skip(fileIndex + 1).Any(provider => provider.TryGet(key, out _));
        return (fileValue, overridden);
    }
}
