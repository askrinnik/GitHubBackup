using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Infrastructure.Logging;

namespace GitHubBackup.TestUtilities;

/// <summary>
/// Copies the build output of a host project into a test folder, so that a test runs the executable from a content
/// root it owns.
/// </summary>
public static class BuildOutput
{
    /// <summary>The name of the backup configuration file that runs of a developer may leave in the build output.</summary>
    private const string _backupConfigFileName = "backup-config.json";

    /// <summary>The folders of the build output that runs create: the log, the run history and the default clones.</summary>
    private static readonly string[] _runFolders = [LogFiles.FolderName, "data", BackupConfig.DefaultSourcesFolderName];

    /// <summary>
    /// Copies every file of <paramref name="sourceFolder"/> into <paramref name="targetFolder"/>, without the folders
    /// that runs create and without <c>backup-config.json</c>.
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
                || relativePath.Equals(_backupConfigFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destination = Path.Combine(targetFolder, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }
}
