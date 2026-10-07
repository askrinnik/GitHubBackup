namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Creates a unique empty folder that serves as the content root of one test host and deletes it on dispose, so that
/// the log files a run writes never land in the test output folder.
/// </summary>
internal sealed class TemporaryContentRoot : IDisposable
{
    /// <summary>
    /// Creates the folder.
    /// </summary>
    public TemporaryContentRoot() => Directory.CreateDirectory(FolderPath);

    /// <summary>Gets the absolute path of the folder.</summary>
    public string FolderPath { get; } = Path.Combine(Path.GetTempPath(), "GitHubBackup.Tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Deletes the folder and everything in it.
    /// </summary>
    public void Dispose() => Directory.Delete(FolderPath, recursive: true);
}
