using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.Hosting;

namespace GitHubBackup.App.Tests;

/// <summary>
/// Provides the settings file and the host settings that the host tests of the WPF application build on.
/// </summary>
internal static class AppTestSettings
{
    /// <summary>
    /// A valid settings file with every section of the shipped <c>appsettings.json</c>.
    /// </summary>
    /// <remarks>
    /// It sets <c>GitHub:Token</c> so that the token chain of the real host stops at its first source and never reads
    /// the Credential Manager of the machine or starts the GitHub CLI.
    /// </remarks>
    public const string ValidAppSettings =
        """
        {
          "GitHub": { "ApiBaseUrl": "https://api.github.com", "Token": "ghp_AppTestTokenValue0123456789abcdefABC" },
          "Tools": { "GitPath": null, "SevenZipPath": "C:\\Program Files\\7-Zip\\7z.exe" },
          "Backup": { "ConfigPath": "backup-config.json", "ShortHashLength": 10, "VerifyArchive": true },
          "History": { "DatabasePath": "data\\history.db" },
          "OpenTelemetry": { "Enabled": false, "OtlpEndpoint": "http://localhost:4317" },
          "Serilog": { "MinimumLevel": { "Default": "Information" } }
        }
        """;

    /// <summary>
    /// Creates host settings over <paramref name="contentRootPath"/> in the <see cref="Environments.Production"/>
    /// environment.
    /// </summary>
    /// <param name="contentRootPath">The existing folder that the host uses as its content root.</param>
    /// <param name="configurationArguments">The configuration keys, each in the form <c>--Section:Key=value</c>.</param>
    /// <returns>The host settings.</returns>
    public static GitHubBackupHostSettings Create(string contentRootPath, params string[] configurationArguments) => new()
    {
        ConfigurationArguments = configurationArguments,
        ContentRootPath = contentRootPath,
        EnvironmentName = Environments.Production,
    };
}
