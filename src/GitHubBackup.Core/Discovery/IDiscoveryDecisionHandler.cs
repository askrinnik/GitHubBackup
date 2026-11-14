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
    /// Receives every finding of discovery before the first question about a single repository, so a host that
    /// shows all findings together can collect its answers here.
    /// </summary>
    /// <remarks>
    /// <see cref="IDiscoveryDecisionApplier"/> calls this method once, before anything changes, and then asks
    /// <see cref="DecideNewAsync"/> and <see cref="DecideRenameAsync"/> about each repository of
    /// <paramref name="review"/> in the same order.
    /// </remarks>
    /// <param name="review">The findings.</param>
    /// <param name="cancellationToken">Cancels the review.</param>
    /// <returns>A task that completes when the handler has its answers.</returns>
    /// <exception cref="OperationCanceledException">The user or <paramref name="cancellationToken"/> cancelled.</exception>
    Task ReviewAsync(DiscoveryReview review, CancellationToken cancellationToken);

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

    /// <summary>
    /// Decides whether a dry run writes the ID found for an entry without one to <c>backup-config.json</c>.
    /// </summary>
    /// <remarks>A backup run writes the ID without asking.</remarks>
    /// <param name="proposal">The entry and the ID found for it.</param>
    /// <param name="cancellationToken">Cancels the decision.</param>
    /// <returns><see langword="true"/> to write the ID; <see langword="false"/> keeps it in memory for the run.</returns>
    /// <exception cref="OperationCanceledException">The user or <paramref name="cancellationToken"/> cancelled.</exception>
    Task<bool> ConfirmIdSaveAsync(RepositoryIdProposal proposal, CancellationToken cancellationToken);
}
