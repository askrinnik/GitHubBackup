namespace GitHubBackup.Core.BackupConfiguration;

/// <summary>
/// Checks a <see cref="BackupConfig"/> against the rules of <c>backup-config.json</c>.
/// </summary>
public interface IBackupConfigValidator
{
    /// <summary>
    /// Validates <paramref name="config"/> and collects every error and warning.
    /// </summary>
    /// <remarks>
    /// Each message names its location in the file, for example <c>accounts[1].url</c>, and never quotes a URL value:
    /// a pasted URL can carry a token.
    /// </remarks>
    /// <param name="config">The configuration to validate.</param>
    /// <param name="defaultSourcesRoot">The absolute clone folder used when <see cref="BackupConfig.SourcesRoot"/> is <see langword="null"/>.</param>
    /// <returns>The errors and warnings found.</returns>
    BackupConfigValidationResult Validate(BackupConfig config, string defaultSourcesRoot);
}
