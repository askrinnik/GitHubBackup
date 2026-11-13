using CommunityToolkit.Mvvm.ComponentModel;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Discovery;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// The row of a new repository in the discovery dialog: the entry proposed for it, with the action, the branch and
/// the archive folder the user can change.
/// </summary>
/// <remarks>
/// The row starts as <see cref="NewRepositoryChoice.Add"/> with the default branch and the derived archive folder,
/// the same proposal the console application offers first. The branch and the archive folder are written only when the
/// repository is added, so only then can they make the row invalid.
/// </remarks>
/// <param name="proposal">The repository and the entry proposed for it.</param>
/// <param name="errorsChanged">Called when <see cref="HasErrors"/> may have changed.</param>
public sealed partial class NewDiscoveryRowViewModel(NewRepositoryProposal proposal, Action errorsChanged)
    : DiscoveryRowViewModel(proposal.Repository.FullName, proposal.Repository.Id, proposal.AccountLogin, proposal.Repository)
{
    /// <summary>The message shown for a branch that cannot be stored.</summary>
    public const string InvalidBranchMessage = "The branch must not start with '-'.";

    /// <summary>The message shown for an archive folder that cannot be stored.</summary>
    public const string InvalidPathMessage = "The archive folder must be an absolute path without invalid characters.";

    /// <summary>The actions offered for a repository of a tracked account.</summary>
    private static readonly IReadOnlyList<DiscoveryOption<NewRepositoryChoice>> _accountChoices =
    [
        new(NewRepositoryChoice.Add, "Add"),
        new(NewRepositoryChoice.Skip, "Skip this time"),
        new(NewRepositoryChoice.ExcludeFromAccount, "Exclude from account"),
        new(NewRepositoryChoice.ExcludeGlobally, "Exclude globally"),
    ];

    /// <summary>
    /// The actions offered for a standalone repository, which has no account whose masks could exclude it.
    /// </summary>
    private static readonly IReadOnlyList<DiscoveryOption<NewRepositoryChoice>> _standaloneChoices =
        [.. _accountChoices.Where(option => option.Value != NewRepositoryChoice.ExcludeFromAccount)];

    /// <summary>Tells the dialog that <see cref="HasErrors"/> may have changed.</summary>
    private readonly Action _errorsChanged = errorsChanged;

    /// <summary>The action chosen for the repository.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAdded), nameof(HasErrors))]
    private NewRepositoryChoice _choice = NewRepositoryChoice.Add;

    /// <summary>The branch typed for the entry; an empty text stands for the default branch.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BranchError), nameof(HasBranchError), nameof(HasErrors))]
    private string _branch = string.Empty;

    /// <summary>The archive folder typed for the entry; an empty text stands for the derived folder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PathError), nameof(HasPathError), nameof(HasErrors))]
    private string _path = proposal.ArchiveFolder;

    /// <summary>Gets the repository and the entry proposed for it.</summary>
    public NewRepositoryProposal Proposal { get; } = proposal;

    /// <summary>Gets the actions offered for the repository.</summary>
    public IReadOnlyList<DiscoveryOption<NewRepositoryChoice>> Choices { get; } =
        proposal.AccountLogin is null ? _standaloneChoices : _accountChoices;

    /// <summary>Gets whether the repository is a standalone repository.</summary>
    public bool IsStandalone => Proposal.AccountLogin is null;

    /// <summary>Gets the narrowest list of masks that can exclude the repository.</summary>
    public NewRepositoryChoice NarrowestExclusion =>
        IsStandalone ? NewRepositoryChoice.ExcludeGlobally : NewRepositoryChoice.ExcludeFromAccount;

    /// <summary>Gets whether the repository is added, so its branch and archive folder are written.</summary>
    public bool IsAdded => Choice == NewRepositoryChoice.Add;

    /// <summary>Gets why <see cref="Branch"/> cannot be stored, or <see langword="null"/> when it can.</summary>
    public string? BranchError =>
        Branch.Trim() is { Length: > 0 } branch && !BackupConfigValidator.IsValidBranch(branch) ? InvalidBranchMessage : null;

    /// <summary>Gets whether <see cref="Branch"/> cannot be stored.</summary>
    public bool HasBranchError => BranchError is not null;

    /// <summary>Gets why <see cref="Path"/> cannot be stored, or <see langword="null"/> when it can.</summary>
    public string? PathError =>
        Path.Trim() is { Length: > 0 } path && !BackupConfigValidator.IsAbsolutePath(path) ? InvalidPathMessage : null;

    /// <summary>Gets whether <see cref="Path"/> cannot be stored.</summary>
    public bool HasPathError => PathError is not null;

    /// <inheritdoc />
    public override string Kind => "New";

    /// <inheritdoc />
    public override string Note => string.Empty;

    /// <inheritdoc />
    public override bool IsActionable => true;

    /// <inheritdoc />
    public override bool HasErrors => IsAdded && (HasBranchError || HasPathError);

    /// <summary>
    /// Returns the decision the row stands for.
    /// </summary>
    /// <remarks>
    /// An empty branch stands for the default branch. An empty archive folder, or one that differs from the derived
    /// folder at most in case and a trailing separator, stands for the derived folder, so the entry keeps no
    /// <c>path</c> it does not need.
    /// </remarks>
    /// <returns>The decision.</returns>
    /// <exception cref="InvalidOperationException">The row is added and <see cref="HasErrors"/> is set.</exception>
    public NewRepositoryDecision ToDecision()
    {
        switch (Choice)
        {
            case NewRepositoryChoice.Skip:
                return NewRepositoryDecision.Skip;
            case NewRepositoryChoice.ExcludeFromAccount:
                return NewRepositoryDecision.Exclude(ExclusionScope.Account);
            case NewRepositoryChoice.ExcludeGlobally:
                return NewRepositoryDecision.Exclude(ExclusionScope.Global);
        }

        if (HasErrors)
        {
            throw new InvalidOperationException($"The row of '{Repository}' has a value that cannot be stored.");
        }

        var branch = Branch.Trim();
        var path = Path.Trim();
        return NewRepositoryDecision.Add(
            branch.Length == 0 ? null : branch,
            path.Length == 0 || ArchiveFolderComparison.IsSameFolder(path, Proposal.ArchiveFolder) ? null : path);
    }

    /// <summary>
    /// Reports a possible change of <see cref="HasErrors"/> to the dialog.
    /// </summary>
    /// <param name="value">The new action.</param>
    partial void OnChoiceChanged(NewRepositoryChoice value) => _errorsChanged();

    /// <summary>
    /// Reports a possible change of <see cref="HasErrors"/> to the dialog.
    /// </summary>
    /// <param name="value">The new branch.</param>
    partial void OnBranchChanged(string value) => _errorsChanged();

    /// <summary>
    /// Reports a possible change of <see cref="HasErrors"/> to the dialog.
    /// </summary>
    /// <param name="value">The new archive folder.</param>
    partial void OnPathChanged(string value) => _errorsChanged();
}
