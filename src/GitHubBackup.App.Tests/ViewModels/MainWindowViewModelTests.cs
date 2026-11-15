using CommunityToolkit.Mvvm.ComponentModel;
using GitHubBackup.App.Dialogs;
using GitHubBackup.App.Navigation;
using GitHubBackup.App.Runs;
using GitHubBackup.App.Shell;
using GitHubBackup.App.ViewModels;
using GitHubBackup.Core.Overview;
using GitHubBackup.Core.Security;
using GitHubBackup.Core.Ui;
using GitHubBackup.Infrastructure.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GitHubBackup.App.Tests.ViewModels;

/// <summary>
/// Tests <see cref="MainWindowViewModel"/>: the first navigation, following the current ViewModel, closing and the
/// placement of the window.
/// </summary>
public sealed class MainWindowViewModelTests
{
    private static readonly WindowPlacement _saved = new(100, 50, 1000, 700, false);

    private readonly INavigationService _navigation = Substitute.For<INavigationService>();

    private readonly IUiSettingsStore _store = Substitute.For<IUiSettingsStore>();

    private readonly IMonitorLayout _monitors = Substitute.For<IMonitorLayout>();

    private readonly UiPreferences _preferences;

    /// <summary>
    /// Creates the preferences of the window over a substitute store.
    /// </summary>
    public MainWindowViewModelTests() =>
        _preferences = new(_store, Substitute.For<IThemeService>(), NullLogger<UiPreferences>.Instance);

    [Fact]
    public void Constructor_NavigatesToRepositories()
    {
        _ = CreateViewModel();

        _navigation.Received(1).NavigateTo<RepositoriesViewModel>();
    }

    [Fact]
    public void NavigationChanged_UpdatesCurrentViewModelAndRaisesPropertyChanged()
    {
        var viewModel = CreateViewModel();
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);
        var repositories = new RepositoriesViewModel(
            Substitute.For<IRepositoryOverviewProvider>(),
            Substitute.For<IBackupRunService>(),
            Substitute.For<IUiDispatcher>(),
            Substitute.For<ILiveLogFeed>(),
            Substitute.For<IDialogService>(),
            new SecretMasker(),
            Substitute.For<IHostEnvironment>(),
            TimeProvider.System,
            _navigation,
            _preferences,
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
        var viewModel = CreateViewModel();
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
        var viewModel = CreateViewModel();

        var canClose = await viewModel.CanCloseAsync();

        canClose.ShouldBeTrue();
    }

    [Fact]
    public void GetStartupPlacement_NothingSaved_ReturnsDefaultPlacement()
    {
        var placement = CreateViewModel().GetStartupPlacement();

        placement.ShouldBe(new StartupPlacement(null, false));
        _monitors.DidNotReceiveWithAnyArgs().IsOnAnyMonitor(default!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetStartupPlacement_SavedOnMonitor_ReturnsSavedBoundsAndState(bool isMaximized)
    {
        var saved = _saved with { IsMaximized = isMaximized };
        _preferences.MainWindowPlacement = saved;
        _monitors.IsOnAnyMonitor(saved).Returns(true);

        var placement = CreateViewModel().GetStartupPlacement();

        placement.ShouldBe(new StartupPlacement(saved, isMaximized));
    }

    [Fact]
    public void GetStartupPlacement_SavedOffScreen_ReturnsDefaultPlacement()
    {
        _preferences.MainWindowPlacement = _saved with { Left = -50000 };
        _monitors.IsOnAnyMonitor(Arg.Any<WindowPlacement>()).Returns(false);

        var placement = CreateViewModel().GetStartupPlacement();

        placement.ShouldBe(new StartupPlacement(null, false));
    }

    [Fact]
    public void GetStartupPlacement_SavedOffScreenMaximized_ReturnsDefaultBoundsMaximized()
    {
        _preferences.MainWindowPlacement = _saved with { Left = -50000, IsMaximized = true };
        _monitors.IsOnAnyMonitor(Arg.Any<WindowPlacement>()).Returns(false);

        var placement = CreateViewModel().GetStartupPlacement();

        placement.ShouldBe(new StartupPlacement(null, true));
    }

    [Fact]
    public async Task RecordPlacement_ThenSave_WritesPlacementWithPanelHeights()
    {
        var viewModel = CreateViewModel();
        _preferences.LogPanelHeight = 240;
        _preferences.SummaryPanelHeight = 300;

        viewModel.RecordPlacement(new(10, 20, 1200, 800, true));
        await _preferences.SaveAsync(TestContext.Current.CancellationToken);

        await _store.Received(1).SaveAsync(
            new UiSettings(UiTheme.System, new(10, 20, 1200, 800, true), 240, 300), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RecordPlacement_InvalidBounds_KeepsPreviousPlacement()
    {
        _preferences.MainWindowPlacement = _saved;

        CreateViewModel().RecordPlacement(new(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity, false));

        _preferences.MainWindowPlacement.ShouldBe(_saved);
    }

    /// <summary>
    /// Creates the subject over the navigation, the preferences and the monitor layout of the test.
    /// </summary>
    /// <returns>The subject.</returns>
    private MainWindowViewModel CreateViewModel() => new(_navigation, _preferences, _monitors);

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
