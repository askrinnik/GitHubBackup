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
    /// <summary>The action that tells the editor about a change.</summary>
    private readonly Action _edited = edited;

    /// <summary>The account URL, <c>https://github.com/&lt;login&gt;</c>.</summary>
    [ObservableProperty]
    [NotifyDataErrorInfo]
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

    /// <summary>Gets the confirmed repositories of the account, in file order.</summary>
    public ObservableCollection<AccountRepositorySettingsViewModel> Repositories { get; } = [.. source.Repositories.Select(repository => new AccountRepositorySettingsViewModel(repository, edited))];

    /// <summary>
    /// Creates the entry of the configuration that the row describes.
    /// </summary>
    /// <returns>The entry.</returns>
    internal AccountConfig ToConfig() => new()
    {
        Url = Url.Trim(),
        Path = NullIfBlank(Path),
        Exclude = ParseMasks(Exclude),
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
}
