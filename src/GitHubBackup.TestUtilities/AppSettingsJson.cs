using System.Text.Json;

namespace GitHubBackup.TestUtilities;

/// <summary>
/// Builds the <c>appsettings.json</c> of a temporary installation of an executable that calls a stubbed GitHub API.
/// </summary>
public static class AppSettingsJson
{
    /// <summary>
    /// The token the settings configure. It has the form of a classic personal access token but grants nothing, so a
    /// test or the test bench never holds a real secret.
    /// </summary>
    public const string Token = "ghp_UiTestTokenValue0123456789abcdefABCD";

    /// <summary>
    /// Builds valid settings with <see cref="Token"/>, git from <c>PATH</c> and 7-Zip in its default location.
    /// </summary>
    /// <param name="apiBaseUrl">The address of the GitHub API.</param>
    /// <returns>The content of <c>appsettings.json</c>.</returns>
    public static string Valid(string apiBaseUrl) =>
        $$"""
        {
          "GitHub": { "ApiBaseUrl": {{JsonSerializer.Serialize(apiBaseUrl)}}, "Token": "{{Token}}" },
          "Tools": { "GitPath": null, "SevenZipPath": "C:\\Program Files\\7-Zip\\7z.exe" },
          "Backup": { "ConfigPath": "backup-config.json", "ShortHashLength": 10, "VerifyArchive": true },
          "History": { "DatabasePath": "data\\history.db" },
          "OpenTelemetry": { "Enabled": false, "OtlpEndpoint": "http://localhost:4317" },
          "Serilog": { "MinimumLevel": { "Default": "Information" } }
        }
        """;
}
