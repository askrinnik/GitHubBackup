using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHubBackup.App.Dialogs;
using GitHubBackup.App.Hosting;
using GitHubBackup.App.Navigation;
using GitHubBackup.App.Runs;
using GitHubBackup.Core.Overview;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Backs the main screen: the tree of accounts and repositories of <c>backup-config.json</c> and the commands that
/// start a backup of everything, of one account or of one repository, show its progress, cancel it and show its
/// summary.
/// </summary>
/// <remarks>
/// The tree is read again before and after every run, so a run always reflects the file as it is on disk and the
/// dates of the last archives follow the run.
/// </remarks>
/// <param name="overviewProvider">Reads the configuration and the dates of the last archives.</param>
/// <param name="runService">Runs the backup.</param>
/// <param name="uiDispatcher">
/// Brings the progress reports and the log entries of a run from the thread of the core to the UI thread.
/// </param>
/// <param name="liveLogFeed">Delivers the log entries that the live log shows while a run works.</param>
/// <param name="dialogs">Shows an unexpected failure of a run and asks before the window closes during a run.</param>
/// <param name="secretMasker">
/// Masks secrets in exception messages before the window shows them, because an exception from a lower layer can
/// carry a token or a header.
/// </param>
/// <param name="environment">The host environment whose content root holds the log folder the summary names.</param>
/// <param name="timeProvider">Supplies the local date that names the log file of the day.</param>
/// <param name="navigation">Opens the settings editor.</param>
/// <param name="logger">Logs the failures of a run and of reading the tree.</param>
public sealed partial class RepositoriesViewModel(
    IRepositoryOverviewProvider overviewProvider,
    IBackupRunService runService,
    IUiDispatcher uiDispatcher,
    ILiveLogFeed liveLogFeed,
    IDialogService dialogs,
    ISecretMasker secretMasker,
    IHostEnvironment environment,
    TimeProvider timeProvider,
    INavigationService navigation,
    ILogger<RepositoriesViewModel> logger) : ObservableObject, ICloseGuard
{
    /// <summary>The title of the dialog that shows an unexpected failure of a run.</summary>
    public const string RunFailedTitle = "Backup failed";

    /// <summary>The title of the dialog that asks whether to close the window during a run.</summary>
    public const string CloseDuringRunTitle = "Backup in progress";

    /// <summary>The question of the dialog that asks whether to close the window during a run.</summary>
    public const string CloseDuringRunMessage =
        "A backup is running. Cancel it and close the window when it has stopped?";

    /// <summary>The nodes at the top of the tree: the accounts in file order, then the group of standalone repositories.</summary>
    [ObservableProperty]
    private IReadOnlyList<TreeNodeViewModel> _nodes = [];

    /// <summary>The node selected in the tree, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartBackupCommand))]
    private TreeNodeViewModel? _selectedNode;

    /// <summary>Whether the next run only shows what a backup run would do.</summary>
    [ObservableProperty]
    private bool _isDryRun;

    /// <summary>
    /// Whether a run is in progress; the commands that start a run, the dry-run switch and the settings editor are
    /// disabled then, because a run writes <c>backup-config.json</c> too.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(StartBackupCommand), nameof(OpenSettingsCommand))]
    private bool _isRunning;

    /// <summary>Why the tree cannot be read, or <see langword="null"/> when it was read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    private string? _loadError;

    /// <summary>Whether the configuration was read and lists neither an account nor a standalone repository.</summary>
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>The summary of the last run, or <see langword="null"/> before the first run and while a run is in progress.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSummary))]
    private RunSummaryViewModel? _summary;

    /// <summary>The current repository and operation of the run, or <see langword="null"/> when nothing is reported.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProgress), nameof(ProgressRepository), nameof(ProgressOperation))]
    private BackupProgress? _currentProgress;

    /// <summary>
    /// The number of the run whose progress the screen accepts; it changes when a run starts and when it ends, so a
    /// report still queued on the UI thread from a finished run is dropped.
    /// </summary>
    private int _runNumber;

    /// <summary>Gets the live log of the current or the last run.</summary>
    public RunLogViewModel RunLog { get; } = new(uiDispatcher);

    /// <summary>Gets whether no run is in progress.</summary>
    public bool IsIdle => !IsRunning;

    /// <summary>Gets whether the tree cannot be read.</summary>
    public bool HasLoadError => LoadError is not null;

    /// <summary>Gets whether a run summary is shown.</summary>
    public bool HasSummary => Summary is not null;

    /// <summary>Gets whether the current repository or operation of a run is shown.</summary>
    public bool HasProgress => CurrentProgress is not null;

    /// <summary>
    /// Gets the repository or wiki the run works on with its position, such as <c>owner/repo (2 of 5)</c>, or
    /// <see langword="null"/> when the operation concerns no single repository.
    /// </summary>
    public string? ProgressRepository =>
        CurrentProgress is { Repository: { } repository } progress
            ? $"{repository} ({progress.Index} of {progress.Total})"
            : null;

    /// <summary>Gets the operation the run is busy with, or <see langword="null"/> when nothing is reported.</summary>
    public string? ProgressOperation => CurrentProgress is { } progress ? DescribeStage(progress.Stage) : null;

    /// <summary>
    /// Returns the text the screen shows for an operation of a run.
    /// </summary>
    /// <param name="stage">The operation.</param>
    /// <returns>The text.</returns>
    public static string DescribeStage(BackupProgressStage stage) => stage switch
    {
        BackupProgressStage.Discovering => "Discovering repositories on GitHub",
        BackupProgressStage.Preparing => "Preparing",
        BackupProgressStage.CheckingRemote => "Checking the server for changes",
        BackupProgressStage.Cloning => "Cloning",
        BackupProgressStage.Fetching => "Fetching changes",
        BackupProgressStage.Archiving => "Archiving",
        _ => stage.ToString(),
    };

    /// <inheritdoc />
    /// <remarks>
    /// Without a run the window closes at once. During a run the user confirms first, unless a cancellation is already
    /// requested; the run is then cancelled and awaited, so the core records it in the run history and removes its
    /// temporary files before the host stops.
    /// </remarks>
    public async Task<bool> CanCloseAsync()
    {
        if (!StartBackupCommand.IsRunning)
        {
            return true;
        }

        if (!StartBackupCommand.IsCancellationRequested && !dialogs.Confirm(CloseDuringRunTitle, CloseDuringRunMessage))
        {
            return false;
        }

        StartBackupCommand.Cancel();
        if (StartBackupCommand.ExecutionTask is { } execution)
        {
            await execution;
        }

        return true;
    }

    /// <summary>
    /// Reads the tree again.
    /// </summary>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>A task that completes when the tree is read or the error is shown.</returns>
    [RelayCommand]
    private Task RefreshAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    /// <summary>
    /// Opens the settings editor in place of the tree.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void OpenSettings() => navigation.NavigateTo<SettingsViewModel>();

    /// <summary>
    /// Returns whether a run of <paramref name="scope"/> can start: no run is in progress, and the selection names
    /// an account for <see cref="BackupScope.Account"/> or an active repository for <see cref="BackupScope.Repository"/>.
    /// </summary>
    /// <param name="scope">The repositories the run would cover.</param>
    /// <returns><see langword="true"/> when the run can start.</returns>
    private bool CanStartBackup(BackupScope scope) => !IsRunning && CreateFilter(scope) is not null;

    /// <summary>
    /// Reads the tree again, runs the backup of <paramref name="scope"/> and shows its summary.
    /// </summary>
    /// <remarks>
    /// A tree that cannot be read does not stop the run: the orchestrator reads the configuration itself, reports the
    /// error and records the run in the history, as the console application does. An unexpected failure has no
    /// result to summarize, so it is shown in a dialog. The progress of the run is shown while it works and cleared when
    /// it ends. The live log is cleared when the run starts and receives the log entries until the run ends; it keeps
    /// them afterwards so the user can read them next to the summary.
    /// </remarks>
    /// <param name="scope">The repositories the run covers.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>A task that completes when the run has finished and the tree is read again.</returns>
    [RelayCommand(CanExecute = nameof(CanStartBackup), IncludeCancelCommand = true)]
    private async Task StartBackupAsync(BackupScope scope, CancellationToken cancellationToken)
    {
        if (CreateFilter(scope) is not { } filter)
        {
            return;
        }

        var dryRun = IsDryRun;
        IsRunning = true;
        Summary = null;
        var run = ++_runNumber;
        CurrentProgress = null;
        RunLog.Clear();
        var logSubscription = liveLogFeed.Subscribe(RunLog.Add);
        var progress = new UiProgress<BackupProgress>(uiDispatcher, value => ShowProgress(run, value));
        try
        {
            await LoadAsync(cancellationToken);
            var result = await runService.RunAsync(filter, dryRun, progress, cancellationToken);
            Summary = RunSummaryViewModel.FromResult(result, GetLogPath(), secretMasker);
        }
        catch (OperationCanceledException)
        {
            // A run cancelled before the core takes the run lock has no result, only the cancellation to report.
            Summary = RunSummaryViewModel.CancelledBeforeStart(GetLogPath());
        }
        catch (Exception exception)
        {
            AppLog.BackupRunFailed(logger, exception);
            dialogs.ShowError(RunFailedTitle, secretMasker.Mask(exception.Message));
        }
        finally
        {
            logSubscription.Dispose();
            _runNumber++;
            CurrentProgress = null;
            await LoadAsync(CancellationToken.None);
            IsRunning = false;
        }
    }

    /// <summary>
    /// Shows a progress report of the run <paramref name="run"/> unless that run has ended.
    /// </summary>
    /// <param name="run">The number of the run that reported.</param>
    /// <param name="progress">The current repository and operation.</param>
    private void ShowProgress(int run, BackupProgress progress)
    {
        if (run == _runNumber)
        {
            CurrentProgress = progress;
        }
    }

    /// <summary>
    /// Returns the path of the text log of the current day, which the summary names.
    /// </summary>
    /// <returns>The absolute path of the log file.</returns>
    private string GetLogPath() => LogFiles.GetTextLogPath(environment.ContentRootPath, timeProvider);

    /// <summary>
    /// Returns the filter of a run of <paramref name="scope"/> for the current selection.
    /// </summary>
    /// <param name="scope">The repositories the run covers.</param>
    /// <returns>The filter, or <see langword="null"/> when the selection does not allow the run.</returns>
    private BackupRunFilter? CreateFilter(BackupScope scope) => scope switch
    {
        BackupScope.All => BackupRunFilter.All,
        BackupScope.Account => SelectedNode switch
        {
            AccountNodeViewModel account => BackupRunFilter.ForAccount(account.Login),
            RepositoryNodeViewModel { Entry.AccountLogin: { } login } => BackupRunFilter.ForAccount(login),
            _ => null,
        },
        BackupScope.Repository => SelectedNode is RepositoryNodeViewModel { IsActive: true } repository
            ? BackupRunFilter.ForRepository(repository.Entry.Owner, repository.Entry.Name)
            : null,
        _ => null,
    };

    /// <summary>
    /// Reads the tree and rebuilds the nodes, keeping the selected node and the collapsed nodes by their keys; a
    /// failure clears the tree and shows the error instead.
    /// </summary>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>A task that completes when the nodes are rebuilt or the error is set.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var selectedKey = SelectedNode?.Key;
        var collapsedKeys = Nodes.Where(node => !node.IsExpanded).Select(node => node.Key).ToHashSet();
        try
        {
            var overview = await overviewProvider.LoadAsync(cancellationToken);
            SelectedNode = null;
            Nodes = BuildNodes(overview, collapsedKeys);
            LoadError = null;
            IsEmpty = overview.IsEmpty;
            if (selectedKey is not null && FindNode(Nodes, selectedKey) is { } selected)
            {
                selected.IsSelected = true;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLog.RepositoryTreeLoadFailed(logger, exception);
            SelectedNode = null;
            Nodes = [];
            IsEmpty = false;
            LoadError = secretMasker.Mask(exception.Message);
        }
    }

    /// <summary>
    /// Creates the nodes of the tree: one per account, then the group of standalone repositories when there is one.
    /// </summary>
    /// <param name="overview">The accounts and the standalone repositories.</param>
    /// <param name="collapsedKeys">The keys of the top nodes that stay collapsed; every other top node is expanded.</param>
    /// <returns>The top nodes.</returns>
    private List<TreeNodeViewModel> BuildNodes(RepositoryOverview overview, HashSet<string> collapsedKeys)
    {
        var nodes = new List<TreeNodeViewModel>(overview.Accounts.Count + 1);
        foreach (var account in overview.Accounts)
        {
            nodes.Add(new AccountNodeViewModel(account.Login, CreateRepositoryNodes(account.Repositories), OnNodeSelectionChanged));
        }

        if (overview.StandaloneRepositories.Count > 0)
        {
            nodes.Add(new RepositoryGroupNodeViewModel(
                CreateRepositoryNodes(overview.StandaloneRepositories), OnNodeSelectionChanged));
        }

        foreach (var node in nodes)
        {
            node.IsExpanded = !collapsedKeys.Contains(node.Key);
        }

        return nodes;
    }

    /// <summary>
    /// Creates the nodes of repositories.
    /// </summary>
    /// <param name="entries">The repositories.</param>
    /// <returns>One node per repository, in the same order.</returns>
    private List<RepositoryNodeViewModel> CreateRepositoryNodes(IReadOnlyList<RepositoryOverviewEntry> entries) =>
        [.. entries.Select(entry => new RepositoryNodeViewModel(entry, OnNodeSelectionChanged))];

    /// <summary>
    /// Follows the selection of the tree in <see cref="SelectedNode"/>.
    /// </summary>
    /// <param name="node">The node whose selection changed.</param>
    /// <param name="isSelected">Whether the node is selected now.</param>
    private void OnNodeSelectionChanged(TreeNodeViewModel node, bool isSelected)
    {
        if (isSelected)
        {
            SelectedNode = node;
        }
        else if (ReferenceEquals(SelectedNode, node))
        {
            SelectedNode = null;
        }
    }

    /// <summary>
    /// Finds the node with <paramref name="key"/> among <paramref name="nodes"/> and their descendants.
    /// </summary>
    /// <param name="nodes">The nodes to search.</param>
    /// <param name="key">The key of the node.</param>
    /// <returns>The node, or <see langword="null"/> when no node has the key.</returns>
    private static TreeNodeViewModel? FindNode(IReadOnlyList<TreeNodeViewModel> nodes, string key)
    {
        foreach (var node in nodes)
        {
            if (node.Key == key)
            {
                return node;
            }

            if (FindNode(node.Children, key) is { } child)
            {
                return child;
            }
        }

        return null;
    }
}
