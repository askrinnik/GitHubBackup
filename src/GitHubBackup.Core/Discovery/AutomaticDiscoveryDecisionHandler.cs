namespace GitHubBackup.Core.Discovery;

/// <summary>
/// Decides about discovered repositories without asking anyone, by the policy of the silent mode: every new repository
/// is added with the default branch and the derived archive folder, every renamed repository keeps its archive
/// folder, and every ID found for an entry without one is written.
/// </summary>
/// <remarks>
/// Keeping the archive folder of a renamed repository is the choice that never moves existing archives without the
/// user's consent; the old folder is written to the <c>path</c> of the entry.
/// </remarks>
public sealed class AutomaticDiscoveryDecisionHandler : IDiscoveryDecisionHandler
{
    /// <inheritdoc />
    public Task<NewRepositoryDecision> DecideNewAsync(NewRepositoryProposal proposal, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<NewRepositoryDecision>(cancellationToken)
            : Task.FromResult(NewRepositoryDecision.Add());

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
