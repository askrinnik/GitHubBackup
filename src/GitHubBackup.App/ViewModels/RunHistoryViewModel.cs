using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHubBackup.App.Hosting;
using GitHubBackup.Core;
using GitHubBackup.Core.History;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.Security;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Backs the list of runs of the history screen: the runs of the run history filtered by period, status and mode,
/// and the details of the selected run.
/// </summary>
/// <remarks>
/// The status of a run is its exit code; a cancelled run finishes with <see cref="ExitCode.Critical"/>. A change of
/// a filter reads the list again.
/// </remarks>
/// <param name="historyStore">Reads the runs.</param>
/// <param name="secretMasker">Masks secrets in errors before the screen shows them.</param>
/// <param name="timeProvider">Supplies the local time zone the dates of the period belong to.</param>
/// <param name="logger">Logs the failures of reading the run history.</param>
public sealed partial class RunHistoryViewModel(
    IRunHistoryStore historyStore,
    ISecretMasker secretMasker,
    TimeProvider timeProvider,
    ILogger<RunHistoryViewModel> logger) : ObservableObject
{
    /// <summary>The text shown when no run matches the filters.</summary>
    public const string NoRunsMessage = "No runs";

    /// <summary>The error shown when the first day of the period is after the last day.</summary>
    public const string InvalidPeriodMessage = "The 'From' date is after the 'To' date.";

    /// <summary>Whether a filter changed while the list was being read, so it is read again when the read ends.</summary>
    private bool _refreshPending;

    /// <summary>The first local day of the period, or <see langword="null"/> for no lower bound.</summary>
    [ObservableProperty]
    private DateTime? _fromDate;

    /// <summary>The last local day of the period, inclusive, or <see langword="null"/> for no upper bound.</summary>
    [ObservableProperty]
    private DateTime? _toDate;

    /// <summary>The selected choice of the status filter.</summary>
    [ObservableProperty]
    private HistoryFilterOption<ExitCode?> _selectedStatus = StatusOptions[0];

    /// <summary>The selected choice of the mode filter.</summary>
    [ObservableProperty]
    private HistoryFilterOption<RunMode?> _selectedMode = ModeOptions[0];

    /// <summary>The selected choice of the dry-run filter.</summary>
    [ObservableProperty]
    private HistoryFilterOption<bool?> _selectedDryRun = DryRunOptions[0];

    /// <summary>The runs that match the filters, the most recent first.</summary>
    [ObservableProperty]
    private IReadOnlyList<RunHistoryRow> _runs = [];

    /// <summary>The run whose details are shown, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedRun))]
    private RunHistoryRow? _selectedRun;

    /// <summary>Whether the list was read and no run matches the filters.</summary>
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>Why the run history cannot be read, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    private string? _loadError;

    /// <summary>Why the filters cannot be applied, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilterError))]
    private string? _filterError;

    /// <summary>Gets the choices of the status filter: every run, then one per exit code.</summary>
    public static IReadOnlyList<HistoryFilterOption<ExitCode?>> StatusOptions { get; } =
    [
        new("All", null),
        new("Success", ExitCode.Success),
        new("Repositories failed", ExitCode.RepositoriesFailed),
        new("Critical", ExitCode.Critical),
        new("Discovery events", ExitCode.DiscoveryEvents),
    ];

    /// <summary>Gets the choices of the mode filter: every run, then one per mode.</summary>
    public static IReadOnlyList<HistoryFilterOption<RunMode?>> ModeOptions { get; } =
    [
        new("All", null),
        new("Interactive", RunMode.Interactive),
        new("Silent", RunMode.Silent),
        new("Ui", RunMode.Ui),
    ];

    /// <summary>Gets the choices of the dry-run filter.</summary>
    public static IReadOnlyList<HistoryFilterOption<bool?>> DryRunOptions { get; } =
    [
        new("All", null),
        new("Regular only", false),
        new("Dry run only", true),
    ];

    /// <summary>Gets whether the details of a run are shown.</summary>
    public bool HasSelectedRun => SelectedRun is not null;

    /// <summary>Gets whether the run history cannot be read.</summary>
    public bool HasLoadError => LoadError is not null;

    /// <summary>Gets whether the filters cannot be applied.</summary>
    public bool HasFilterError => FilterError is not null;

    /// <summary>
    /// Reads the runs that match the filters, keeping the selected run when it still matches; a filter changed while
    /// the list is read starts another read when this one ends.
    /// </summary>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>A task that completes when the list is shown or the error is set.</returns>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        do
        {
            _refreshPending = false;
            await LoadAsync(cancellationToken);
        }
        while (_refreshPending);
    }

    /// <summary>
    /// Reads the runs that match the current filters once.
    /// </summary>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>A task that completes when the list is shown or the error is set.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (FromDate is { } from && ToDate is { } to && from.Date > to.Date)
        {
            ShowRuns([]);
            IsEmpty = false;
            LoadError = null;
            FilterError = InvalidPeriodMessage;
            return;
        }

        FilterError = null;
        try
        {
            var runs = await historyStore.QueryAsync(CreateQuery(), cancellationToken);
            ShowRuns([.. runs.Select(run => RunHistoryRow.Create(run, secretMasker))]);
            IsEmpty = Runs.Count == 0;
            LoadError = null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLog.RunHistoryLoadFailed(logger, exception);
            ShowRuns([]);
            IsEmpty = false;
            LoadError = secretMasker.Mask(exception.Message);
        }
    }

    /// <summary>
    /// Replaces the list, keeping the selected run when the new list holds it.
    /// </summary>
    /// <param name="runs">The new list.</param>
    private void ShowRuns(IReadOnlyList<RunHistoryRow> runs)
    {
        var selectedId = SelectedRun?.RunId;
        Runs = runs;
        SelectedRun = runs.FirstOrDefault(run => run.RunId == selectedId);
    }

    /// <summary>
    /// Creates the query of the current filters; the days of the period become the instants their local midnights
    /// fall on, and the last day is included whole.
    /// </summary>
    /// <returns>The query.</returns>
    private RunHistoryQuery CreateQuery() => new()
    {
        From = FromDate is { } from ? GetLocalMidnight(from) : null,
        To = ToDate is { } to ? GetLocalMidnight(to.AddDays(1)) : null,
        Mode = SelectedMode.Value,
        IsDryRun = SelectedDryRun.Value,
        ExitCode = SelectedStatus.Value,
    };

    /// <summary>
    /// Returns the instant a local day starts at in the local time zone.
    /// </summary>
    /// <remarks>
    /// Where a clock change skips midnight, the day starts at the first local time that exists.
    /// </remarks>
    /// <param name="day">The day; its time of day is ignored.</param>
    /// <returns>The instant with the offset of the time zone at that moment.</returns>
    private DateTimeOffset GetLocalMidnight(DateTime day)
    {
        var timeZone = timeProvider.LocalTimeZone;
        var start = DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified);
        while (timeZone.IsInvalidTime(start))
        {
            start = start.AddMinutes(15);
        }

        return new(start, timeZone.GetUtcOffset(start));
    }

    /// <summary>
    /// Reads the list again, or marks it to be read again when a read is in progress.
    /// </summary>
    private void RequestRefresh()
    {
        if (RefreshCommand.IsRunning)
        {
            _refreshPending = true;
            return;
        }

        RefreshCommand.Execute(null);
    }

    /// <summary>Reads the list for the new period.</summary>
    /// <param name="value">The new first day.</param>
    partial void OnFromDateChanged(DateTime? value) => RequestRefresh();

    /// <summary>Reads the list for the new period.</summary>
    /// <param name="value">The new last day.</param>
    partial void OnToDateChanged(DateTime? value) => RequestRefresh();

    /// <summary>Reads the list for the new status.</summary>
    /// <param name="value">The new choice.</param>
    partial void OnSelectedStatusChanged(HistoryFilterOption<ExitCode?> value) => RequestRefresh();

    /// <summary>Reads the list for the new mode.</summary>
    /// <param name="value">The new choice.</param>
    partial void OnSelectedModeChanged(HistoryFilterOption<RunMode?> value) => RequestRefresh();

    /// <summary>Reads the list for the new dry-run choice.</summary>
    /// <param name="value">The new choice.</param>
    partial void OnSelectedDryRunChanged(HistoryFilterOption<bool?> value) => RequestRefresh();
}
