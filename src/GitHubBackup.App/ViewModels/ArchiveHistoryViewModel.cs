using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHubBackup.App.Dialogs;
using GitHubBackup.App.Hosting;
using GitHubBackup.App.Shell;
using GitHubBackup.Core.History;
using GitHubBackup.Core.Overview;
using GitHubBackup.Core.Security;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Backs the archive history of the history screen: the repositories of <c>backup-config.json</c>, the archives of the
/// selected repository and its wiki, and the commands that show the archive folder or an archive in Explorer.
/// </summary>
/// <param name="overviewProvider">Reads the repositories with their archive folders.</param>
/// <param name="archiveHistoryProvider">Reads the archives of a repository.</param>
/// <param name="explorer">Opens the archive folder and selects an archive in Explorer.</param>
/// <param name="dialogs">Shows a failure to start Explorer.</param>
/// <param name="secretMasker">Masks secrets in errors before the screen shows them.</param>
/// <param name="logger">Logs the failures of reading and of starting Explorer.</param>
public sealed partial class ArchiveHistoryViewModel(
    IRepositoryOverviewProvider overviewProvider,
    IArchiveHistoryProvider archiveHistoryProvider,
    IExplorerLauncher explorer,
    IDialogService dialogs,
    ISecretMasker secretMasker,
    ILogger<ArchiveHistoryViewModel> logger) : ObservableObject
{
    /// <summary>The text shown when the selected repository has no archives.</summary>
    public const string NoArchivesMessage = "No archives";

    /// <summary>The title of the dialog that shows a failure to start Explorer.</summary>
    public const string ExplorerFailedTitle = "Explorer not started";

    /// <summary>Whether the selection changed while archives were being read, so they are read again when the read ends.</summary>
    private bool _archivesPending;

    /// <summary>The repositories of the configuration: those of the accounts in file order, then the standalone ones.</summary>
    [ObservableProperty]
    private IReadOnlyList<RepositoryOverviewEntry> _repositories = [];

    /// <summary>The repository whose archives are shown, or <see langword="null"/>.</summary>
    [ObservableProperty]
    private RepositoryOverviewEntry? _selectedRepository;

    /// <summary>Why the repositories cannot be read, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRepositoriesError))]
    private string? _repositoriesError;

    /// <summary>The archives of the selected repository and its wiki, the most recent first.</summary>
    [ObservableProperty]
    private IReadOnlyList<ArchiveHistoryRow> _archives = [];

    /// <summary>The selected archive, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowInExplorerCommand))]
    private ArchiveHistoryRow? _selectedArchive;

    /// <summary>The archive folder of the selected repository, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
    private string? _archiveFolder;

    /// <summary>Whether the archive folder of the selected repository exists.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
    private bool _folderExists;

    /// <summary>Whether the archives were read and there are none.</summary>
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>Why the archives of the selected repository cannot be read, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    private string? _loadError;

    /// <summary>Gets whether the repositories cannot be read.</summary>
    public bool HasRepositoriesError => RepositoriesError is not null;

    /// <summary>Gets whether the archives of the selected repository cannot be read.</summary>
    public bool HasLoadError => LoadError is not null;

    /// <summary>
    /// Reads the repositories of the configuration and keeps the selected repository when it is still listed.
    /// </summary>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>A task that completes when the list is shown or the error is set.</returns>
    [RelayCommand]
    private async Task LoadRepositoriesAsync(CancellationToken cancellationToken)
    {
        var selectedId = SelectedRepository?.RepositoryId;
        try
        {
            var overview = await overviewProvider.LoadAsync(cancellationToken);
            Repositories =
            [
                .. overview.Accounts.SelectMany(account => account.Repositories),
                .. overview.StandaloneRepositories,
            ];
            RepositoriesError = null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLog.HistoryRepositoriesLoadFailed(logger, exception);
            Repositories = [];
            RepositoriesError = secretMasker.Mask(exception.Message);
        }

        SelectedRepository = Repositories.FirstOrDefault(repository => repository.RepositoryId == selectedId);
    }

    /// <summary>
    /// Reads the archives of the selected repository; a selection changed while they are read starts another read
    /// when this one ends.
    /// </summary>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>A task that completes when the archives are shown or the error is set.</returns>
    [RelayCommand]
    private async Task LoadArchivesAsync(CancellationToken cancellationToken)
    {
        do
        {
            _archivesPending = false;
            await ReadArchivesAsync(cancellationToken);
        }
        while (_archivesPending);
    }

    /// <summary>
    /// Opens the archive folder of the selected repository in Explorer.
    /// </summary>
    /// <param name="cancellationToken">Cancels waiting for Explorer.</param>
    /// <returns>A task that completes when Explorer has taken the request or the error is shown.</returns>
    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    private Task OpenFolderAsync(CancellationToken cancellationToken) => ArchiveFolder is { } folder
        ? RunExplorerAsync(folder, () => explorer.OpenFolderAsync(folder, cancellationToken))
        : Task.CompletedTask;

    /// <summary>
    /// Opens the archive folder in Explorer with the selected archive selected.
    /// </summary>
    /// <param name="cancellationToken">Cancels waiting for Explorer.</param>
    /// <returns>A task that completes when Explorer has taken the request or the error is shown.</returns>
    [RelayCommand(CanExecute = nameof(CanShowInExplorer))]
    private Task ShowInExplorerAsync(CancellationToken cancellationToken) => SelectedArchive is { IsMissing: false } archive
        ? RunExplorerAsync(archive.FullPath, () => explorer.ShowFileAsync(archive.FullPath, cancellationToken))
        : Task.CompletedTask;

    /// <summary>
    /// Returns whether the archive folder of the selected repository exists.
    /// </summary>
    /// <returns><see langword="true"/> when the folder can be opened.</returns>
    private bool CanOpenFolder() => ArchiveFolder is not null && FolderExists;

    /// <summary>
    /// Returns whether an archive whose file exists is selected.
    /// </summary>
    /// <returns><see langword="true"/> when the archive can be shown.</returns>
    private bool CanShowInExplorer() => SelectedArchive is { IsMissing: false };

    /// <summary>
    /// Starts Explorer and shows a failure in a dialog.
    /// </summary>
    /// <param name="path">The folder or file Explorer is to show, for the log.</param>
    /// <param name="start">Starts Explorer.</param>
    /// <returns>A task that completes when Explorer has taken the request or the error is shown.</returns>
    private async Task RunExplorerAsync(string path, Func<Task> start)
    {
        try
        {
            await start();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLog.ExplorerFailed(logger, path, exception);
            dialogs.ShowError(ExplorerFailedTitle, secretMasker.Mask(exception.Message));
        }
    }

    /// <summary>
    /// Reads the archives of the selected repository once.
    /// </summary>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>A task that completes when the archives are shown or the error is set.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    private async Task ReadArchivesAsync(CancellationToken cancellationToken)
    {
        SelectedArchive = null;
        if (SelectedRepository is not { } repository)
        {
            Archives = [];
            ArchiveFolder = null;
            FolderExists = false;
            IsEmpty = false;
            LoadError = null;
            return;
        }

        ArchiveFolder = repository.ArchiveFolder;
        try
        {
            var history = await archiveHistoryProvider.LoadAsync(
                repository.ArchiveFolder, repository.RepositoryId, repository.FullName, cancellationToken);
            Archives = [.. history.Archives.Select(ArchiveHistoryRow.Create)];
            FolderExists = history.FolderExists;
            IsEmpty = Archives.Count == 0;
            LoadError = null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLog.ArchiveHistoryLoadFailed(logger, repository.FullName, exception);
            Archives = [];
            FolderExists = false;
            IsEmpty = false;
            LoadError = secretMasker.Mask(exception.Message);
        }
    }

    /// <summary>
    /// Reads the archives of the new selection, or marks them to be read again when a read is in progress.
    /// </summary>
    /// <param name="value">The new selection.</param>
    partial void OnSelectedRepositoryChanged(RepositoryOverviewEntry? value)
    {
        if (LoadArchivesCommand.IsRunning)
        {
            _archivesPending = true;
            return;
        }

        LoadArchivesCommand.Execute(null);
    }
}
