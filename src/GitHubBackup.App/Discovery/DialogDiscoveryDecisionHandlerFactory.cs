using GitHubBackup.App.Dialogs;
using GitHubBackup.Core.Discovery;

namespace GitHubBackup.App.Discovery;

/// <summary>
/// Creates a <see cref="DialogDiscoveryDecisionHandler"/> for each run.
/// </summary>
/// <param name="dialogService">Shows the discovery dialog.</param>
internal sealed class DialogDiscoveryDecisionHandlerFactory(IDiscoveryDialogService dialogService)
    : IDiscoveryDecisionHandlerFactory
{
    /// <inheritdoc />
    public IDiscoveryDecisionHandler Create(Action cancelRun) => new DialogDiscoveryDecisionHandler(dialogService, cancelRun);
}
