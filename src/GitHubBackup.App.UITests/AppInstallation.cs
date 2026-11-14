using System.Reflection;
using System.Text.Json;
using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.TestUtilities;
using GitHubBackup.TestUtilities.Git;
using Microsoft.Extensions.Hosting;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Installs a copy of the WPF executable in a temporary folder, configured for a stubbed GitHub API and, when a
/// scenario runs git, for remotes in a local folder; deletes the folder on dispose.
/// </summary>
/// <remarks>
/// The folder is the content root of the executable, so <c>appsettings.json</c>, <c>user-settings.json</c>,
/// <c>backup-config.json</c>, the log, the run history and the default clones all stay inside it. The token comes from
/// <c>GitHub:Token</c> of the copied settings, so the application reads neither Windows Credential Manager nor the
/// GitHub CLI.
/// </remarks>
internal sealed class AppInstallation : IDisposable
{
    /// <summary>The token the settings of the installation configure.</summary>
    public const string Token = "ghp_UiTestTokenValue0123456789abcdefABCD";

    /// <summary>
    /// The environment variable that names a published application folder; when it is set, the installation copies
    /// that folder and starts its <c>GitHubBackup.exe</c> instead of the build output.
    /// </summary>
    public const string PublishedAppPathVariable = "PUBLISHED_APP_PATH";

    /// <summary>The name of the executable in the build output of the WPF project.</summary>
    private const string _buildExecutableName = "GitHubBackup.App.exe";

    /// <summary>The name of the executable in a published application folder.</summary>
    private const string _publishedExecutableName = "GitHubBackup.exe";

    /// <summary>The name of the executable in the installation folder.</summary>
    private readonly string _executableName;

    /// <summary>
    /// Copies the published application folder named by <see cref="PublishedAppPathVariable"/>, or the build output of
    /// the WPF project when the variable is not set, and writes valid settings.
    /// </summary>
    /// <param name="apiBaseUrl">The address of the GitHub API the application calls.</param>
    /// <param name="remotesRoot">
    /// The folder of the bare repositories that <c>https://github.com/</c> is redirected to, or
    /// <see langword="null"/> when the scenario runs no git command.
    /// </param>
    public AppInstallation(string apiBaseUrl, string? remotesRoot = null)
    {
        FolderPath = TemporaryFolder.Create();
        var publishedFolder = System.Environment.GetEnvironmentVariable(PublishedAppPathVariable);
        string source;
        if (string.IsNullOrEmpty(publishedFolder))
        {
            source = typeof(AppInstallation).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "GitHubBackupAppOutputPath")
                .Value!;
            _executableName = _buildExecutableName;
        }
        else
        {
            source = Path.GetFullPath(publishedFolder);
            _executableName = _publishedExecutableName;
        }

        BuildOutput.CopyTo(source, FolderPath, _executableName);
        WriteAppSettings(ValidAppSettings(apiBaseUrl));

        Dictionary<string, string> environment = new(StringComparer.OrdinalIgnoreCase)
        {
            // Production skips the user secrets of the developer, which could supply another token or API address.
            [GitHubBackupHostSettings.EnvironmentVariableName] = Environments.Production,
        };
        if (remotesRoot is not null)
        {
            foreach (var (name, value) in new GitRemoteRedirect(FolderPath, remotesRoot).EnvironmentVariables())
            {
                environment[name] = value;
            }
        }

        Environment = environment;
    }

    /// <summary>Gets the absolute path of the installation folder.</summary>
    public string FolderPath { get; }

    /// <summary>Gets the absolute path of the executable.</summary>
    public string ExecutablePath => Path.Combine(FolderPath, _executableName);

    /// <summary>Gets the environment variables the executable is started with.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; }

    /// <summary>Gets the absolute path of <c>backup-config.json</c>, which the application reads by default.</summary>
    public string BackupConfigPath => Path.Combine(FolderPath, "backup-config.json");

    /// <summary>Gets the absolute path of <c>user-settings.json</c> in the content root.</summary>
    public string UserSettingsPath => Path.Combine(FolderPath, GitHubBackupHostBuilder.UserSettingsFileName);

    /// <summary>Gets the absolute path of the archive root that scenarios configure.</summary>
    public string ArchivesPath => Path.Combine(FolderPath, "archives");

    /// <summary>
    /// Returns a builder of <c>backup-config.json</c> whose archive root is <see cref="ArchivesPath"/>.
    /// </summary>
    /// <returns>The builder.</returns>
    public BackupConfigJson NewBackupConfig() => new(ArchivesPath);

    /// <summary>
    /// Replaces <c>appsettings.json</c> of the installation.
    /// </summary>
    /// <param name="json">The content of the file.</param>
    public void WriteAppSettings(string json) =>
        File.WriteAllText(Path.Combine(FolderPath, GitHubBackupHostBuilder.AppSettingsFileName), json);

    /// <summary>
    /// Reads <c>backup-config.json</c> of the installation.
    /// </summary>
    /// <returns>The text of the file.</returns>
    public string ReadBackupConfig() => File.ReadAllText(BackupConfigPath);

    /// <summary>
    /// Returns every archive in the archive folder of one repository below <see cref="ArchivesPath"/>.
    /// </summary>
    /// <param name="owner">The owner login.</param>
    /// <param name="name">The repository name.</param>
    /// <returns>The absolute paths, or none when the folder does not exist.</returns>
    public string[] FindArchives(string owner, string name)
    {
        var folder = Path.Combine(ArchivesPath, owner, name);
        return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.zip") : [];
    }

    /// <summary>
    /// Deletes the installation folder and everything the application created in it.
    /// </summary>
    public void Dispose() => TemporaryFolder.Delete(FolderPath);

    /// <summary>
    /// Builds valid settings with <see cref="Token"/>, git from <c>PATH</c> and 7-Zip in its default location.
    /// </summary>
    /// <param name="apiBaseUrl">The address of the GitHub API.</param>
    /// <returns>The content of <c>appsettings.json</c>.</returns>
    private static string ValidAppSettings(string apiBaseUrl) =>
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
