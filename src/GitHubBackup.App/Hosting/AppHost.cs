using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.Hosting;

namespace GitHubBackup.App.Hosting;

/// <summary>
/// Builds the host of the WPF application on the builder shared with the console application.
/// </summary>
internal static class AppHost
{
    /// <summary>
    /// Builds the host of the WPF application.
    /// </summary>
    /// <param name="settings">The process the host is built for.</param>
    /// <returns>The host, not yet started.</returns>
    /// <exception cref="ConfigurationFileException">The settings file is missing or is not valid JSON.</exception>
    /// <exception cref="Microsoft.Extensions.Options.OptionsValidationException">A logging setting is invalid.</exception>
    public static IHost BuildHost(GitHubBackupHostSettings settings)
    {
        var builder = GitHubBackupHostBuilder.Create(settings);
        builder.Services.AddGitHubBackupApp();
        return builder.Build();
    }
}
