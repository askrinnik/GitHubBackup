using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHubBackup.Core.BackupConfiguration;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// One tracked account in the settings editor: its URL, archive folder, exclusion masks and the confirmed
/// repositories, which can be edited or removed but not added by hand.
/// </summary>
/// <param name="source">The entry as read.</param>
/// <param name="edited">Called after the user changes a field or removes a repository.</param>
public sealed partial class AccountSettingsViewModel(AccountConfig source, Action edited) : SettingsFormViewModel
{
    /// <summary>The name shown in the list of accounts while the URL is empty.</summary>
    public const string NewAccountName = "(new account)";

    /// <summary>The action that tells the editor about a change.</summary>
    private readonly Action _edited = edited;

    /// <summary>The account URL, <c>https://github.com/&lt;login&gt;</c>.</summary>
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [CustomValidation(typeof(SettingsFormViewModel), nameof(ValidateText))]
    private string _url = source.Url;

    /// <summary>The folder that replaces <c>&lt;backupRoot&gt;\&lt;owner&gt;</c>; empty keeps the default.</summary>
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(SettingsFormViewModel), nameof(ValidateOptionalPath))]
    private string _path = source.Path ?? string.Empty;

    /// <summary>The exclusion masks of the account, separated by commas.</summary>
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(SettingsFormViewModel), nameof(ValidateText))]
    private string _exclude = FormatMasks(source.Exclude);

    /// <summary>Whether an unfiltered run skips the account and all its repositories (<c>paused</c>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusName))]
    private bool _isPaused = source.Paused;

    /// <summary>
    /// Whether a field of the account or of one of its repositories shows an error, as of the last
    /// <see cref="RefreshHasAnyErrors"/>.
    /// </summary>
    [ObservableProperty]
    private bool _hasAnyErrors;

    /// <summary>Gets the confirmed repositories of the account, in file order.</summary>
    public ObservableCollection<AccountRepositorySettingsViewModel> Repositories { get; } = [.. source.Repositories.Select(repository => new AccountRepositorySettingsViewModel(repository, edited))];

    /// <summary>Gets the repositories of the account whose name contains the filter of the table.</summary>
    public FilteredItems<AccountRepositorySettingsViewModel> RepositoryView => field ??= new(Repositories, repository => repository.Name);

    /// <summary>Gets the name shown in the list of accounts: the URL, or <see cref="NewAccountName"/> while it is empty.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Url) ? NewAccountName : Url.Trim();

    /// <summary>Gets the name of the status icon in the list of accounts: active or paused.</summary>
    public string StatusName => IsPaused ? TreeNodeViewModel.PausedStatusName : TreeNodeViewModel.ActiveStatusName;

    /// <summary>
    /// Updates <see cref="HasAnyErrors"/> from the errors shown at the fields of the account and its repositories.
    /// </summary>
    internal void RefreshHasAnyErrors() => HasAnyErrors = HasErrors || Repositories.Any(repository => repository.HasErrors);

    /// <summary>
    /// Creates the entry of the configuration that the row describes.
    /// </summary>
    /// <returns>The entry with its repositories.</returns>
    internal AccountConfig ToConfig() => new()
    {
        Url = Url.Trim(),
        Path = NullIfBlank(Path),
        Exclude = ParseMasks(Exclude),
        Paused = IsPaused,
        Repositories = [.. Repositories.Select(repository => repository.ToConfig())],
    };

    /// <summary>
    /// Adds the fields of the account and its repositories to <paramref name="fields"/>, the entry itself showing its
    /// errors at the path.
    /// </summary>
    /// <param name="location">The location of the account, such as <c>accounts[0]</c>.</param>
    /// <param name="fields">The fields by location.</param>
    internal void AddFields(string location, Dictionary<string, SettingsField> fields)
    {
        fields[location] = new(this, nameof(Path));
        fields[location + ".url"] = new(this, nameof(Url));
        fields[location + ".path"] = new(this, nameof(Path));
        fields[location + ".exclude"] = new(this, nameof(Exclude));
        for (var index = 0; index < Repositories.Count; index++)
        {
            Repositories[index].AddFields($"{location}.repositories[{index}]", fields);
        }
    }

    /// <summary>
    /// Removes a repository from the account.
    /// </summary>
    /// <param name="repository">The repository to remove.</param>
    [RelayCommand]
    private void RemoveRepository(AccountRepositorySettingsViewModel? repository)
    {
        if (repository is not null && Repositories.Remove(repository))
        {
            _edited();
        }
    }

    /// <summary>Tells the editor that the URL changed.</summary>
    /// <param name="value">The new URL.</param>
    partial void OnUrlChanged(string value) => _edited();

    /// <summary>Tells the editor that the archive folder changed.</summary>
    /// <param name="value">The new folder.</param>
    partial void OnPathChanged(string value) => _edited();

    /// <summary>Tells the editor that the exclusion masks changed.</summary>
    /// <param name="value">The new masks.</param>
    partial void OnExcludeChanged(string value) => _edited();

    /// <summary>Tells the editor that the pause changed.</summary>
    /// <param name="value">The new pause.</param>
    partial void OnIsPausedChanged(bool value) => _edited();
}
