namespace GitHubBackup.Core.BackupConfiguration;

/// <summary>
/// Holds a configuration read from <c>backup-config.json</c> together with its validation warnings.
/// </summary>
/// <param name="Config">The valid configuration.</param>
/// <param name="Warnings">The warnings found while validating it.</param>
public sealed record BackupConfigLoadResult(BackupConfig Config, IReadOnlyList<string> Warnings);
