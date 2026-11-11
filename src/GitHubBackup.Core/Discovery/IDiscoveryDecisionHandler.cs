namespace GitHubBackup.Core.Discovery;

/// <summary>
/// Asks the user, or applies the non-interactive policy of a host, about each repository discovery cannot settle alone.
/// </summary>
/// <remarks>
/// <see cref="IDiscoveryDecisionApplier"/> collects every answer before it changes anything, so a handler may throw
/// <see cref="OperationCanceledException"/> to abandon all decisions.
/// </remarks>
public interface IDiscoveryDecisionHandler
{
    /// <summary>
    /// Decides whether a new repository is added, skipped or excluded.
    /// </summary>
    /// <param name="proposal">The repository and the entry proposed for it.</param>
    /// <param name="cancellationToken">Cancels the decision.</param>
    /// <returns>The decision.</returns>
    /// <exception cref="OperationCanceledException">The user or <paramref name="cancellationToken"/> cancelled.</exception>
    Task<NewRepositoryDecision> DecideNewAsync(NewRepositoryProposal proposal, CancellationToken cancellationToken);

    /// <summary>
    /// Decides whether the archive folder of a renamed repository follows the new name.
    /// </summary>
    /// <param name="proposal">The renamed repository and its archive folders.</param>
    /// <param name="cancellationToken">Cancels the decision.</param>
    /// <returns>The decision.</returns>
    /// <exception cref="OperationCanceledException">The user or <paramref name="cancellationToken"/> cancelled.</exception>
    Task<RenameDecision> DecideRenameAsync(RenameProposal proposal, CancellationToken cancellationToken);
}
