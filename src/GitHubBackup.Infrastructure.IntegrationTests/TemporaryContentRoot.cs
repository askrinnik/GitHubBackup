using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.Infrastructure.Logging;
using Microsoft.Extensions.Hosting;

namespace GitHubBackup.Infrastructure.IntegrationTests;

/// <summary>
/// Creates a unique folder that serves as the content root of one test host and deletes it on dispose.
/// </summary>
internal sealed class TemporaryContentRoot : IDisposable
{
    /// <summary>
    /// Creates the folder with <paramref name="appSettings"/> as its <c>appsettings.json</c>.
    /// </summary>
    /// <param name="appSettings">The JSON text of the settings file.</param>
    public TemporaryContentRoot(string appSettings)
    {
        FolderPath = Path.Combine(Path.GetTempPath(), "GitHubBackup.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(FolderPath);
        File.WriteAllText(Path.Combine(FolderPath, GitHubBackupHostBuilder.AppSettingsFileName), appSettings);
    }

    /// <summary>Gets the absolute path of the folder.</summary>
    public string FolderPath { get; }

    /// <summary>Gets the absolute path of the log folder inside the content root.</summary>
    public string LogsPath => Path.Combine(FolderPath, LogFiles.FolderName);

    /// <summary>
    /// Builds a host over this content root, not yet started.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The host; the caller disposes it.</returns>
    public IHost BuildHost(params string[] args) =>
        GitHubBackupHostBuilder.Create(new()
        {
            Args = args,
            ContentRootPath = FolderPath,
            EnvironmentName = Environments.Production,
        }).Build();

    /// <summary>
    /// Deletes the folder and everything in it.
    /// </summary>
    public void Dispose() => Directory.Delete(FolderPath, recursive: true);
}
