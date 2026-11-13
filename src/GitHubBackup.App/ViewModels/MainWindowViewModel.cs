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
    /// Subscribes to the navigation and opens the home view.
    /// </summary>
    /// <param name="navigation">The navigation of the main window.</param>
    public MainWindowViewModel(INavigationService navigation)
    {
        _navigation = navigation;
        _navigation.CurrentViewModelChanged += OnNavigationChanged;
        _navigation.NavigateTo<HomeViewModel>();
    }

    /// <summary>
    /// Copies the current ViewModel of the navigation.
    /// </summary>
    /// <param name="sender">The navigation.</param>
    /// <param name="e">No data.</param>
    private void OnNavigationChanged(object? sender, EventArgs e) => CurrentViewModel = _navigation.CurrentViewModel;
}
