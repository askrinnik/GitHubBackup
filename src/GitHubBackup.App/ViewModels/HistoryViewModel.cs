using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHubBackup.App.Navigation;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Backs the history screen: the run history on one tab and the archive history of a repository on the other.
/// </summary>
/// <param name="runs">The list of runs with their details.</param>
/// <param name="archives">The archive history of the selected repository.</param>
/// <param name="navigation">Returns to the repository tree.</param>
public sealed partial class HistoryViewModel(
    RunHistoryViewModel runs,
    ArchiveHistoryViewModel archives,
    INavigationService navigation) : ObservableObject
{
    /// <summary>Gets the list of runs with their details.</summary>
    public RunHistoryViewModel Runs { get; } = runs;

    /// <summary>Gets the archive history of the selected repository.</summary>
    public ArchiveHistoryViewModel Archives { get; } = archives;

    /// <summary>
    /// Reads the runs and the repositories; each tab shows its own error.
    /// </summary>
    /// <returns>A task that completes when both are read.</returns>
    [RelayCommand]
    private async Task LoadAsync()
    {
        await Runs.RefreshCommand.ExecuteAsync(null);
        await Archives.LoadRepositoriesCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Returns to the repository tree.
    /// </summary>
    [RelayCommand]
    private void Back() => navigation.NavigateTo<RepositoriesViewModel>();
}
