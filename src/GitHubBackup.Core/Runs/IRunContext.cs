namespace GitHubBackup.Core.Runs;

/// <summary>
/// Identifies the run that the current host performs; every log entry carries its identifier.
/// </summary>
public interface IRunContext
{
    /// <summary>Gets the identifier of the current run.</summary>
    Guid RunId { get; }

    /// <summary>
    /// Starts a new run: creates a new identifier that <see cref="RunId"/> returns from then on, so the log entries
    /// and the history record of the next run carry it.
    /// </summary>
    /// <remarks>
    /// A host that performs one run per process never calls it; a host that performs several runs calls it before
    /// each of them, because the run history keeps one record per identifier.
    /// </remarks>
    /// <returns>The new identifier.</returns>
    Guid BeginRun();
}
