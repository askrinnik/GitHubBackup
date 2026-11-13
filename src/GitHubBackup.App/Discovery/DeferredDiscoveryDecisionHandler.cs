using GitHubBackup.Core.Discovery;

namespace GitHubBackup.App.Discovery;

/// <summary>
/// Decides about discovered repositories for a run started from the desktop application without asking: every new
/// repository is skipped and every renamed repository keeps its archive folder.
/// </summary>
/// <remarks>
/// Skipping writes nothing, so discovery offers the repository again on the next run, and keeping the archive folder
/// never moves existing archives; neither choice decides anything the user has not confirmed.
/// </remarks>
internal sealed class DeferredDiscoveryDecisionHandler : IDiscoveryDecisionHandler
{
    /// <inheritdoc />
    public Task<NewRepositoryDecision> DecideNewAsync(NewRepositoryProposal proposal, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<NewRepositoryDecision>(cancellationToken)
            : Task.FromResult(NewRepositoryDecision.Skip);

    /// <inheritdoc />
    public Task<RenameDecision> DecideRenameAsync(RenameProposal proposal, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<RenameDecision>(cancellationToken)
            : Task.FromResult(RenameDecision.KeepArchiveFolder);
}
