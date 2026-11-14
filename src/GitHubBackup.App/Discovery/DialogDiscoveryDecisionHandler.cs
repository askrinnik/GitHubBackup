using GitHubBackup.App.Dialogs;
using GitHubBackup.App.ViewModels;
using GitHubBackup.Core.Discovery;

namespace GitHubBackup.App.Discovery;

/// <summary>
/// Decides about discovered repositories for one run started from the desktop application: it shows every finding in
/// the discovery dialog and answers the questions about single repositories with what the user chose there.
/// </summary>
/// <remarks>
/// Cancelling or closing the dialog cancels the whole run, as Ctrl+C does in the console application. The orchestrator
/// records a run as cancelled only when its own token is cancelled, so the handler cancels the run through
/// <c>cancelRun</c> before it throws. Every ID found for an entry without one is written, in a dry run too: an ID
/// found by the name the user configured only completes that entry.
/// </remarks>
/// <param name="dialogService">Shows the discovery dialog.</param>
/// <param name="cancelRun">Cancels the token of the run that uses this handler.</param>
internal sealed class DialogDiscoveryDecisionHandler(IDiscoveryDialogService dialogService, Action cancelRun)
    : IDiscoveryDecisionHandler
{
    /// <summary>The decisions about new repositories the user applied, by repository id.</summary>
    private IReadOnlyDictionary<long, NewRepositoryDecision> _newDecisions = new Dictionary<long, NewRepositoryDecision>();

    /// <summary>The decisions about renamed repositories the user applied, by repository id.</summary>
    private IReadOnlyDictionary<long, RenameDecision> _renameDecisions = new Dictionary<long, RenameDecision>();

    /// <inheritdoc />
    /// <remarks>Findings without any repository show no dialog.</remarks>
    public async Task ReviewAsync(DiscoveryReview review, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!review.HasItems)
        {
            return;
        }

        var viewModel = new DiscoveryReviewViewModel(review);
        var applied = await dialogService.ShowAsync(viewModel, cancellationToken);
        if (!applied)
        {
            cancelRun();
            throw new OperationCanceledException("The user cancelled the discovery dialog.", cancellationToken);
        }

        _newDecisions = viewModel.GetNewDecisions();
        _renameDecisions = viewModel.GetRenameDecisions();
    }

    /// <inheritdoc />
    /// <remarks>A repository the dialog did not show is skipped, so nothing unconfirmed is written.</remarks>
    public Task<NewRepositoryDecision> DecideNewAsync(NewRepositoryProposal proposal, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<NewRepositoryDecision>(cancellationToken)
            : Task.FromResult(_newDecisions.GetValueOrDefault(proposal.Repository.Id, NewRepositoryDecision.Skip));

    /// <inheritdoc />
    /// <remarks>A repository the dialog did not show keeps its archive folder, so no archives move unconfirmed.</remarks>
    public Task<RenameDecision> DecideRenameAsync(RenameProposal proposal, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<RenameDecision>(cancellationToken)
            : Task.FromResult(_renameDecisions.GetValueOrDefault(proposal.Renamed.Current.Id, RenameDecision.KeepArchiveFolder));

    /// <inheritdoc />
    public Task<bool> ConfirmIdSaveAsync(RepositoryIdProposal proposal, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<bool>(cancellationToken)
            : Task.FromResult(true);
}
