using CommunityToolkit.Mvvm.ComponentModel;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Backs one node of the repository tree: its selection and expansion, which the tree view binds two-way.
/// </summary>
/// <param name="key">Identifies the node across reloads of the tree, so the selection survives a reload.</param>
/// <param name="selectionChanged">Receives the node and its new selection state whenever it is selected or deselected.</param>
public abstract partial class TreeNodeViewModel(string key, Action<TreeNodeViewModel, bool> selectionChanged) : ObservableObject
{
    /// <summary>The status name of a node that an unfiltered run backs up.</summary>
    public const string ActiveStatusName = "Active";

    /// <summary>The status name of a paused node.</summary>
    public const string PausedStatusName = "Paused";

    /// <summary>The status name of an unavailable repository.</summary>
    public const string UnavailableStatusName = "Unavailable";

    /// <summary>The status name of an unavailable repository that is paused too.</summary>
    public const string UnavailablePausedStatusName = "Unavailable, paused";

    /// <summary>The status name of a repository that an exclusion mask matches.</summary>
    public const string ExcludedStatusName = "Excluded";

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

    /// <summary>Gets whether the first status slot shows the check: an unfiltered run backs the node up.</summary>
    public virtual bool ShowsActiveIcon => false;

    /// <summary>Gets whether the first status slot shows the pause icon.</summary>
    public virtual bool ShowsPausedIcon => false;

    /// <summary>Gets whether the first status slot shows the minus of an excluded repository.</summary>
    public virtual bool ShowsExcludedIcon => false;

    /// <summary>Gets whether the second status slot shows the icon of an unavailable repository.</summary>
    public virtual bool ShowsUnavailableIcon => false;

    /// <summary>Gets whether the tree shows the text of the node in the muted color, as for a paused node.</summary>
    public virtual bool IsMuted => false;

    /// <summary>
    /// Gets the name of the status the icons of the node show, for their tooltip and UI Automation name, or
    /// <see langword="null"/> when the node shows no status icon.
    /// </summary>
    public virtual string? StatusName => null;

    /// <summary>
    /// Reports the new selection state to the owner of the tree.
    /// </summary>
    /// <param name="value">Whether the node is selected.</param>
    partial void OnIsSelectedChanged(bool value) => selectionChanged(this, value);
}
