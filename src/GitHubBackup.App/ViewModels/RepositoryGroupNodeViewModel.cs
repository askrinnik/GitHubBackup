namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Backs the node that groups the standalone repositories, tracked without their account.
/// </summary>
/// <param name="repositories">The nodes of the standalone repositories, in file order.</param>
/// <param name="selectionChanged">Receives the node and its new selection state.</param>
public sealed class RepositoryGroupNodeViewModel(
    IReadOnlyList<RepositoryNodeViewModel> repositories, Action<TreeNodeViewModel, bool> selectionChanged)
    : TreeNodeViewModel(GroupKey, selectionChanged)
{
    /// <summary>The text the tree shows for the group.</summary>
    public const string GroupName = "Individual repositories";

    /// <summary>The key of the group, which exists at most once in the tree.</summary>
    public const string GroupKey = "group:individual";

    /// <summary>Gets the nodes of the standalone repositories.</summary>
    public IReadOnlyList<RepositoryNodeViewModel> Repositories { get; } = repositories;

    /// <inheritdoc />
    public override string DisplayName => GroupName;

    /// <inheritdoc />
    public override IReadOnlyList<TreeNodeViewModel> Children => Repositories;
}
