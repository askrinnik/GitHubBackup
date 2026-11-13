namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Groups the tests that run a whole backup; they run one at a time, because every run takes the machine-wide run
/// lock and a second run that finds it taken ends with a critical error.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BackupRunLockTestGroup
{
    /// <summary>The name of the test collection.</summary>
    public const string Name = "Backup run lock";
}
