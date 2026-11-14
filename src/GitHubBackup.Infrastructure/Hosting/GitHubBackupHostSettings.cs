using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GitHubBackup.Infrastructure.Hosting;

/// <summary>
/// Describes the process a host is built for: its configuration arguments, its folder and its environment.
/// </summary>
public sealed record GitHubBackupHostSettings
{
    /// <summary>The environment variable that names the host environment.</summary>
    public const string EnvironmentVariableName = "DOTNET_ENVIRONMENT";

    /// <summary>
    /// Gets the configuration keys given on the command line, each in the form <c>--Section:Key=value</c>; the host
    /// reads them as configuration and does not interpret any other argument.
    /// </summary>
    public IReadOnlyList<string> ConfigurationArguments { get; init; } = [];

    /// <summary>
    /// Gets the absolute path of <c>backup-config.json</c> that overrides <c>Backup:ConfigPath</c>, or
    /// <see langword="null"/> to keep the configured path.
    /// </summary>
    public string? BackupConfigPath { get; init; }

    /// <summary>
    /// Gets the absolute path of an existing folder that holds the executable; relative paths in the configuration
    /// are resolved against it.
    /// </summary>
    public required string ContentRootPath { get; init; }

    /// <summary>Gets the host environment name, for example <see cref="Environments.Production"/>.</summary>
    public required string EnvironmentName { get; init; }

    /// <summary>
    /// Gets the file provider that supplies <c>appsettings.json</c> and <c>user-settings.json</c>; <see langword="null"/>
    /// reads them from
    /// <see cref="ContentRootPath"/>.
    /// </summary>
    public IFileProvider? ConfigurationFileProvider { get; init; }

    /// <summary>
    /// Creates the settings of the current process: the folder of the executable and the environment named by
    /// <see cref="EnvironmentVariableName"/>, <see cref="Environments.Production"/> when it is not set.
    /// </summary>
    /// <returns>The settings of the current process.</returns>
    public static GitHubBackupHostSettings ForCurrentProcess()
    {
        var environmentName = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        return new()
        {
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = string.IsNullOrWhiteSpace(environmentName) ? Environments.Production : environmentName,
        };
    }
}
