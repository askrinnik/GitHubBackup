namespace GitHubBackup.Core.BackupConfiguration;

/// <summary>
/// Holds the outcome of validating a <see cref="BackupConfig"/>.
/// </summary>
/// <param name="Errors">The problems that make the configuration unusable, one sentence each.</param>
/// <param name="Warnings">The problems that the user is told about but that do not stop a run.</param>
public sealed record BackupConfigValidationResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    /// <summary>Gets a value indicating whether the configuration has no errors.</summary>
    public bool IsValid => Errors.Count == 0;
}
