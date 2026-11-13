using GitHubBackup.Core.Discovery;

namespace GitHubBackup.App.Discovery;

/// <summary>
/// Decides about discovered repositories for a run started from the desktop application without asking: every new
/// repository is skipped, every renamed repository keeps its archive folder, and every ID found for an entry without
/// one is written, in a dry run too.
/// </summary>
/// <remarks>
/// Skipping writes nothing, so discovery offers the repository again on the next run, and keeping the archive folder
/// never moves existing archives; neither choice decides anything the user has not confirmed. An ID found by the name
/// the user configured only completes that entry.
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

    /// <inheritdoc />
    public Task<bool> ConfirmIdSaveAsync(RepositoryIdProposal proposal, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<bool>(cancellationToken)
            : Task.FromResult(true);
}
