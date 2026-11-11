using GitHubBackup.Core;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Infrastructure.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GitHubBackup.Infrastructure.Hosting;

/// <summary>
/// Creates the Generic Host builder shared by the console and the WPF executables.
/// </summary>
/// <remarks>
/// The host starts without the framework defaults so that only the documented sources are read, in this order of
/// increasing priority: <see cref="AppSettingsFileName"/>, user secrets (<see cref="Environments.Development"/> only),
/// environment variables with <see cref="EnvironmentVariablePrefix"/>, the <c>--Section:Key=value</c> arguments of
/// <see cref="GitHubBackupHostSettings.ConfigurationArguments"/>, and
/// <see cref="GitHubBackupHostSettings.BackupConfigPath"/>, which sets <see cref="BackupOptions.ConfigPath"/>.
/// Unprefixed variables and <c>appsettings.{Environment}.json</c> are not read.
/// </remarks>
public static class GitHubBackupHostBuilder
{
    /// <summary>The name of the required settings file in the folder of the executable.</summary>
    public const string AppSettingsFileName = "appsettings.json";

    /// <summary>The prefix of environment variables that override settings, for example <c>GitHubBackup__Backup__ShortHashLength</c>.</summary>
    public const string EnvironmentVariablePrefix = "GitHubBackup__";

    /// <summary>The user secrets identifier shared by both executables.</summary>
    public const string UserSecretsId = "GitHubBackup";

    /// <summary>The configuration key that <see cref="GitHubBackupHostSettings.BackupConfigPath"/> overrides.</summary>
    private const string _configPathKey = BackupOptions.SectionName + ":" + nameof(BackupOptions.ConfigPath);

    /// <summary>
    /// Creates a host builder with the configuration sources, the service provider checks, the services of
    /// <c>Core</c> and <c>Infrastructure</c> and the file logging registered; the caller adds its own services and
    /// builds the host.
    /// </summary>
    /// <param name="settings">The process the host is built for.</param>
    /// <returns>The host builder.</returns>
    /// <exception cref="ConfigurationFileException">
    /// <see cref="AppSettingsFileName"/> does not exist or is not valid JSON, or the user secrets file is not valid
    /// JSON.
    /// </exception>
    public static HostApplicationBuilder Create(GitHubBackupHostSettings settings)
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            Args = null,
            ContentRootPath = settings.ContentRootPath,
            EnvironmentName = settings.EnvironmentName,
        });

        var configuration = builder.Configuration;
        try
        {
            configuration.AddJsonFile(
                settings.ConfigurationFileProvider ?? builder.Environment.ContentRootFileProvider,
                AppSettingsFileName,
                optional: false,
                reloadOnChange: false);

            if (builder.Environment.IsDevelopment())
            {
                configuration.AddUserSecrets(UserSecretsId, reloadOnChange: false);
            }
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException)
        {
            throw new ConfigurationFileException(exception);
        }

        configuration.AddEnvironmentVariables(EnvironmentVariablePrefix);
        configuration.AddCommandLine([.. settings.ConfigurationArguments]);
        if (settings.BackupConfigPath is not null)
        {
            configuration.AddInMemoryCollection([new(_configPathKey, settings.BackupConfigPath)]);
        }

        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));

        builder.Services
            .AddGitHubBackupCore()
            .AddGitHubBackupInfrastructure(configuration)
            .AddGitHubBackupLogging(configuration);

        return builder;
    }
}
