using CommunityToolkit.Mvvm.ComponentModel;
using GitHubBackup.App.Navigation;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Backs the main window: exposes the ViewModel of the view that the window shows.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    /// <summary>The navigation whose current ViewModel the window shows.</summary>
    private readonly INavigationService _navigation;

    /// <summary>The ViewModel of the view shown in the window; follows <see cref="INavigationService.CurrentViewModel"/>.</summary>
    [ObservableProperty]
    private ObservableObject? _currentViewModel;

    /// <summary>
    /// Subscribes to the navigation and opens the repository tree.
    /// </summary>
    /// <param name="navigation">The navigation of the main window.</param>
    public MainWindowViewModel(INavigationService navigation)
    {
        _navigation = navigation;
        _navigation.CurrentViewModelChanged += OnNavigationChanged;
        _navigation.NavigateTo<RepositoriesViewModel>();
    }

    /// <summary>
    /// Returns whether the window may close, asking the current ViewModel when it is an <see cref="ICloseGuard"/>.
    /// </summary>
    /// <returns>A task that returns <see langword="true"/> when the window may close.</returns>
    public Task<bool> CanCloseAsync() =>
        CurrentViewModel is ICloseGuard guard ? guard.CanCloseAsync() : Task.FromResult(true);

    /// <summary>
    /// Copies the current ViewModel of the navigation.
    /// </summary>
    /// <param name="sender">The navigation.</param>
    /// <param name="e">No data.</param>
    private void OnNavigationChanged(object? sender, EventArgs e) => CurrentViewModel = _navigation.CurrentViewModel;
}
