using CommunityToolkit.Mvvm.ComponentModel;

namespace GitHubBackup.App.Navigation;

/// <summary>
/// Holds the ViewModel of the view shown in the main window and switches it, so that ViewModels navigate without
/// knowing the views.
/// </summary>
public interface INavigationService
{
    /// <summary>Gets the ViewModel of the current view, or <see langword="null"/> before the first navigation.</summary>
    ObservableObject? CurrentViewModel { get; }

    /// <summary>Occurs after <see cref="CurrentViewModel"/> has changed.</summary>
    event EventHandler? CurrentViewModelChanged;

    /// <summary>
    /// Makes a new or shared instance of <typeparamref name="TViewModel"/>, as its registration decides, the current
    /// ViewModel.
    /// </summary>
    /// <typeparam name="TViewModel">The ViewModel of the target view.</typeparam>
    /// <exception cref="InvalidOperationException"><typeparamref name="TViewModel"/> is not registered.</exception>
    void NavigateTo<TViewModel>()
        where TViewModel : ObservableObject;
}
