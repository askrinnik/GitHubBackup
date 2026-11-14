using GitHubBackup.App.Dialogs;
using GitHubBackup.App.Navigation;
using GitHubBackup.App.Shell;
using GitHubBackup.App.ViewModels;
using GitHubBackup.Core.History;
using GitHubBackup.Core.Overview;
using GitHubBackup.Core.Security;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GitHubBackup.App.Tests.ViewModels;

/// <summary>
/// Tests <see cref="HistoryViewModel"/>: reading both tabs and returning to the repository tree.
/// </summary>
public sealed class HistoryViewModelTests
{
    private readonly IRunHistoryStore _store = Substitute.For<IRunHistoryStore>();

    private readonly IRepositoryOverviewProvider _overviewProvider = Substitute.For<IRepositoryOverviewProvider>();

    private readonly INavigationService _navigation = Substitute.For<INavigationService>();

    private readonly HistoryViewModel _viewModel;

    /// <summary>
    /// Creates the ViewModel with both tabs over substituted sources that hold nothing.
    /// </summary>
    public HistoryViewModelTests()
    {
        _store.QueryAsync(Arg.Any<RunHistoryQuery>(), Arg.Any<CancellationToken>()).Returns([]);
        _overviewProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(new RepositoryOverview([], []));
        var masker = new SecretMasker();
        var runs = new RunHistoryViewModel(_store, masker, TimeProvider.System, NullLogger<RunHistoryViewModel>.Instance);
        var archives = new ArchiveHistoryViewModel(
            _overviewProvider,
            Substitute.For<IArchiveHistoryProvider>(),
            Substitute.For<IExplorerLauncher>(),
            Substitute.For<IDialogService>(),
            masker,
            NullLogger<ArchiveHistoryViewModel>.Instance);
        _viewModel = new(runs, archives, _navigation);
    }

    [Fact]
    public async Task Load_ReadsRunsAndRepositories()
    {
        await _viewModel.LoadCommand.ExecuteAsync(null);

        await _store.Received(1).QueryAsync(RunHistoryQuery.All, Arg.Any<CancellationToken>());
        await _overviewProvider.Received(1).LoadAsync(Arg.Any<CancellationToken>());
        _viewModel.Runs.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public async Task Load_RunHistoryFails_StillReadsRepositories()
    {
        _store.QueryAsync(Arg.Any<RunHistoryQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RunHistoryException(@"C:\app\data\history.db", "broken"));

        await _viewModel.LoadCommand.ExecuteAsync(null);

        _viewModel.Runs.HasLoadError.ShouldBeTrue();
        await _overviewProvider.Received(1).LoadAsync(Arg.Any<CancellationToken>());
        _viewModel.Archives.HasRepositoriesError.ShouldBeFalse();
    }

    [Fact]
    public void Back_NavigatesToRepositories()
    {
        _viewModel.BackCommand.Execute(null);

        _navigation.Received(1).NavigateTo<RepositoriesViewModel>();
    }
}
