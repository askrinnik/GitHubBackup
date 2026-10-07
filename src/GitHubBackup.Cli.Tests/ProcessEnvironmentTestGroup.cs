namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Groups the tests that change environment variables of the test process; they run alone, after the parallel
/// tests, so no other host reads a variable they set.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentTestGroup
{
    /// <summary>The name of the test collection.</summary>
    public const string Name = "Process environment";
}
