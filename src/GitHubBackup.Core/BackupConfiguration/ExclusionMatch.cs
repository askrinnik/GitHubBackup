namespace GitHubBackup.Core.BackupConfiguration;

/// <summary>
/// Describes the mask that excluded a repository.
/// </summary>
/// <param name="Mask">The mask as written in <c>backup-config.json</c>.</param>
/// <param name="Scope">The list the mask belongs to.</param>
public sealed record ExclusionMatch(string Mask, ExclusionScope Scope);
