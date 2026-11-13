using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHubBackup.Core.Discovery;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Backs the discovery dialog: one row per new, renamed, missing and returned repository, the actions for every row
/// at once, and the decisions the dialog returns.
/// </summary>
/// <remarks>
/// The rows are new repositories first, then renamed, missing and returned ones, each group in the order discovery
/// reported it. The actions for every row leave the informational rows of missing and returned repositories alone.
/// </remarks>
public sealed partial class DiscoveryReviewViewModel : ObservableObject
{
    /// <summary>The text of the button that applies the decisions.</summary>
    public const string ApplyLabel = "Apply";

    /// <summary>The text of the same button when no row asks for a decision.</summary>
    public const string ContinueLabel = "Continue";

    /// <summary>
    /// Initializes the dialog with the findings of discovery.
    /// </summary>
    /// <param name="review">The findings.</param>
    public DiscoveryReviewViewModel(DiscoveryReview review)
    {
        NewRows = [.. review.New.Select(proposal => new NewDiscoveryRowViewModel(proposal, ApplyCommand.NotifyCanExecuteChanged))];
        RenamedRows = [.. review.Renamed.Select(proposal => new RenamedDiscoveryRowViewModel(proposal))];
        Rows =
        [
            .. NewRows,
            .. RenamedRows,
            .. review.Missing.Select(tracked => new MissingDiscoveryRowViewModel(tracked)),
            .. review.Returned.Select(returned => new ReturnedDiscoveryRowViewModel(returned)),
        ];
    }

    /// <summary>
    /// Occurs when the dialog is to close; the argument is <see langword="true"/> when the decisions are applied and
    /// <see langword="false"/> when the user cancelled.
    /// </summary>
    public event EventHandler<bool>? CloseRequested;

    /// <summary>Gets every row of the dialog.</summary>
    public IReadOnlyList<DiscoveryRowViewModel> Rows { get; }

    /// <summary>Gets the rows of new repositories.</summary>
    public IReadOnlyList<NewDiscoveryRowViewModel> NewRows { get; }

    /// <summary>Gets the rows of renamed repositories.</summary>
    public IReadOnlyList<RenamedDiscoveryRowViewModel> RenamedRows { get; }

    /// <summary>Gets whether at least one row asks for a decision.</summary>
    public bool HasActionableRows => NewRows.Count > 0 || RenamedRows.Count > 0;

    /// <summary>Gets whether the dialog lists new repositories, so the actions for all of them are offered.</summary>
    public bool HasNewRows => NewRows.Count > 0;

    /// <summary>Gets whether the dialog lists renamed repositories, so the choices for all of them are offered.</summary>
    public bool HasRenamedRows => RenamedRows.Count > 0;

    /// <summary>Gets the text of the button that closes the dialog and goes on with the run.</summary>
    public string ApplyText => HasActionableRows ? ApplyLabel : ContinueLabel;

    /// <summary>
    /// Returns the decisions about the new repositories, by repository id.
    /// </summary>
    /// <returns>One decision per new row.</returns>
    /// <exception cref="InvalidOperationException">A row that is added has a value that cannot be stored.</exception>
    public IReadOnlyDictionary<long, NewRepositoryDecision> GetNewDecisions() =>
        NewRows.ToDictionary(row => row.Proposal.Repository.Id, row => row.ToDecision());

    /// <summary>
    /// Returns the decisions about the archive folders of the renamed repositories, by repository id.
    /// </summary>
    /// <returns>One decision per renamed row.</returns>
    public IReadOnlyDictionary<long, RenameDecision> GetRenameDecisions() =>
        RenamedRows.ToDictionary(row => row.Proposal.Renamed.Current.Id, row => row.ToDecision());

    /// <summary>
    /// Chooses <see cref="NewRepositoryChoice.Add"/> for every new repository.
    /// </summary>
    [RelayCommand]
    private void AddAll() => SetAllChoices(_ => NewRepositoryChoice.Add);

    /// <summary>
    /// Chooses <see cref="NewRepositoryChoice.Skip"/> for every new repository.
    /// </summary>
    [RelayCommand]
    private void SkipAll() => SetAllChoices(_ => NewRepositoryChoice.Skip);

    /// <summary>
    /// Excludes every new repository in the narrowest list of masks that can hold it: the masks of its account, or the
    /// global masks for a standalone repository.
    /// </summary>
    [RelayCommand]
    private void ExcludeAll() => SetAllChoices(row => row.NarrowestExclusion);

    /// <summary>
    /// Chooses <see cref="RenameDecision.RenameArchiveFolder"/> for every renamed repository whose folder depends on
    /// its name.
    /// </summary>
    [RelayCommand]
    private void RenameAllArchiveFolders() => SetAllVariants(RenameDecision.RenameArchiveFolder);

    /// <summary>
    /// Chooses <see cref="RenameDecision.KeepArchiveFolder"/> for every renamed repository.
    /// </summary>
    [RelayCommand]
    private void KeepAllArchiveFolders() => SetAllVariants(RenameDecision.KeepArchiveFolder);

    /// <summary>
    /// Returns whether the decisions can be applied: no row that is added has a value that cannot be stored.
    /// </summary>
    /// <returns><see langword="true"/> when every row is valid.</returns>
    private bool CanApply() => !Rows.Any(row => row.HasErrors);

    /// <summary>
    /// Closes the dialog and applies the decisions.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply() => CloseRequested?.Invoke(this, true);

    /// <summary>
    /// Closes the dialog without a decision, which cancels the run.
    /// </summary>
    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    /// <summary>
    /// Sets the action of every new repository.
    /// </summary>
    /// <param name="choose">Returns the action for a row.</param>
    private void SetAllChoices(Func<NewDiscoveryRowViewModel, NewRepositoryChoice> choose)
    {
        foreach (var row in NewRows)
        {
            row.Choice = choose(row);
        }
    }

    /// <summary>
    /// Sets the choice for the archive folder of every renamed repository whose folder depends on its name.
    /// </summary>
    /// <param name="variant">The choice.</param>
    private void SetAllVariants(RenameDecision variant)
    {
        foreach (var row in RenamedRows.Where(row => row.CanChooseVariant))
        {
            row.Variant = variant;
        }
    }
}
