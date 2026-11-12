using GitHubBackup.Core.Discovery;

namespace GitHubBackup.Core.Runs;

/// <summary>
/// Describes one backup run: which repositories it covers and who decides about the repositories discovery finds.
/// </summary>
/// <remarks>
/// The host chooses the decision handler for each run, so the same orchestrator serves an interactive console, the
/// silent mode and the dialog of the desktop application.
/// </remarks>
/// <param name="Filter">The repositories the run covers.</param>
/// <param name="DecisionHandler">Decides about new and renamed repositories.</param>
public sealed record BackupRunRequest(BackupRunFilter Filter, IDiscoveryDecisionHandler DecisionHandler)
{
    /// <summary>
    /// Gets whether the run only shows what a backup run would do: nothing is cloned, updated, archived or saved, the
    /// folder of temporary archives is not emptied and <see cref="DecisionHandler"/> is not asked.
    /// </summary>
    public bool DryRun { get; init; }
}
