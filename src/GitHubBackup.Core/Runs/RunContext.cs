namespace GitHubBackup.Core.Runs;

/// <summary>
/// Holds the identifier of the current run, created with the instance and replaced by <see cref="BeginRun"/>;
/// registered as a singleton, it identifies the runs of the host.
/// </summary>
/// <remarks>
/// The identifier is read by the logger on any thread while a run on another thread may replace it, so reads and
/// writes take a lock: a <see cref="Guid"/> is not written atomically.
/// </remarks>
public sealed class RunContext : IRunContext
{
    /// <summary>Guards <see cref="_runId"/>.</summary>
    private readonly Lock _gate = new();

    /// <summary>The identifier of the current run.</summary>
    private Guid _runId = Guid.NewGuid();

    /// <inheritdoc />
    public Guid RunId
    {
        get
        {
            lock (_gate)
            {
                return _runId;
            }
        }
    }

    /// <inheritdoc />
    public Guid BeginRun()
    {
        var runId = Guid.NewGuid();
        lock (_gate)
        {
            _runId = runId;
        }

        return runId;
    }
}
