using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using GitHubBackup.Core.BackupConfiguration;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// One repository of a tracked account in the settings editor: its branch, archive folder, pause and status can be
/// changed, its name and id are kept as read.
/// </summary>
/// <param name="source">The entry as read.</param>
/// <param name="edited">Called after the user changes a field.</param>
public sealed partial class AccountRepositorySettingsViewModel(AccountRepositoryConfig source, Action edited) : SettingsFormViewModel
{
    /// <summary>The action that tells the editor about a change.</summary>
    private readonly Action _edited = edited;

    /// <summary>The GitHub id, written back unchanged.</summary>
    private readonly long _id = source.Id;

    /// <summary>The folder of the user's own clone, written back unchanged; the editor does not show it.</summary>
    private readonly string? _clonePath = source.ClonePath;

    /// <summary>The branch checked out in the clone; empty selects the default branch.</summary>
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(SettingsFormViewModel), nameof(ValidateBranch))]
    private string _branch = source.Branch ?? string.Empty;

    /// <summary>The archive folder; empty keeps the folder derived from the account.</summary>
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(SettingsFormViewModel), nameof(ValidateOptionalPath))]
    private string _path = source.Path ?? string.Empty;

    /// <summary>Gets the repository name, without the owner.</summary>
    public string Name { get; } = source.Name;

    /// <summary>Whether an unfiltered run skips the repository (<c>paused</c>).</summary>
    [ObservableProperty]
    private bool _isPaused = source.Paused;

    /// <summary>
    /// Whether the repository is marked as not found on GitHub: the <see cref="RepositoryStatus.Unavailable"/> status,
    /// <see cref="RepositoryStatus.Active"/> otherwise. A run that finds the repository again sets the status anew.
    /// </summary>
    [ObservableProperty]
    private bool _isUnavailable = source.Status == RepositoryStatus.Unavailable;

    /// <summary>
    /// Creates the entry of the configuration that the row describes.
    /// </summary>
    /// <returns>The entry with the name, id and clone folder as read.</returns>
    internal AccountRepositoryConfig ToConfig() => new()
    {
        Name = Name,
        Id = _id,
        ClonePath = _clonePath,
        Status = IsUnavailable ? RepositoryStatus.Unavailable : RepositoryStatus.Active,
        Paused = IsPaused,
        Branch = NullIfBlank(Branch),
        Path = NullIfBlank(Path),
    };

    /// <summary>
    /// Adds the fields of the row to <paramref name="fields"/>, the entry itself showing its errors at the path.
    /// </summary>
    /// <param name="location">The location of the entry, such as <c>accounts[0].repositories[1]</c>.</param>
    /// <param name="fields">The fields by location.</param>
    internal void AddFields(string location, Dictionary<string, SettingsField> fields)
    {
        fields[location] = new(this, nameof(Path));
        fields[location + ".branch"] = new(this, nameof(Branch));
        fields[location + ".path"] = new(this, nameof(Path));
    }

    /// <summary>Tells the editor that the branch changed.</summary>
    /// <param name="value">The new branch.</param>
    partial void OnBranchChanged(string value) => _edited();

    /// <summary>Tells the editor that the archive folder changed.</summary>
    /// <param name="value">The new folder.</param>
    partial void OnPathChanged(string value) => _edited();

    /// <summary>Tells the editor that the pause changed.</summary>
    /// <param name="value">The new pause.</param>
    partial void OnIsPausedChanged(bool value) => _edited();

    /// <summary>Tells the editor that the status changed.</summary>
    /// <param name="value">Whether the repository is now marked as unavailable.</param>
    partial void OnIsUnavailableChanged(bool value) => _edited();
}
