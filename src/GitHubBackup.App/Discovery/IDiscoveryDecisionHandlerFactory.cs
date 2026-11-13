using GitHubBackup.Core.Discovery;

namespace GitHubBackup.App.Discovery;

/// <summary>
/// Creates the decision handler of one run started from the desktop application.
/// </summary>
public interface IDiscoveryDecisionHandlerFactory
{
    /// <summary>
    /// Creates a handler for a new run.
    /// </summary>
    /// <param name="cancelRun">Cancels the token of the run, so a cancelled dialog cancels the whole run.</param>
    /// <returns>The handler, used by that run only.</returns>
    IDiscoveryDecisionHandler Create(Action cancelRun);
}
