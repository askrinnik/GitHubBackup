using GitHubBackup.App.ViewModels;
using GitHubBackup.Core;
using GitHubBackup.Core.History;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.Security;
using GitHubBackup.Core.State;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace GitHubBackup.App.Tests.ViewModels;

/// <summary>
/// Tests <see cref="RunHistoryViewModel"/>: the list of runs, its filters, the details of a run and the errors.
/// </summary>
public sealed class RunHistoryViewModelTests
{
    private const int _runHistoryLoadFailedEventId = 9;

    private const string _token = "ghp_HistoryTokenValue0123456789abcdefABC";

    private static readonly DateTimeOffset _startedAt = new(2026, 11, 12, 10, 15, 30, TimeSpan.FromHours(3));

    private readonly IRunHistoryStore _store = Substitute.For<IRunHistoryStore>();

    private readonly SecretMasker _masker = new();

    private readonly FakeLogger<RunHistoryViewModel> _logger = new();

    private readonly RunHistoryViewModel _viewModel;

    private readonly List<RunHistoryQuery> _queries = [];

    private Func<Task<IReadOnlyList<RunHistoryEntry>>> _respond = () => Task.FromResult<IReadOnlyList<RunHistoryEntry>>([]);

    /// <summary>
    /// Creates the ViewModel in the time zone UTC+3 over a store that records every query and returns no runs.
    /// </summary>
    public RunHistoryViewModelTests()
    {
        _store.QueryAsync(Arg.Do<RunHistoryQuery>(_queries.Add), Arg.Any<CancellationToken>()).Returns(_ => _respond());
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        timeProvider.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3"));
        _viewModel = new(_store, _masker, timeProvider, _logger);
    }

    [Fact]
    public async Task Refresh_Runs_ShowsRowsInStoreOrder()
    {
        var newer = CreateRun(_startedAt.AddHours(1));
        var older = CreateRun(_startedAt);
        UseRuns(newer, older);

        await _viewModel.RefreshCommand.ExecuteAsync(null);

        _viewModel.Runs.Select(row => row.RunId).ShouldBe([newer.RunId, older.RunId]);
        _viewModel.IsEmpty.ShouldBeFalse();
        _viewModel.LoadError.ShouldBeNull();
        _queries.ShouldHaveSingleItem().ShouldBe(RunHistoryQuery.All);
    }

    [Fact]
    public async Task Refresh_NoRuns_IsEmpty()
    {
        await _viewModel.RefreshCommand.ExecuteAsync(null);

        _viewModel.Runs.ShouldBeEmpty();
        _viewModel.IsEmpty.ShouldBeTrue();
        RunHistoryViewModel.NoRunsMessage.ShouldBe("No runs");
    }

    [Fact]
    public async Task Refresh_StoreFails_ShowsMaskedErrorAndLogs()
    {
        _masker.Register(_token);
        _respond = () => Task.FromException<IReadOnlyList<RunHistoryEntry>>(
            new RunHistoryException(@"C:\app\data\history.db", $"The runs cannot be read. {_token}"));

        await _viewModel.RefreshCommand.ExecuteAsync(null);

        _viewModel.Runs.ShouldBeEmpty();
        _viewModel.IsEmpty.ShouldBeFalse();
        _viewModel.HasLoadError.ShouldBeTrue();
        _viewModel.LoadError.ShouldBe(@"The run history database 'C:\app\data\history.db' cannot be used: The runs cannot be read. ***");
        var record = _logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Id.Id.ShouldBe(_runHistoryLoadFailedEventId);
        record.Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public async Task Refresh_AfterError_ClearsError()
    {
        var calls = 0;
        _respond = () => ++calls == 1
            ? Task.FromException<IReadOnlyList<RunHistoryEntry>>(new RunHistoryException(@"C:\db", "broken"))
            : Task.FromResult<IReadOnlyList<RunHistoryEntry>>([]);
        await _viewModel.RefreshCommand.ExecuteAsync(null);

        await _viewModel.RefreshCommand.ExecuteAsync(null);

        _viewModel.LoadError.ShouldBeNull();
        _viewModel.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void StatusFilter_Changed_QueriesExitCode()
    {
        _viewModel.SelectedStatus = RunHistoryViewModel.StatusOptions.Single(option => option.Value == ExitCode.Critical);

        _queries.ShouldHaveSingleItem().ExitCode.ShouldBe(ExitCode.Critical);
    }

    [Fact]
    public void ModeFilter_Changed_QueriesMode()
    {
        _viewModel.SelectedMode = RunHistoryViewModel.ModeOptions.Single(option => option.Value == RunMode.Silent);

        _queries.ShouldHaveSingleItem().Mode.ShouldBe(RunMode.Silent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DryRunFilter_Changed_QueriesFlag(bool isDryRun)
    {
        _viewModel.SelectedDryRun = RunHistoryViewModel.DryRunOptions.Single(option => option.Value == isDryRun);

        _queries.ShouldHaveSingleItem().IsDryRun.ShouldBe(isDryRun);
    }

    [Fact]
    public void FilterOptions_ListAllThenEveryValue()
    {
        RunHistoryViewModel.StatusOptions.Select(option => option.Value)
            .ShouldBe([null, ExitCode.Success, ExitCode.RepositoriesFailed, ExitCode.Critical, ExitCode.DiscoveryEvents]);
        RunHistoryViewModel.ModeOptions.Select(option => option.Value)
            .ShouldBe([null, RunMode.Interactive, RunMode.Silent, RunMode.Ui]);
        RunHistoryViewModel.DryRunOptions.Select(option => option.Value).ShouldBe([null, false, true]);
        _viewModel.SelectedStatus.Value.ShouldBeNull();
        _viewModel.SelectedMode.Value.ShouldBeNull();
        _viewModel.SelectedDryRun.Value.ShouldBeNull();
    }

    [Fact]
    public void Period_FromAndTo_QueriesLocalMidnightsWithLastDayIncluded()
    {
        _viewModel.FromDate = new DateTime(2026, 11, 10);
        _viewModel.ToDate = new DateTime(2026, 11, 12);

        var query = _queries[^1];
        query.From.ShouldBe(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.FromHours(3)));
        query.From!.Value.Offset.ShouldBe(TimeSpan.FromHours(3));
        query.To.ShouldBe(new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.FromHours(3)));
    }

    [Fact]
    public void Period_SameDay_QueriesWholeDay()
    {
        _viewModel.FromDate = new DateTime(2026, 11, 12);
        _viewModel.ToDate = new DateTime(2026, 11, 12);

        var query = _queries[^1];
        (query.To - query.From).ShouldBe(TimeSpan.FromDays(1));
        _viewModel.FilterError.ShouldBeNull();
    }

    [Fact]
    public void Period_FromAfterTo_ShowsErrorAndDoesNotQuery()
    {
        _viewModel.ToDate = new DateTime(2026, 11, 10);
        _queries.Clear();

        _viewModel.FromDate = new DateTime(2026, 11, 11);

        _queries.ShouldBeEmpty();
        _viewModel.HasFilterError.ShouldBeTrue();
        _viewModel.FilterError.ShouldBe(RunHistoryViewModel.InvalidPeriodMessage);
        _viewModel.Runs.ShouldBeEmpty();
        _viewModel.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void Period_Corrected_ClearsErrorAndQueries()
    {
        _viewModel.ToDate = new DateTime(2026, 11, 10);
        _viewModel.FromDate = new DateTime(2026, 11, 11);
        _queries.Clear();

        _viewModel.ToDate = new DateTime(2026, 11, 11);

        _queries.ShouldHaveSingleItem();
        _viewModel.FilterError.ShouldBeNull();
    }

    [Fact]
    public async Task FilterChanged_WhileReading_ReadsAgainWithNewFilter()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<RunHistoryEntry>>();
        var responses = new Queue<Task<IReadOnlyList<RunHistoryEntry>>>([pending.Task, Task.FromResult<IReadOnlyList<RunHistoryEntry>>([])]);
        _respond = responses.Dequeue;
        var refresh = _viewModel.RefreshCommand.ExecuteAsync(null);

        _viewModel.SelectedMode = RunHistoryViewModel.ModeOptions[2];
        _queries.Count.ShouldBe(1);
        pending.SetResult([CreateRun(_startedAt)]);
        await refresh;

        _queries.Count.ShouldBe(2);
        _queries[^1].Mode.ShouldBe(RunMode.Silent);
        _viewModel.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Refresh_SelectedRunStillListed_KeepsSelection()
    {
        var run = CreateRun(_startedAt);
        UseRuns(run, CreateRun(_startedAt.AddHours(-1)));
        await _viewModel.RefreshCommand.ExecuteAsync(null);
        _viewModel.SelectedRun = _viewModel.Runs[0];

        await _viewModel.RefreshCommand.ExecuteAsync(null);

        _viewModel.SelectedRun.ShouldNotBeNull().RunId.ShouldBe(run.RunId);
        _viewModel.HasSelectedRun.ShouldBeTrue();
    }

    [Fact]
    public async Task Refresh_SelectedRunNoLongerListed_ClearsSelection()
    {
        UseRuns(CreateRun(_startedAt));
        await _viewModel.RefreshCommand.ExecuteAsync(null);
        _viewModel.SelectedRun = _viewModel.Runs[0];
        UseRuns();

        await _viewModel.RefreshCommand.ExecuteAsync(null);

        _viewModel.SelectedRun.ShouldBeNull();
        _viewModel.HasSelectedRun.ShouldBeFalse();
    }

    [Fact]
    public async Task Refresh_RunWithDetails_RowShowsRunItemsAndEvents()
    {
        _masker.Register(_token);
        var run = CreateRun(_startedAt) with
        {
            Mode = RunMode.Silent,
            IsDryRun = true,
            ExitCode = ExitCode.RepositoriesFailed,
            RepositoryFilter = "octo/one",
            Items =
            [
                new("octo/one", BackupTarget.Repository, BackupItemStatus.Archived, TimeSpan.FromSeconds(12.5))
                {
                    Branch = "main",
                    HeadCommit = "0123456789abcdef",
                    ArchiveFileName = "one.zip",
                    ChangedRefs = ["heads/main", "tags/v1"],
                },
                new("octo/one", BackupTarget.Wiki, BackupItemStatus.Failed, TimeSpan.FromSeconds(1))
                {
                    Error = $"fatal: token {_token} rejected",
                },
                new("octo/two", BackupTarget.Repository, BackupItemStatus.Failed, TimeSpan.Zero),
            ],
            DiscoveryEvents =
            [
                new("octo/renamed", DiscoveryEventKind.Renamed)
                {
                    PreviousFullName = "octo/old",
                    Decision = DiscoveryEventDecision.FolderRenamed,
                    DecidedBy = DecisionMaker.Automatic,
                },
                new("octo/gone", DiscoveryEventKind.Missing),
            ],
        };
        UseRuns(run);

        await _viewModel.RefreshCommand.ExecuteAsync(null);

        var row = _viewModel.Runs.ShouldHaveSingleItem();
        row.StartedAt.ShouldBe("2026-11-12 10:15:30");
        row.FinishedAt.ShouldBe("2026-11-12 10:17:30");
        row.Duration.ShouldBe("2:00.0");
        row.Mode.ShouldBe("Silent");
        row.DryRun.ShouldBe("Yes");
        row.Filter.ShouldBe("--repo octo/one");
        row.ExitCode.ShouldBe("RepositoriesFailed (1)");
        row.Counts.ShouldBe("Archived: 1, Failed: 2");
        row.Items.ShouldBe(
        [
            new("octo/one", "Repository", "Archived", "0:12.5", "main", "0123456789abcdef", "one.zip", "heads/main, tags/v1", ""),
            new("octo/one", "Wiki", "Failed", "0:01.0", "", "", "", "", "fatal: token *** rejected"),
            new("octo/two", "Repository", "Failed", "0:00.0", "", "", "", "", ""),
        ]);
        row.DiscoveryEvents.ShouldBe(
        [
            new("Renamed", "octo/renamed", "octo/old", "FolderRenamed", "Automatic"),
            new("Missing", "octo/gone", "", "", ""),
        ]);
        row.HasDiscoveryEvents.ShouldBeTrue();
    }

    [Theory]
    [InlineData("octo", null, "--account octo")]
    [InlineData(null, "octo/one", "--repo octo/one")]
    [InlineData(null, null, RunHistoryRow.NoFilterText)]
    public void CreateRow_Filter_ShowsOption(string? account, string? repository, string expected)
    {
        var run = CreateRun(_startedAt) with { AccountFilter = account, RepositoryFilter = repository };

        RunHistoryRow.Create(run, _masker).Filter.ShouldBe(expected);
    }

    [Fact]
    public void CreateRow_NoItems_ShowsNoItemsAndNoEvents()
    {
        var row = RunHistoryRow.Create(CreateRun(_startedAt), _masker);

        row.Counts.ShouldBe(RunHistoryRow.NoItemsText);
        row.DryRun.ShouldBe("No");
        row.ExitCode.ShouldBe("Success (0)");
        row.HasDiscoveryEvents.ShouldBeFalse();
    }

    /// <summary>
    /// Makes the store return <paramref name="runs"/> for every query.
    /// </summary>
    /// <param name="runs">The runs.</param>
    private void UseRuns(params RunHistoryEntry[] runs) =>
        _respond = () => Task.FromResult<IReadOnlyList<RunHistoryEntry>>(runs);

    /// <summary>
    /// Creates an interactive run that took two minutes and succeeded, without items.
    /// </summary>
    /// <param name="startedAt">The time the run started.</param>
    /// <returns>The run.</returns>
    private static RunHistoryEntry CreateRun(DateTimeOffset startedAt) =>
        new(Guid.NewGuid(), startedAt, startedAt.AddMinutes(2), RunMode.Interactive, false, ExitCode.Success);
}
