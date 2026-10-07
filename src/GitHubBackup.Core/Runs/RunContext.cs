namespace GitHubBackup.Core.Runs;

/// <summary>
/// Holds a run identifier created once per instance; registered as a singleton, it identifies the run of the host.
/// </summary>
public sealed class RunContext : IRunContext
{
    /// <inheritdoc />
    public Guid RunId { get; } = Guid.NewGuid();
}
