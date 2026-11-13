using GitHubBackup.App.Dialogs;
using GitHubBackup.App.Navigation;
using GitHubBackup.App.Runs;
using GitHubBackup.App.ViewModels;
using GitHubBackup.Core.Overview;
using GitHubBackup.Core.Security;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GitHubBackup.App.Tests.ViewModels;

/// <summary>
/// Tests <see cref="MainWindowViewModel"/>: the first navigation and following the current ViewModel.
/// </summary>
public sealed class MainWindowViewModelTests
{
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();

    [Fact]
    public void Constructor_NavigatesToRepositories()
    {
        _ = new MainWindowViewModel(_navigation);

        _navigation.Received(1).NavigateTo<RepositoriesViewModel>();
    }

    [Fact]
    public void NavigationChanged_UpdatesCurrentViewModelAndRaisesPropertyChanged()
    {
        var viewModel = new MainWindowViewModel(_navigation);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);
        var repositories = new RepositoriesViewModel(
            Substitute.For<IRepositoryOverviewProvider>(),
            Substitute.For<IBackupRunService>(),
            Substitute.For<IDialogService>(),
            new SecretMasker(),
            NullLogger<RepositoriesViewModel>.Instance);
        _navigation.CurrentViewModel.Returns(repositories);

        _navigation.CurrentViewModelChanged += Raise.Event();

        viewModel.CurrentViewModel.ShouldBeSameAs(repositories);
        changedProperties.ShouldBe([nameof(MainWindowViewModel.CurrentViewModel)]);
    }
}
