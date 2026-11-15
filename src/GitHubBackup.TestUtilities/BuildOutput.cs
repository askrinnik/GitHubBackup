using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.Infrastructure.Logging;

namespace GitHubBackup.TestUtilities;

/// <summary>
/// Copies the build output of a host project into a test folder, so that a test runs the executable from a content
/// root it owns.
/// </summary>
public static class BuildOutput
{
    /// <summary>The extension of the copy of its previous version that the application keeps next to a file it writes.</summary>
    private const string _backupExtension = ".bak";

    /// <summary>The name of the settings file that earlier builds of the desktop app wrote next to the executable.</summary>
    private const string _legacyUserSettingsFileName = "user-settings.json";

    /// <summary>
    /// The files that runs of a developer may leave in the build output: the backup configuration, the settings file
    /// the settings editor writes, the user interface settings the main window writes, their backups, and the
    /// <c>user-settings.json</c> that earlier builds of the desktop app wrote; a test that checks the application
    /// leaves that file alone must start without it.
    /// </summary>
    private static readonly string[] _runFiles =
    [
        "backup-config.json",
        GitHubBackupHostBuilder.AppSettingsFileName,
        GitHubBackupHostBuilder.UiSettingsFileName,
        _legacyUserSettingsFileName,
        "backup-config.json" + _backupExtension,
        GitHubBackupHostBuilder.AppSettingsFileName + _backupExtension,
        GitHubBackupHostBuilder.UiSettingsFileName + _backupExtension,
        _legacyUserSettingsFileName + _backupExtension,
    ];

    /// <summary>The folders of the build output that runs create: the log, the run history and the default clones.</summary>
    private static readonly string[] _runFolders = [LogFiles.FolderName, "data", BackupConfig.DefaultSourcesFolderName];

    /// <summary>
    /// Copies every file of <paramref name="sourceFolder"/> into <paramref name="targetFolder"/>, without the folders
    /// that runs create and without the settings and configuration files that runs write.
    /// </summary>
    /// <param name="sourceFolder">The absolute path of the build output.</param>
    /// <param name="targetFolder">The absolute path of the folder to copy into.</param>
    /// <param name="executableName">The file name of the executable the build output must contain.</param>
    /// <exception cref="InvalidOperationException">The build output has no <paramref name="executableName"/>.</exception>
    public static void CopyTo(string sourceFolder, string targetFolder, string executableName)
    {
        if (!File.Exists(Path.Combine(sourceFolder, executableName)))
        {
            throw new InvalidOperationException(
                $"{executableName} was not found in '{sourceFolder}'. Build the solution first.");
        }

        foreach (var file in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceFolder, file);
            var topSegment = relativePath.Split(Path.DirectorySeparatorChar)[0];
            if (_runFolders.Contains(topSegment, StringComparer.OrdinalIgnoreCase)
                || _runFiles.Contains(relativePath, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var destination = Path.Combine(targetFolder, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }
}
