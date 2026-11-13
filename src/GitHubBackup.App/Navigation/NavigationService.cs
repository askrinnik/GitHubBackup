using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace GitHubBackup.App.Navigation;

/// <summary>
/// Resolves the target ViewModel from the service provider of the host.
/// </summary>
/// <remarks>
/// The provider is used here as the factory of the composition root: it creates ViewModels with their constructor
/// dependencies, while ViewModels themselves receive only <see cref="INavigationService"/> and never the provider.
/// </remarks>
/// <param name="services">The service provider of the host.</param>
internal sealed class NavigationService(IServiceProvider services) : INavigationService
{
    /// <inheritdoc />
    public ObservableObject? CurrentViewModel { get; private set; }

    /// <inheritdoc />
    public event EventHandler? CurrentViewModelChanged;

    /// <inheritdoc />
    public void NavigateTo<TViewModel>()
        where TViewModel : ObservableObject
    {
        CurrentViewModel = services.GetRequiredService<TViewModel>();
        CurrentViewModelChanged?.Invoke(this, EventArgs.Empty);
    }
}
