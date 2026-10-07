using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GitHubBackup.Infrastructure.Hosting;

/// <summary>
/// Describes the process a host is built for: its arguments, its folder and its environment.
/// </summary>
public sealed record GitHubBackupHostSettings
{
    /// <summary>The environment variable that names the host environment.</summary>
    public const string EnvironmentVariableName = "DOTNET_ENVIRONMENT";

    /// <summary>Gets the command-line arguments of the process.</summary>
    public required IReadOnlyList<string> Args { get; init; }

    /// <summary>
    /// Gets the absolute path of an existing folder that holds the executable; relative paths in the configuration
    /// are resolved against it.
    /// </summary>
    public required string ContentRootPath { get; init; }

    /// <summary>Gets the host environment name, for example <see cref="Environments.Production"/>.</summary>
    public required string EnvironmentName { get; init; }

    /// <summary>
    /// Gets the file provider that supplies <c>appsettings.json</c>; <see langword="null"/> reads it from
    /// <see cref="ContentRootPath"/>.
    /// </summary>
    public IFileProvider? ConfigurationFileProvider { get; init; }

    /// <summary>
    /// Creates the settings of the current process: the folder of the executable and the environment named by
    /// <see cref="EnvironmentVariableName"/>, <see cref="Environments.Production"/> when it is not set.
    /// </summary>
    /// <param name="args">The command-line arguments of the process.</param>
    /// <returns>The settings of the current process.</returns>
    public static GitHubBackupHostSettings ForCurrentProcess(IReadOnlyList<string> args)
    {
        var environmentName = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        return new()
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = string.IsNullOrWhiteSpace(environmentName) ? Environments.Production : environmentName,
        };
    }
}
