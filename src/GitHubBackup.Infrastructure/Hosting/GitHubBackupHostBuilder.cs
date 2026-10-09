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
/// environment variables with <see cref="EnvironmentVariablePrefix"/>, <c>--Section:Key=value</c> arguments, and
/// <see cref="ConfigOption"/>, which sets <see cref="BackupOptions.ConfigPath"/>.
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

    /// <summary>
    /// The option that names <c>backup-config.json</c>, as <c>--config &lt;path&gt;</c> or <c>--config=&lt;path&gt;</c>;
    /// a relative path is resolved against the current directory, where the user typed it.
    /// </summary>
    public const string ConfigOption = "--config";

    /// <summary>The configuration key that <see cref="ConfigOption"/> overrides.</summary>
    private const string _configPathKey = BackupOptions.SectionName + ":" + nameof(BackupOptions.ConfigPath);

    /// <summary>
    /// Creates a host builder with the configuration sources, the service provider checks, the services of
    /// <c>Core</c> and <c>Infrastructure</c> and the file logging registered; the caller adds its own services and
    /// builds the host.
    /// </summary>
    /// <param name="settings">The process the host is built for.</param>
    /// <returns>The host builder.</returns>
    /// <exception cref="FileNotFoundException"><see cref="AppSettingsFileName"/> does not exist.</exception>
    /// <exception cref="InvalidDataException">
    /// <see cref="AppSettingsFileName"/> is not valid JSON, or <see cref="ConfigOption"/> has no value.
    /// </exception>
    public static HostApplicationBuilder Create(GitHubBackupHostSettings settings)
    {
        var configPath = FindConfigPath(settings.Args);

        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            Args = null,
            ContentRootPath = settings.ContentRootPath,
            EnvironmentName = settings.EnvironmentName,
        });

        var configuration = builder.Configuration;
        configuration.AddJsonFile(
            settings.ConfigurationFileProvider ?? builder.Environment.ContentRootFileProvider,
            AppSettingsFileName,
            optional: false,
            reloadOnChange: false);

        if (builder.Environment.IsDevelopment())
        {
            configuration.AddUserSecrets(UserSecretsId, reloadOnChange: false);
        }

        configuration.AddEnvironmentVariables(EnvironmentVariablePrefix);
        configuration.AddCommandLine([.. settings.Args.Where(IsConfigurationArgument)]);
        if (configPath is not null)
        {
            configuration.AddInMemoryCollection([new(_configPathKey, configPath)]);
        }

        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));

        builder.Services
            .AddGitHubBackupCore(configuration)
            .AddGitHubBackupInfrastructure(configuration)
            .AddGitHubBackupLogging(configuration);

        return builder;
    }

    /// <summary>
    /// Returns the full path given by the last <see cref="ConfigOption"/> in <paramref name="args"/>.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The full path, or <see langword="null"/> when the option is absent.</returns>
    /// <exception cref="InvalidDataException">The option has no value.</exception>
    private static string? FindConfigPath(IReadOnlyList<string> args)
    {
        string? value = null;
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (argument == ConfigOption)
            {
                // An option in the value position means the path was left out, not that the path starts with "--".
                var hasValue = index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal);
                value = hasValue ? args[++index] : string.Empty;
            }
            else if (argument.StartsWith(ConfigOption + "=", StringComparison.Ordinal))
            {
                value = argument[(ConfigOption.Length + 1)..];
            }
            else
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException($"The {ConfigOption} option requires the path of the backup configuration file.");
            }
        }

        return value is null ? null : Path.GetFullPath(value);
    }

    /// <summary>
    /// Returns whether <paramref name="argument"/> has the form <c>--Section:Key=value</c>.
    /// </summary>
    /// <remarks>
    /// Command options such as <c>--silent</c> or <c>--account name</c> are left out, so they never become
    /// configuration keys or swallow the next argument as a value.
    /// </remarks>
    /// <param name="argument">One command-line argument.</param>
    /// <returns><see langword="true"/> when the argument sets a configuration key.</returns>
    private static bool IsConfigurationArgument(string argument)
    {
        if (!argument.StartsWith("--", StringComparison.Ordinal))
        {
            return false;
        }

        var separator = argument.IndexOf('=', StringComparison.Ordinal);
        return separator > 2 && argument.AsSpan(2, separator - 2).Contains(':');
    }
}
