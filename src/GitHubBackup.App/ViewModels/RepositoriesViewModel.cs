using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHubBackup.App.Dialogs;
using GitHubBackup.App.Hosting;
using GitHubBackup.App.Runs;
using GitHubBackup.Core.Overview;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.Security;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Backs the main screen: the tree of accounts and repositories of <c>backup-config.json</c> and the commands that
/// start a backup of everything, of one account or of one repository.
/// </summary>
/// <remarks>
/// The tree is read again before and after every run, so a run always reflects the file as it is on disk and the
/// dates of the last archives follow the run.
/// </remarks>
/// <param name="overviewProvider">Reads the configuration and the dates of the last archives.</param>
/// <param name="runService">Runs the backup.</param>
/// <param name="dialogs">Shows the errors of a run.</param>
/// <param name="secretMasker">
/// Masks secrets in exception messages before the window shows them, because an exception from a lower layer can
/// carry a token or a header.
/// </param>
/// <param name="logger">Logs the failures of a run and of reading the tree.</param>
public sealed partial class RepositoriesViewModel(
    IRepositoryOverviewProvider overviewProvider,
    IBackupRunService runService,
    IDialogService dialogs,
    ISecretMasker secretMasker,
    ILogger<RepositoriesViewModel> logger) : ObservableObject
{
    /// <summary>The title of the dialog that shows the errors that stopped a run.</summary>
    public const string RunStoppedTitle = "Backup stopped";

    /// <summary>The title of the dialog that shows an unexpected failure of a run.</summary>
    public const string RunFailedTitle = "Backup failed";

    /// <summary>The title of the dialog that shows that a run was not recorded in the run history.</summary>
    public const string HistoryWarningTitle = "Run history not updated";

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

    /// <summary>Whether a run is in progress; the commands that start a run and the dry-run switch are disabled then.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(StartBackupCommand))]
    private bool _isRunning;

    /// <summary>Why the tree cannot be read, or <see langword="null"/> when it was read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    private string? _loadError;

    /// <summary>Whether the configuration was read and lists neither an account nor a standalone repository.</summary>
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>The outcome of the last run that went through, or <see langword="null"/>.</summary>
    [ObservableProperty]
    private string? _lastRunMessage;

    /// <summary>Gets whether no run is in progress.</summary>
    public bool IsIdle => !IsRunning;

    /// <summary>Gets whether the tree cannot be read.</summary>
    public bool HasLoadError => LoadError is not null;

    /// <summary>
    /// Reads the tree again.
    /// </summary>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>A task that completes when the tree is read or the error is shown.</returns>
    [RelayCommand]
    private Task RefreshAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    /// <summary>
    /// Returns whether a run of <paramref name="scope"/> can start: no run is in progress, and the selection names
    /// an account for <see cref="BackupScope.Account"/> or an active repository for <see cref="BackupScope.Repository"/>.
    /// </summary>
    /// <param name="scope">The repositories the run would cover.</param>
    /// <returns><see langword="true"/> when the run can start.</returns>
    private bool CanStartBackup(BackupScope scope) => !IsRunning && CreateFilter(scope) is not null;

    /// <summary>
    /// Reads the tree again, runs the backup of <paramref name="scope"/> and shows its outcome.
    /// </summary>
    /// <remarks>
    /// A tree that cannot be read does not stop the run: the orchestrator reads the configuration itself, reports the
    /// error and records the run in the history, as the console application does.
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
        LastRunMessage = null;
        try
        {
            await LoadAsync(cancellationToken);
            var result = await runService.RunAsync(filter, dryRun, cancellationToken);
            ShowOutcome(result, dryRun);
        }
        catch (OperationCanceledException)
        {
            // A cancelled run is the user's choice and needs no message.
        }
        catch (Exception exception)
        {
            AppLog.BackupRunFailed(logger, exception);
            dialogs.ShowError(RunFailedTitle, secretMasker.Mask(exception.Message));
        }
        finally
        {
            await LoadAsync(CancellationToken.None);
            IsRunning = false;
        }
    }

    /// <summary>
    /// Shows the outcome of a run: the errors that stopped it and a missing history record in a dialog, otherwise
    /// the exit code in <see cref="LastRunMessage"/>.
    /// </summary>
    /// <param name="result">The result of the run.</param>
    /// <param name="dryRun">Whether the run was a dry run.</param>
    private void ShowOutcome(BackupRunResult result, bool dryRun)
    {
        if (result.CriticalErrors.Count > 0)
        {
            dialogs.ShowError(RunStoppedTitle, string.Join(Environment.NewLine, result.CriticalErrors));
        }
        else
        {
            LastRunMessage = string.Format(
                CultureInfo.InvariantCulture,
                "{0} finished with exit code {1}.",
                dryRun ? "Dry run" : "Backup",
                (int)result.ExitCode);
        }

        if (result.HistoryWarning is not null)
        {
            dialogs.ShowError(HistoryWarningTitle, result.HistoryWarning);
        }
    }

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
