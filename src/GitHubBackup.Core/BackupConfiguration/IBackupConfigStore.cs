namespace GitHubBackup.Core.BackupConfiguration;

/// <summary>
/// Reads and writes <c>backup-config.json</c>.
/// </summary>
public interface IBackupConfigStore
{
    /// <summary>
    /// Reads and validates the configuration file.
    /// </summary>
    /// <param name="cancellationToken">Cancels reading the file.</param>
    /// <returns>The configuration and its validation warnings.</returns>
    /// <exception cref="BackupConfigException">
    /// The file does not exist, is not valid JSON, has an unsupported schema version or fails validation.
    /// </exception>
    Task<BackupConfigLoadResult> LoadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Validates <paramref name="config"/> and replaces the configuration file with it atomically, keeping the
    /// previous file as <c>&lt;file&gt;.bak</c>.
    /// </summary>
    /// <remarks>A failure or cancellation leaves the previous file unchanged.</remarks>
    /// <param name="config">The configuration to write.</param>
    /// <param name="cancellationToken">Cancels the write before the file is replaced.</param>
    /// <returns>A task that completes when the file is replaced.</returns>
    /// <exception cref="BackupConfigException"><paramref name="config"/> fails validation; nothing is written.</exception>
    Task SaveAsync(BackupConfig config, CancellationToken cancellationToken);
}
