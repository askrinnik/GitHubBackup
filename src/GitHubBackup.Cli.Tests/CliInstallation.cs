using System.Reflection;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.State;
using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.Infrastructure.Logging;
using GitHubBackup.TestUtilities;
using Microsoft.Extensions.Hosting;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Installs a copy of the console executable in a temporary folder, configured for a stubbed GitHub API and for git
/// remotes in a local folder, runs it and deletes the folder on dispose.
/// </summary>
/// <remarks>
/// The folder is the content root of the executable, so the log, the run history and the default clone root of a
/// run all stay inside it. The copy leaves out what a developer's own runs left in the build output: the log and
/// history folders, the clones and <c>backup-config.json</c>.
/// </remarks>
internal sealed class CliInstallation : IDisposable
{
    /// <summary>The name of the executable in the installation folder.</summary>
    private const string _executableName = "GitHubBackup.Cli.exe";

    /// <summary>The name of the git configuration file that replaces the user's global one for the executable.</summary>
    private const string _gitConfigName = "redirect.gitconfig";

    /// <summary>The folders of the build output that runs create; they are not copied.</summary>
    private static readonly string[] _runFolders = [LogFiles.FolderName, "data", BackupConfig.DefaultSourcesFolderName];

    private readonly Dictionary<string, string> _environment;

    /// <summary>
    /// Copies the build output of the console project and writes the settings and the git configuration.
    /// </summary>
    /// <param name="apiBaseUrl">The address of the GitHub API the executable calls.</param>
    /// <param name="remotesRoot">
    /// The folder of the bare repositories; <c>https://github.com/&lt;owner&gt;/&lt;name&gt;.git</c> is redirected to
    /// <c>&lt;remotesRoot&gt;\&lt;owner&gt;\&lt;name&gt;.git</c>.
    /// </param>
    public CliInstallation(string apiBaseUrl, string remotesRoot)
    {
        FolderPath = TemporaryFolder.Create();
        CopyBuildOutput(FolderPath);
        File.WriteAllText(
            Path.Combine(FolderPath, GitHubBackupHostBuilder.AppSettingsFileName),
            CliApplicationTests.ValidAppSettings.Replace("https://api.github.com", apiBaseUrl, StringComparison.Ordinal));

        var gitConfigPath = Path.Combine(FolderPath, _gitConfigName);
        var remotesUrl = new Uri(Path.TrimEndingDirectorySeparator(remotesRoot) + Path.DirectorySeparatorChar).AbsoluteUri;
        File.WriteAllText(
            gitConfigPath,
            $"""
            [url "{remotesUrl}"]
                insteadOf = https://github.com/
            """);

        _environment = new(StringComparer.OrdinalIgnoreCase)
        {
            // Production skips the user secrets of the developer, which could supply another token or API address.
            [GitHubBackupHostSettings.EnvironmentVariableName] = Environments.Production,
            ["GIT_CONFIG_GLOBAL"] = gitConfigPath,
            ["GIT_CONFIG_NOSYSTEM"] = "1",
        };
    }

    /// <summary>Gets the absolute path of the installation folder.</summary>
    public string FolderPath { get; }

    /// <summary>Gets the absolute path of <c>backup-config.json</c>, which the executable reads by default.</summary>
    public string BackupConfigPath => Path.Combine(FolderPath, "backup-config.json");

    /// <summary>Gets the absolute path of the archive root that scenarios configure.</summary>
    public string ArchivesPath => Path.Combine(FolderPath, "archives");

    /// <summary>Gets the absolute path of the default clone root.</summary>
    public string SourcesPath => Path.Combine(FolderPath, BackupConfig.DefaultSourcesFolderName);

    /// <summary>Gets the absolute path of the log folder.</summary>
    public string LogsPath => Path.Combine(FolderPath, LogFiles.FolderName);

    /// <summary>
    /// Returns a builder of <c>backup-config.json</c> whose archive root is <see cref="ArchivesPath"/>.
    /// </summary>
    /// <returns>The builder.</returns>
    public BackupConfigJson NewBackupConfig() => new(ArchivesPath);

    /// <summary>
    /// Returns the clone folder of a repository under the default clone root.
    /// </summary>
    /// <param name="owner">The owner login.</param>
    /// <param name="name">The repository name.</param>
    /// <returns>The absolute path.</returns>
    public string CloneFolder(string owner, string name) => Path.Combine(SourcesPath, owner, name);

    /// <summary>
    /// Returns every archive under <see cref="ArchivesPath"/>.
    /// </summary>
    /// <returns>The absolute paths, or none when the folder does not exist.</returns>
    public string[] FindArchives() => FindFiles(ArchivesPath, "*.zip");

    /// <summary>
    /// Returns every archive under the folder of one repository below <see cref="ArchivesPath"/>.
    /// </summary>
    /// <param name="owner">The owner login.</param>
    /// <param name="name">The repository name.</param>
    /// <returns>The absolute paths, or none when the folder does not exist.</returns>
    public string[] FindArchives(string owner, string name) =>
        [.. FindArchives().Where(path => Path.GetFileName(Path.GetDirectoryName(path))!.Equals(name, StringComparison.Ordinal)
            && Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)))!.Equals(owner, StringComparison.Ordinal))];

    /// <summary>
    /// Returns every state file under <see cref="ArchivesPath"/>.
    /// </summary>
    /// <returns>The absolute paths, or none when the folder does not exist.</returns>
    public string[] FindStateFiles() => FindFiles(ArchivesPath, BackupState.FileName);

    /// <summary>
    /// Runs the executable of the installation.
    /// </summary>
    /// <param name="arguments">The command-line arguments.</param>
    /// <returns>The exit code and the output.</returns>
    public Task<CliProcessResult> RunAsync(params string[] arguments) =>
        CliProcess.RunAsync(
            Path.Combine(FolderPath, _executableName), arguments, _environment, TestContext.Current.CancellationToken);

    /// <summary>
    /// Deletes the installation folder and everything a run created in it.
    /// </summary>
    public void Dispose() => TemporaryFolder.Delete(FolderPath);

    /// <summary>
    /// Returns the files named <paramref name="pattern"/> anywhere under <paramref name="folder"/>.
    /// </summary>
    /// <param name="folder">The root folder.</param>
    /// <param name="pattern">The file name pattern.</param>
    /// <returns>The absolute paths, or none when the folder does not exist.</returns>
    private static string[] FindFiles(string folder, string pattern) =>
        Directory.Exists(folder) ? Directory.GetFiles(folder, pattern, SearchOption.AllDirectories) : [];

    /// <summary>
    /// Copies the build output of the console project into <paramref name="target"/>, without the folders and the
    /// configuration file that runs create.
    /// </summary>
    /// <param name="target">The installation folder.</param>
    /// <exception cref="InvalidOperationException">The build output has no executable.</exception>
    private static void CopyBuildOutput(string target)
    {
        var source = typeof(CliInstallation).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "GitHubBackupCliOutputPath")
            .Value!;
        if (!File.Exists(Path.Combine(source, _executableName)))
        {
            throw new InvalidOperationException($"{_executableName} was not found in '{source}'. Build the solution first.");
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, file);
            var topSegment = relativePath.Split(Path.DirectorySeparatorChar)[0];
            if (_runFolders.Contains(topSegment, StringComparer.OrdinalIgnoreCase)
                || relativePath.Equals("backup-config.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destination = Path.Combine(target, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }
}
