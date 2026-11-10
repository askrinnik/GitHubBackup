using System.Text.Json;
using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.Infrastructure.Logging;
using Microsoft.Extensions.Hosting;

namespace GitHubBackup.TestUtilities;

/// <summary>
/// Creates a unique folder under the system temp folder that serves as the content root of one test host, so that
/// the files a run writes never land in the test output folder, and deletes it on dispose.
/// </summary>
public sealed class TemporaryContentRoot : IDisposable
{
    /// <summary>
    /// Creates the folder, with <paramref name="appSettings"/> as its <c>appsettings.json</c> when given.
    /// </summary>
    /// <param name="appSettings">The JSON text of the settings file, or <see langword="null"/> for an empty folder.</param>
    public TemporaryContentRoot(string? appSettings = null)
    {
        FolderPath = TemporaryFolder.Create();
        if (appSettings is not null)
        {
            File.WriteAllText(Path.Combine(FolderPath, GitHubBackupHostBuilder.AppSettingsFileName), appSettings);
        }
    }

    /// <summary>Gets the absolute path of the folder.</summary>
    public string FolderPath { get; }

    /// <summary>Gets the absolute path of the log folder inside the content root.</summary>
    public string LogsPath => Path.Combine(FolderPath, LogFiles.FolderName);

    /// <summary>
    /// Builds a host over this content root in the <see cref="Environments.Production"/> environment, not yet started.
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
    /// Returns the path of the only log file with <paramref name="extension"/>.
    /// </summary>
    /// <param name="extension">The extension of the file, <see cref="LogFiles.TextExtension"/> or
    /// <see cref="LogFiles.JsonExtension"/>.</param>
    /// <returns>The path.</returns>
    /// <exception cref="InvalidOperationException">The log folder holds no such file or more than one.</exception>
    public string GetLogFilePath(string extension) =>
        Directory.GetFiles(LogsPath, LogFiles.FileNamePrefix + "*" + extension).Single();

    /// <summary>
    /// Reads the text log.
    /// </summary>
    /// <returns>The content of the file.</returns>
    public string ReadTextLog() => File.ReadAllText(GetLogFilePath(LogFiles.TextExtension));

    /// <summary>
    /// Reads the entries of the structured log.
    /// </summary>
    /// <returns>One element per entry, independent of the parsed documents.</returns>
    public JsonElement[] ReadJsonLogEntries() =>
        [.. File.ReadAllLines(GetLogFilePath(LogFiles.JsonExtension)).Select(ParseEntry)];

    /// <summary>
    /// Deletes the folder and everything in it.
    /// </summary>
    public void Dispose() => TemporaryFolder.Delete(FolderPath);

    /// <summary>
    /// Parses one line of the structured log.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns>The entry, independent of the parsed document.</returns>
    private static JsonElement ParseEntry(string line)
    {
        using var document = JsonDocument.Parse(line);
        return document.RootElement.Clone();
    }
}
