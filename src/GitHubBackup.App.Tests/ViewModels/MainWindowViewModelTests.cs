using GitHubBackup.App.Navigation;
using GitHubBackup.App.ViewModels;
using NSubstitute;

namespace GitHubBackup.App.Tests.ViewModels;

/// <summary>
/// Tests <see cref="MainWindowViewModel"/>: the first navigation and following the current ViewModel.
/// </summary>
public sealed class MainWindowViewModelTests
{
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();

    [Fact]
    public void Constructor_NavigatesToHome()
    {
        _ = new MainWindowViewModel(_navigation);

        _navigation.Received(1).NavigateTo<HomeViewModel>();
    }

    [Fact]
    public void NavigationChanged_UpdatesCurrentViewModelAndRaisesPropertyChanged()
    {
        var viewModel = new MainWindowViewModel(_navigation);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);
        var home = new HomeViewModel();
        _navigation.CurrentViewModel.Returns(home);

        _navigation.CurrentViewModelChanged += Raise.Event();

        viewModel.CurrentViewModel.ShouldBeSameAs(home);
        changedProperties.ShouldBe([nameof(MainWindowViewModel.CurrentViewModel)]);
    }
}
