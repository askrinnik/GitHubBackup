using CommunityToolkit.Mvvm.ComponentModel;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Backs one node of the repository tree: its selection and expansion, which the tree view binds two-way.
/// </summary>
/// <param name="key">Identifies the node across reloads of the tree, so the selection survives a reload.</param>
/// <param name="selectionChanged">Receives the node and its new selection state whenever it is selected or deselected.</param>
public abstract partial class TreeNodeViewModel(string key, Action<TreeNodeViewModel, bool> selectionChanged) : ObservableObject
{
    /// <summary>Whether the node is selected in the tree.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Whether the children of the node are shown.</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>Gets the key that identifies the node across reloads of the tree.</summary>
    public string Key { get; } = key;

    /// <summary>Gets the text the tree shows for the node.</summary>
    public abstract string DisplayName { get; }

    /// <summary>Gets the child nodes; empty for a leaf.</summary>
    public virtual IReadOnlyList<TreeNodeViewModel> Children => [];

    /// <summary>
    /// Reports the new selection state to the owner of the tree.
    /// </summary>
    /// <param name="value">Whether the node is selected.</param>
    partial void OnIsSelectedChanged(bool value) => selectionChanged(this, value);
}
