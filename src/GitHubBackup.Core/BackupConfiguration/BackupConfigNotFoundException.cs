namespace GitHubBackup.Core.BackupConfiguration;

/// <summary>
/// Reports that <c>backup-config.json</c> does not exist; only the settings editor of the WPF application creates it.
/// </summary>
/// <param name="configPath">The path of the missing configuration file.</param>
/// <param name="innerException">The exception that reported the missing file, or <see langword="null"/>.</param>
public sealed class BackupConfigNotFoundException(string configPath, Exception? innerException = null)
    : BackupConfigException(configPath, [FormatError(configPath)], innerException)
{
    /// <summary>
    /// Builds the error that names the missing file and tells how to create it.
    /// </summary>
    /// <param name="configPath">The path of the missing configuration file.</param>
    /// <returns>The error message.</returns>
    public static string FormatError(string configPath) =>
        $"The backup configuration file '{configPath}' does not exist. Create it in the settings editor of GitHubBackup.exe.";
}
