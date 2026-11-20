using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using GitHubBackup.Core.BackupConfiguration;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// One standalone repository in the settings editor: its URL, branch, archive folder, pause and status can be
/// changed, its id is kept as read.
/// </summary>
/// <param name="source">The entry as read, or a new entry.</param>
/// <param name="edited">Called after the user changes a field.</param>
public sealed partial class RepositorySettingsViewModel(RepositoryConfig source, Action edited) : SettingsFormViewModel
{
    /// <summary>The action that tells the editor about a change.</summary>
    private readonly Action _edited = edited;

    /// <summary>The GitHub id, written back unchanged; <c>0</c> for a new entry, resolved by name on the next run.</summary>
    private readonly long _id = source.Id;

    /// <summary>The folder of the user's own clone, written back unchanged; the editor does not show it.</summary>
    private readonly string? _clonePath = source.ClonePath;

    /// <summary>The repository URL, <c>https://github.com/&lt;owner&gt;/&lt;repo&gt;</c>.</summary>
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(SettingsFormViewModel), nameof(ValidateText))]
    private string _url = source.Url;

    /// <summary>The branch checked out in the clone; empty selects the default branch.</summary>
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(SettingsFormViewModel), nameof(ValidateBranch))]
    private string _branch = source.Branch ?? string.Empty;

    /// <summary>The archive folder; empty selects <c>&lt;backupRoot&gt;\&lt;owner&gt;\&lt;repo&gt;</c>.</summary>
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(SettingsFormViewModel), nameof(ValidateOptionalPath))]
    private string _path = source.Path ?? string.Empty;

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
    /// <returns>The entry with the id and clone folder as read.</returns>
    internal RepositoryConfig ToConfig() => new()
    {
        Url = Url.Trim(),
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
    /// <param name="location">The location of the entry, such as <c>repositories[0]</c>.</param>
    /// <param name="fields">The fields by location.</param>
    internal void AddFields(string location, Dictionary<string, SettingsField> fields)
    {
        fields[location] = new(this, nameof(Path));
        fields[location + ".url"] = new(this, nameof(Url));
        fields[location + ".branch"] = new(this, nameof(Branch));
        fields[location + ".path"] = new(this, nameof(Path));
    }

    /// <summary>Tells the editor that the URL changed.</summary>
    /// <param name="value">The new URL.</param>
    partial void OnUrlChanged(string value) => _edited();

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
