namespace GitHubBackup.Core.Runs;

/// <summary>
/// Identifies the run that the current host performs; every log entry carries its identifier.
/// </summary>
public interface IRunContext
{
    /// <summary>Gets the identifier of the run.</summary>
    Guid RunId { get; }
}
