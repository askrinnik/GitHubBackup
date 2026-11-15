using GitHubBackup.Core;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.Tools;
using GitHubBackup.Infrastructure.Logging;
using GitHubBackup.Infrastructure.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GitHubBackup.Infrastructure.Hosting;

/// <summary>
/// Creates the Generic Host builder shared by the console and the WPF executables.
/// </summary>
/// <remarks>
/// The host starts without the framework defaults so that only the documented sources are read, in this order of
/// increasing priority: <see cref="AppSettingsFileName"/>, the optional <see cref="UserSettingsFileName"/>, user secrets (<see cref="Environments.Development"/> only),
/// environment variables with <see cref="EnvironmentVariablePrefix"/>, the <c>--Section:Key=value</c> arguments of
/// <see cref="GitHubBackupHostSettings.ConfigurationArguments"/>, and
/// <see cref="GitHubBackupHostSettings.BackupConfigPath"/>, which sets <see cref="BackupOptions.ConfigPath"/>.
/// Unprefixed variables and <c>appsettings.{Environment}.json</c> are not read.
/// </remarks>
public static class GitHubBackupHostBuilder
{
    /// <summary>The name of the required settings file in the folder of the executable.</summary>
    public const string AppSettingsFileName = "appsettings.json";

    /// <summary>
    /// The name of the optional settings file in the folder of the executable that holds the tool paths written by the
    /// settings editor.
    /// </summary>
    public const string UserSettingsFileName = "user-settings.json";

    /// <summary>The prefix of environment variables that override settings, for example <c>GitHubBackup__Backup__ShortHashLength</c>.</summary>
    public const string EnvironmentVariablePrefix = "GitHubBackup__";

    /// <summary>The user secrets identifier shared by both executables.</summary>
    public const string UserSecretsId = "GitHubBackup";

    /// <summary>The configuration key that <see cref="GitHubBackupHostSettings.BackupConfigPath"/> overrides.</summary>
    private const string _configPathKey = BackupOptions.SectionName + ":" + nameof(BackupOptions.ConfigPath);

    /// <summary>
    /// Creates a host builder with the configuration sources, the service provider checks, the services of
    /// <c>Core</c> and <c>Infrastructure</c>, the file logging and, when enabled, the OpenTelemetry export
    /// registered; the caller adds its own services and builds the host.
    /// </summary>
    /// <param name="settings">The process the host is built for.</param>
    /// <returns>The host builder.</returns>
    /// <exception cref="ConfigurationFileException">
    /// <see cref="AppSettingsFileName"/> does not exist or is not valid JSON, or <see cref="UserSettingsFileName"/> or
    /// the user secrets file is not valid JSON.
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
        var fileProvider = settings.ConfigurationFileProvider ?? builder.Environment.ContentRootFileProvider;
        try
        {
            configuration.AddJsonFile(fileProvider, AppSettingsFileName, optional: false, reloadOnChange: false);
            configuration.AddInMemoryCollection(ReadUserSettings(fileProvider));

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
            .AddGitHubBackupLogging(configuration)
            .AddGitHubBackupTelemetry(configuration);

        return builder;
    }

    /// <summary>
    /// Reads the tool paths from <see cref="UserSettingsFileName"/>, if the file exists.
    /// </summary>
    /// <remarks>
    /// Only <see cref="ToolPaths.GitPathKey"/> and <see cref="ToolPaths.SevenZipPathKey"/> are taken: a key such as
    /// <c>GitHub:ApiBaseUrl</c> added by hand to a file the application writes must not redirect the token to another
    /// host. The <c>Ui</c> section stays out of the configuration as well: only the WPF application reads it, from the
    /// file itself, and an invalid value there must not fail the start of either host.
    /// </remarks>
    /// <param name="fileProvider">The provider of the folder of the executable.</param>
    /// <returns>The tool path keys found in the file with their values.</returns>
    /// <exception cref="InvalidDataException">The file is not valid JSON.</exception>
    private static List<KeyValuePair<string, string?>> ReadUserSettings(IFileProvider fileProvider)
    {
        var userSettings = new ConfigurationBuilder()
            .AddJsonFile(fileProvider, UserSettingsFileName, optional: true, reloadOnChange: false)
            .Build();
        return
        [
            .. userSettings.AsEnumerable().Where(entry =>
                string.Equals(entry.Key, ToolPaths.GitPathKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Key, ToolPaths.SevenZipPathKey, StringComparison.OrdinalIgnoreCase)),
        ];
    }
}
