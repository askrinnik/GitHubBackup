using CommunityToolkit.Mvvm.ComponentModel;
using GitHubBackup.App.Navigation;
using GitHubBackup.App.Shell;
using GitHubBackup.Core.Ui;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Backs the main window: exposes the ViewModel of the view that the window shows and decides where the window opens
/// and what placement it leaves for the next start.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    /// <summary>The navigation whose current ViewModel the window shows.</summary>
    private readonly INavigationService _navigation;

    /// <summary>The preferences that hold the placement of the window.</summary>
    private readonly UiPreferences _preferences;

    /// <summary>Tells whether a saved placement is still on a connected monitor.</summary>
    private readonly IMonitorLayout _monitors;

    /// <summary>The ViewModel of the view shown in the window; follows <see cref="INavigationService.CurrentViewModel"/>.</summary>
    [ObservableProperty]
    private ObservableObject? _currentViewModel;

    /// <summary>
    /// Subscribes to the navigation and opens the repository tree.
    /// </summary>
    /// <param name="navigation">The navigation of the main window.</param>
    /// <param name="preferences">The preferences that hold the placement of the window.</param>
    /// <param name="monitors">Tells whether a saved placement is still on a connected monitor.</param>
    public MainWindowViewModel(INavigationService navigation, UiPreferences preferences, IMonitorLayout monitors)
    {
        _navigation = navigation;
        _preferences = preferences;
        _monitors = monitors;
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
    /// Returns how the window opens: at the saved placement, or at the default one when nothing is saved or the
    /// saved bounds overlap no connected monitor.
    /// </summary>
    /// <remarks>
    /// A window saved maximized opens maximized even when its bounds are lost; at the default position it maximizes
    /// on the primary monitor.
    /// </remarks>
    /// <returns>The placement to apply before the window is shown.</returns>
    public StartupPlacement GetStartupPlacement()
    {
        if (_preferences.MainWindowPlacement is not { } saved)
        {
            return new(null, false);
        }

        return _monitors.IsOnAnyMonitor(saved) ? new(saved, saved.IsMaximized) : new(null, saved.IsMaximized);
    }

    /// <summary>
    /// Keeps the placement of the closing window for the next start; invalid bounds leave the previous placement.
    /// </summary>
    /// <param name="placement">The normal bounds of the window and whether it is maximized.</param>
    public void RecordPlacement(WindowPlacement placement)
    {
        if (placement.IsValid())
        {
            _preferences.MainWindowPlacement = placement;
        }
    }

    /// <summary>
    /// Copies the current ViewModel of the navigation.
    /// </summary>
    /// <param name="sender">The navigation.</param>
    /// <param name="e">No data.</param>
    private void OnNavigationChanged(object? sender, EventArgs e) => CurrentViewModel = _navigation.CurrentViewModel;
}
