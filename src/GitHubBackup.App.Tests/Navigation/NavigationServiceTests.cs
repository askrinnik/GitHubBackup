using CommunityToolkit.Mvvm.ComponentModel;
using GitHubBackup.App.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace GitHubBackup.App.Tests.Navigation;

/// <summary>
/// Tests <see cref="NavigationService"/>: resolving the target ViewModel and announcing the change.
/// </summary>
public sealed class NavigationServiceTests : IDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection().AddTransient<TargetViewModel>().BuildServiceProvider();

    [Fact]
    public void CurrentViewModel_BeforeNavigation_IsNull()
    {
        var navigation = new NavigationService(_services);

        navigation.CurrentViewModel.ShouldBeNull();
    }

    [Fact]
    public void NavigateTo_RegisteredViewModel_SetsCurrentViewModel()
    {
        var navigation = new NavigationService(_services);

        navigation.NavigateTo<TargetViewModel>();

        navigation.CurrentViewModel.ShouldBeOfType<TargetViewModel>();
    }

    [Fact]
    public void NavigateTo_RegisteredViewModel_RaisesCurrentViewModelChanged()
    {
        var navigation = new NavigationService(_services);
        ObservableObject? announced = null;
        navigation.CurrentViewModelChanged += (_, _) => announced = navigation.CurrentViewModel;

        navigation.NavigateTo<TargetViewModel>();

        announced.ShouldBeOfType<TargetViewModel>();
    }

    [Fact]
    public void NavigateTo_UnregisteredViewModel_Throws()
    {
        var navigation = new NavigationService(_services);

        Should.Throw<InvalidOperationException>(navigation.NavigateTo<UnregisteredViewModel>);
        navigation.CurrentViewModel.ShouldBeNull();
    }

    /// <summary>
    /// Disposes the service provider.
    /// </summary>
    public void Dispose() => _services.Dispose();

    /// <summary>
    /// A ViewModel registered in the service provider of the tests.
    /// </summary>
    private sealed class TargetViewModel : ObservableObject;

    /// <summary>
    /// A ViewModel that the service provider of the tests does not know.
    /// </summary>
    private sealed class UnregisteredViewModel : ObservableObject;
}
