using CommunityToolkit.Mvvm.ComponentModel;
using GitHubBackup.App.Dialogs;
using GitHubBackup.App.Navigation;
using GitHubBackup.App.Runs;
using GitHubBackup.App.ViewModels;
using GitHubBackup.Core.Overview;
using GitHubBackup.Core.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GitHubBackup.App.Tests.ViewModels;

/// <summary>
/// Tests <see cref="MainWindowViewModel"/>: the first navigation, following the current ViewModel and closing.
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
            Substitute.For<IHostEnvironment>(),
            TimeProvider.System,
            NullLogger<RepositoriesViewModel>.Instance);
        _navigation.CurrentViewModel.Returns(repositories);

        _navigation.CurrentViewModelChanged += Raise.Event();

        viewModel.CurrentViewModel.ShouldBeSameAs(repositories);
        changedProperties.ShouldBe([nameof(MainWindowViewModel.CurrentViewModel)]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CanClose_CurrentViewModelIsCloseGuard_ReturnsItsAnswer(bool answer)
    {
        var viewModel = new MainWindowViewModel(_navigation);
        var guarded = new GuardedViewModel(answer);
        _navigation.CurrentViewModel.Returns(guarded);
        _navigation.CurrentViewModelChanged += Raise.Event();

        var canClose = await viewModel.CanCloseAsync();

        canClose.ShouldBe(answer);
        guarded.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task CanClose_NoCurrentViewModel_ReturnsTrue()
    {
        var viewModel = new MainWindowViewModel(_navigation);

        var canClose = await viewModel.CanCloseAsync();

        canClose.ShouldBeTrue();
    }

    /// <summary>
    /// A ViewModel that answers whether the window may close with a fixed value and counts the questions.
    /// </summary>
    /// <param name="answer">The answer to every question.</param>
    private sealed class GuardedViewModel(bool answer) : ObservableObject, ICloseGuard
    {
        /// <summary>Gets how many times the window asked.</summary>
        public int Calls { get; private set; }

        /// <inheritdoc />
        public Task<bool> CanCloseAsync()
        {
            Calls++;
            return Task.FromResult(answer);
        }
    }
}
