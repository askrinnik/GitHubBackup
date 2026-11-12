using System.IO.Abstractions;
using GitHubBackup.Core.Archiving;
using GitHubBackup.Core.Authentication;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Changes;
using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Core.Logging;
using GitHubBackup.Core.State;
using GitHubBackup.Core.Tools;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Runs;

/// <summary>
/// Implements <see cref="IBackupOrchestrator"/> for the repositories listed in <c>backup-config.json</c> and their
/// wikis.
/// </summary>
/// <remarks>
/// The wiki of each active repository is processed right after the repository, by the same steps but with its own
/// clone, its own entry in the state file and its own result, so a change or a failure of one never decides for the
/// other.
/// </remarks>
/// <param name="configStore">Reads <c>backup-config.json</c>.</param>
/// <param name="toolLocator">Locates git and 7-Zip.</param>
/// <param name="tokenProvider">Supplies the GitHub token, or anonymous access.</param>
/// <param name="runLock">Keeps a second run from starting while this one works.</param>
/// <param name="pathResolver">Resolves the archive, clone and temporary folders.</param>
/// <param name="exclusionMatcher">Finds the repositories of the list that an exclusion mask removes from the run.</param>
/// <param name="discoveryService">Finds new, missing and renamed repositories.</param>
/// <param name="decisionApplier">Applies the decisions about discovered repositories to the configuration.</param>
/// <param name="remoteUrlProvider">Supplies the URL git clones each repository and wiki from.</param>
/// <param name="gitHubApi">
/// Tells whether the wiki feature of a repository is enabled when discovery did not report the repository.
/// </param>
/// <param name="changeDetector">Brings each clone up to date and decides whether it needs an archive.</param>
/// <param name="nameBuilder">Names each new archive.</param>
/// <param name="archiver">Creates each archive.</param>
/// <param name="stateStore">Reads and records the archives of each repository.</param>
/// <param name="fileSystem">Empties the folder of temporary archives.</param>
/// <param name="timeProvider">Measures the time spent on each repository and wiki.</param>
/// <param name="logger">The logger.</param>
public sealed class BackupOrchestrator(
    IBackupConfigStore configStore,
    IToolLocator toolLocator,
    ITokenProvider tokenProvider,
    IRunLock runLock,
    IRepositoryPathResolver pathResolver,
    IExclusionMatcher exclusionMatcher,
    IRepositoryDiscoveryService discoveryService,
    IDiscoveryDecisionApplier decisionApplier,
    IRemoteUrlProvider remoteUrlProvider,
    IGitHubApi gitHubApi,
    IChangeDetector changeDetector,
    IArchiveNameBuilder nameBuilder,
    IArchiver archiver,
    IBackupStateStore stateStore,
    IFileSystem fileSystem,
    TimeProvider timeProvider,
    ILogger<BackupOrchestrator> logger) : IBackupOrchestrator
{
    /// <summary>The error that stops a run while another run holds the lock.</summary>
    public const string RunLockHeldMessage = "A backup is already running.";

    /// <summary>The message of the repository or wiki that was being processed when the run was cancelled.</summary>
    public const string CancelledMessage = "The run was cancelled while this item was being processed.";

    /// <summary>The message of each repository that a cancelled run did not start.</summary>
    public const string NotProcessedMessage = "Not processed: the run was cancelled.";

    /// <inheritdoc />
    public async Task<BackupRunResult> RunAsync(BackupRunRequest request, CancellationToken cancellationToken)
    {
        BackupConfigLoadResult loaded;
        try
        {
            loaded = await configStore.LoadAsync(cancellationToken);
        }
        catch (BackupConfigException exception)
        {
            return Stop(exception.Errors, [], exception);
        }

        var config = loaded.Config;
        var filter = request.Filter;
        ToolSet tools;
        IReadOnlyList<BackupWorkItem> items;
        try
        {
            tools = await toolLocator.LocateAsync(cancellationToken);
            items = BackupWorkItem.CreateAll(config, pathResolver);
        }
        catch (Exception exception) when (exception is ToolUnavailableException or ArgumentException)
        {
            return Stop([exception.Message], loaded.Warnings, exception);
        }

        if (filter.IsRepository && !items.Any(filter.Includes))
        {
            return Stop([$"The repository '{filter.RepositoryFullName}' is not in backup-config.json."], loaded.Warnings, null);
        }

        var token = await tokenProvider.GetTokenAsync(cancellationToken);

        // The lock is taken before the folder of temporary archives is emptied, because that folder may hold the
        // archive another run is building right now.
        using var runLockHandle = await runLock.TryAcquireAsync(cancellationToken);
        if (runLockHandle is null)
        {
            return Stop([RunLockHeldMessage], loaded.Warnings, null);
        }

        try
        {
            return await RunLockedAsync(request, loaded, tools, items, token, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            BackupOrchestratorLog.RunCancelled(logger, 0, 0);
            return new([], loaded.Warnings, []) { IsCancelled = true, IsDryRun = request.DryRun };
        }
    }

    /// <summary>
    /// Runs the part of the backup that needs the lock: empties the folder of temporary archives, discovers, applies the
    /// decisions and processes the repositories.
    /// </summary>
    /// <param name="request">The repositories the run covers, the handler of discovery decisions and the mode.</param>
    /// <param name="loaded">The configuration and its warnings.</param>
    /// <param name="tools">The located tools.</param>
    /// <param name="items">The repositories of the configuration.</param>
    /// <param name="token">The token passed to git.</param>
    /// <param name="cancellationToken">
    /// Cancels the run; a cancellation during a repository is reported in the result, one before the first repository
    /// is thrown.
    /// </param>
    /// <returns>The result of the run.</returns>
    private async Task<BackupRunResult> RunLockedAsync(
        BackupRunRequest request,
        BackupConfigLoadResult loaded,
        ToolSet tools,
        IReadOnlyList<BackupWorkItem> items,
        GitHubToken token,
        CancellationToken cancellationToken)
    {
        var config = loaded.Config;
        var filter = request.Filter;
        var temporaryFolder = pathResolver.GetTemporaryArchiveFolder(config);
        var dryRun = request.DryRun;
        if (dryRun)
        {
            BackupOrchestratorLog.DryRunStarted(logger);
            BackupOrchestratorLog.TemporaryFolderLeftUntouched(logger, temporaryFolder);
        }
        else
        {
            EmptyTemporaryFolder(temporaryFolder);
        }

        DiscoveryResult discovery;
        DiscoveryApplyResult? applied = null;
        try
        {
            discovery = await DiscoverAsync(config, filter, cancellationToken);

            // The applier always saves the configuration and may move folders, so a dry run only shows what
            // discovery found and leaves the decisions to a backup run.
            if (discovery.HasChanges && !dryRun)
            {
                applied = await decisionApplier.ApplyAsync(
                    config, discovery, request.DecisionHandler, tools.Git.Path, cancellationToken);
                items = BackupWorkItem.CreateAll(config, pathResolver);
            }
        }
        catch (BackupConfigException exception)
        {
            return Stop(exception.Errors, loaded.Warnings, exception);
        }
        catch (Exception exception) when (
            exception is GitHubApiException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            // Without a complete discovery the run cannot tell which repositories to back up, and a failed save
            // leaves the configuration in memory out of step with the file, so neither may lead to a partial run.
            return Stop([exception.Message], loaded.Warnings, exception);
        }

        if (dryRun)
        {
            items = ReactivateReturned(items, discovery);
        }

        var missing = dryRun ? discovery.Missing.Select(repository => repository.Id).ToHashSet() : [];
        var settings = new RunSettings(tools, token, temporaryFolder, dryRun);
        var work = ExcludeMatching(config, SelectIncluded(items, filter));
        var results = new List<BackupItemResult>(work.Count);
        var cancelled = await ProcessAllAsync(work, missing, discovery.Repositories, settings, results, cancellationToken);

        foreach (var repository in applied?.Skipped ?? [])
        {
            BackupOrchestratorLog.NewRepositoryNotConfirmed(logger, repository);
            results.Add(new(repository, BackupItemStatus.Skipped, null, TimeSpan.Zero, "The new repository is not confirmed for backup."));
        }

        return dryRun
            ? new(results, loaded.Warnings, [])
            {
                IsDryRun = true,
                PlannedDiscovery = discovery.HasChanges ? discovery : null,
                IsCancelled = cancelled,
            }
            : new(results, loaded.Warnings, []) { Discovery = applied, IsCancelled = cancelled };
    }

    /// <summary>
    /// Processes the repositories and their wikis in order until the work is done or the run is cancelled.
    /// </summary>
    /// <remarks>
    /// On cancellation the repository or wiki being processed is reported as <see cref="BackupItemStatus.Cancelled"/>
    /// and every repository not yet started as <see cref="BackupItemStatus.Skipped"/>, so the summary still accounts for
    /// the whole run.
    /// </remarks>
    /// <param name="work">The repositories to process.</param>
    /// <param name="missing">The ids of the repositories a dry run finds missing on GitHub.</param>
    /// <param name="discovered">The repositories GitHub reported during discovery, by id.</param>
    /// <param name="settings">The tools, the token, the temporary folder and the mode of the run.</param>
    /// <param name="results">Receives the result of each repository and wiki.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns><see langword="true"/> when the run was cancelled before all the work was done.</returns>
    private async Task<bool> ProcessAllAsync(
        List<BackupWorkItem> work,
        HashSet<long> missing,
        IReadOnlyDictionary<long, GitHubRepository> discovered,
        RunSettings settings,
        List<BackupItemResult> results,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < work.Count; index++)
        {
            var item = work[index];
            if (cancellationToken.IsCancellationRequested)
            {
                BackupOrchestratorLog.RunCancelled(logger, results.Count, work.Count - index);
                SkipNotProcessed(work, index, results);
                return true;
            }

            if (missing.Contains(item.Id))
            {
                results.Add(SkipMissing(item));
                continue;
            }

            var started = timeProvider.GetTimestamp();
            var (current, target) = (item.FullName, BackupTarget.Repository);
            try
            {
                results.Add(await ProcessAsync(item, settings, cancellationToken));
                started = timeProvider.GetTimestamp();
                (current, target) = (item.WikiFullName, BackupTarget.Wiki);
                var wiki = await ProcessWikiAsync(item, discovered, settings, cancellationToken);
                if (wiki is not null)
                {
                    results.Add(wiki);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                BackupOrchestratorLog.RunCancelled(logger, results.Count, work.Count - index - 1);
                results.Add(new(current, BackupItemStatus.Cancelled, null, timeProvider.GetElapsedTime(started), CancelledMessage)
                {
                    Target = target,
                });
                SkipNotProcessed(work, index + 1, results);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reports the repositories that the cancelled run did not start as skipped.
    /// </summary>
    /// <param name="work">The repositories of the run.</param>
    /// <param name="first">The index of the first repository not started.</param>
    /// <param name="results">Receives a result for each repository not started.</param>
    private static void SkipNotProcessed(List<BackupWorkItem> work, int first, List<BackupItemResult> results)
    {
        for (var index = first; index < work.Count; index++)
        {
            results.Add(new(work[index].FullName, BackupItemStatus.Skipped, null, TimeSpan.Zero, NotProcessedMessage));
        }
    }

    /// <summary>
    /// Treats the unavailable entries that GitHub reports again as active, as a backup run would after reactivating
    /// them.
    /// </summary>
    /// <param name="items">The repositories of the configuration.</param>
    /// <param name="discovery">What discovery found.</param>
    /// <returns>The repositories, with each returned entry active.</returns>
    private static IReadOnlyList<BackupWorkItem> ReactivateReturned(
        IReadOnlyList<BackupWorkItem> items, DiscoveryResult discovery)
    {
        if (discovery.Returned.Count == 0)
        {
            return items;
        }

        var returned = discovery.Returned.Select(repository => repository.Tracked.Id).ToHashSet();
        return [.. items.Select(item => returned.Contains(item.Id) ? item with { IsActive = true } : item)];
    }

    /// <summary>
    /// Reports a repository that a dry run finds missing on GitHub as skipped; its wiki is not processed either.
    /// </summary>
    /// <param name="item">The repository.</param>
    /// <returns>The result of the repository.</returns>
    private BackupItemResult SkipMissing(BackupWorkItem item)
    {
        using var scope = logger.BeginRepositoryScope(item.FullName);
        BackupOrchestratorLog.RepositoryMissing(logger);
        return new(
            item.FullName,
            BackupItemStatus.Skipped,
            null,
            TimeSpan.Zero,
            "The repository is missing on GitHub; a backup run marks it Unavailable.");
    }

    /// <summary>
    /// Discovers the repositories of every account, or of the account the run is limited to; a run limited to one
    /// repository skips discovery.
    /// </summary>
    /// <param name="config">The configuration to compare with.</param>
    /// <param name="filter">The repositories the run covers.</param>
    /// <param name="cancellationToken">Cancels the discovery.</param>
    /// <returns>The classified repositories, or <see cref="DiscoveryResult.Empty"/> when discovery is skipped.</returns>
    /// <exception cref="GitHubApiException">Listing the repositories failed.</exception>
    /// <exception cref="ArgumentException">The account of <paramref name="filter"/> is not tracked.</exception>
    private async Task<DiscoveryResult> DiscoverAsync(
        BackupConfig config, BackupRunFilter filter, CancellationToken cancellationToken)
    {
        if (filter.IsRepository)
        {
            BackupOrchestratorLog.DiscoverySkipped(logger, filter.RepositoryFullName!);
            return DiscoveryResult.Empty;
        }

        return await discoveryService.DiscoverAsync(config, filter.AccountLogin, cancellationToken);
    }

    /// <summary>
    /// Keeps the repositories the filter of the run covers and logs each one it leaves out.
    /// </summary>
    /// <param name="items">The repositories of the configuration.</param>
    /// <param name="filter">The filter of the run.</param>
    /// <returns>The repositories the filter keeps, in their original order.</returns>
    private List<BackupWorkItem> SelectIncluded(IReadOnlyList<BackupWorkItem> items, BackupRunFilter filter)
    {
        var kept = new List<BackupWorkItem>(items.Count);
        foreach (var item in items)
        {
            if (filter.Includes(item))
            {
                kept.Add(item);
            }
            else
            {
                BackupOrchestratorLog.RepositoryFilteredOut(logger, item.FullName);
            }
        }

        return kept;
    }

    /// <summary>
    /// Removes the repositories that an exclusion mask matches and logs a warning for each; an exclusion wins over an
    /// entry of the list.
    /// </summary>
    /// <param name="config">The configuration that holds the masks.</param>
    /// <param name="items">The repositories of the list.</param>
    /// <returns>The repositories that no mask matches, in their original order.</returns>
    private List<BackupWorkItem> ExcludeMatching(BackupConfig config, List<BackupWorkItem> items)
    {
        var kept = new List<BackupWorkItem>(items.Count);
        foreach (var item in items)
        {
            var match = exclusionMatcher.Match(config, item.Owner, item.Name);
            if (match is null)
            {
                kept.Add(item);
            }
            else
            {
                BackupOrchestratorLog.RepositoryExcluded(logger, item.FullName, match.Mask, match.Scope);
            }
        }

        return kept;
    }

    /// <summary>
    /// Logs the errors that stop the run and returns its result.
    /// </summary>
    /// <param name="errors">The errors, one sentence each.</param>
    /// <param name="configWarnings">The warnings about <c>backup-config.json</c> found so far.</param>
    /// <param name="exception">The exception that reported the errors, or <see langword="null"/> when the run found them itself.</param>
    /// <returns>The result with <see cref="BackupRunResult.CriticalErrors"/> set.</returns>
    private BackupRunResult Stop(IReadOnlyList<string> errors, IReadOnlyList<string> configWarnings, Exception? exception)
    {
        var reported = errors.Count > 0 || exception is null ? errors : [exception.Message];
        foreach (var error in reported)
        {
            BackupOrchestratorLog.RunStopped(logger, error, exception);
        }

        return BackupRunResult.Stopped(reported, configWarnings);
    }

    /// <summary>
    /// Deletes the files and folders that interrupted runs left in the folder of temporary archives.
    /// </summary>
    /// <remarks>
    /// The folder holds only archives under construction, so anything in it at the start of a run is garbage. An
    /// entry that cannot be deleted is logged and left behind: it does not prevent new archives, whose temporary names
    /// are unique. A folder that is a junction or a symbolic link is left alone, because emptying it would delete
    /// files outside the sources root.
    /// </remarks>
    /// <param name="folder">The folder of temporary archives.</param>
    private void EmptyTemporaryFolder(string folder)
    {
        if (!fileSystem.Directory.Exists(folder))
        {
            return;
        }

        if (fileSystem.DirectoryInfo.New(folder).Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            BackupOrchestratorLog.TemporaryFolderIsLink(logger, folder);
            return;
        }

        string[] entries;
        try
        {
            entries = fileSystem.Directory.GetFileSystemEntries(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            BackupOrchestratorLog.TemporaryFolderNotListed(logger, folder, exception);
            return;
        }

        foreach (var entry in entries)
        {
            try
            {
                if (fileSystem.Directory.Exists(entry))
                {
                    fileSystem.Directory.Delete(entry, recursive: true);
                }
                else
                {
                    fileSystem.File.Delete(entry);
                }

                BackupOrchestratorLog.TemporaryEntryDeleted(logger, entry);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                BackupOrchestratorLog.TemporaryEntryNotDeleted(logger, entry, exception);
            }
        }
    }

    /// <summary>
    /// Backs up one repository and turns any failure into a <see cref="BackupItemStatus.Failed"/> result.
    /// </summary>
    /// <param name="item">The repository.</param>
    /// <param name="settings">The tools, the token, the temporary folder and the mode of the run.</param>
    /// <param name="cancellationToken">Cancels the work; cancellation is rethrown, not recorded as a failure.</param>
    /// <returns>The result of the repository.</returns>
    private async Task<BackupItemResult> ProcessAsync(
        BackupWorkItem item, RunSettings settings, CancellationToken cancellationToken)
    {
        using var scope = logger.BeginRepositoryScope(item.FullName);
        var started = timeProvider.GetTimestamp();
        BackupOutcome outcome;
        if (!item.IsActive)
        {
            BackupOrchestratorLog.RepositorySkipped(logger);
            outcome = new(BackupItemStatus.Skipped, null, "The repository is marked Unavailable in backup-config.json.");
        }
        else
        {
            BackupOrchestratorLog.RepositoryStarted(logger, item.CloneFolder, item.ArchiveFolder);
            try
            {
                var source = new BackupSource(
                    BackupTarget.Repository,
                    item.FullName,
                    item.CloneFolder,
                    remoteUrlProvider.GetRepositoryUrl(item.Owner, item.Name),
                    item.Branch);
                outcome = await BackupAsync(item, source, settings, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // The failure of one repository must not stop the run, so every exception except the requested
                // cancellation becomes the result of this repository.
                BackupOrchestratorLog.RepositoryFailed(logger, exception);
                outcome = new(BackupItemStatus.Failed, null, exception.Message);
            }
        }

        var duration = timeProvider.GetElapsedTime(started);
        BackupOrchestratorLog.RepositoryFinished(logger, outcome.Status, outcome.Archive, duration);
        return outcome.ToResult(item.FullName, BackupTarget.Repository, duration);
    }

    /// <summary>
    /// Backs up the wiki of one repository when its wiki feature is enabled and turns any failure into a
    /// <see cref="BackupItemStatus.Failed"/> result.
    /// </summary>
    /// <remarks>
    /// The wiki is cloned on the server's default branch: the branch configured for the repository names a branch of
    /// the code, not of the wiki. An enabled wiki that nobody has edited has no git repository; it is logged and
    /// skipped, and its clone and archives from earlier runs are kept.
    /// </remarks>
    /// <param name="item">The repository that owns the wiki.</param>
    /// <param name="discovered">The repositories GitHub reported during discovery, by id.</param>
    /// <param name="settings">The tools, the token, the temporary folder and the mode of the run.</param>
    /// <param name="cancellationToken">Cancels the work; cancellation is rethrown, not recorded as a failure.</param>
    /// <returns>
    /// The result of the wiki, or <see langword="null"/> when the repository is not active, the wiki feature is
    /// disabled or the wiki has no git repository on the server.
    /// </returns>
    private async Task<BackupItemResult?> ProcessWikiAsync(
        BackupWorkItem item,
        IReadOnlyDictionary<long, GitHubRepository> discovered,
        RunSettings settings,
        CancellationToken cancellationToken)
    {
        if (!item.IsActive)
        {
            return null;
        }

        using var scope = logger.BeginRepositoryScope(item.WikiFullName);
        var started = timeProvider.GetTimestamp();
        try
        {
            if (!await IsWikiEnabledAsync(item, discovered, cancellationToken))
            {
                BackupOrchestratorLog.WikiDisabled(logger);
                return null;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Without the answer the wiki may go unprotected, so the failed lookup is the failure of the wiki; the
            // repository keeps its own result.
            BackupOrchestratorLog.WikiLookupFailed(logger, exception);
            return FinishWiki(item, started, new(BackupItemStatus.Failed, null, exception.Message));
        }

        BackupOrchestratorLog.RepositoryStarted(logger, item.WikiCloneFolder, item.ArchiveFolder);
        BackupOutcome outcome;
        try
        {
            var source = new BackupSource(
                BackupTarget.Wiki,
                item.WikiFullName,
                item.WikiCloneFolder,
                remoteUrlProvider.GetWikiUrl(item.Owner, item.Name),
                Branch: null);
            outcome = await BackupAsync(item, source, settings, cancellationToken);
        }
        catch (GitRepositoryNotFoundException)
        {
            BackupOrchestratorLog.WikiNotFound(logger);
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            BackupOrchestratorLog.RepositoryFailed(logger, exception);
            outcome = new(BackupItemStatus.Failed, null, exception.Message);
        }

        return FinishWiki(item, started, outcome);
    }

    /// <summary>
    /// Logs the outcome of a wiki and returns its result.
    /// </summary>
    /// <param name="item">The repository that owns the wiki.</param>
    /// <param name="started">The timestamp taken when the wiki was started.</param>
    /// <param name="outcome">What the backup of the wiki did.</param>
    /// <returns>The result of the wiki.</returns>
    private BackupItemResult FinishWiki(BackupWorkItem item, long started, BackupOutcome outcome)
    {
        var duration = timeProvider.GetElapsedTime(started);
        BackupOrchestratorLog.RepositoryFinished(logger, outcome.Status, outcome.Archive, duration);
        return outcome.ToResult(item.WikiFullName, BackupTarget.Wiki, duration);
    }

    /// <summary>
    /// Tells whether the wiki feature of a repository is enabled, from the metadata discovery collected or, when
    /// discovery did not report the repository, from the GitHub API.
    /// </summary>
    /// <param name="item">The repository.</param>
    /// <param name="discovered">The repositories GitHub reported during discovery, by id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><see langword="true"/> when the wiki feature is enabled.</returns>
    /// <exception cref="GitHubApiException">The request fails.</exception>
    /// <exception cref="ArgumentException">The owner or the name is not valid for the API.</exception>
    private async Task<bool> IsWikiEnabledAsync(
        BackupWorkItem item, IReadOnlyDictionary<long, GitHubRepository> discovered, CancellationToken cancellationToken)
    {
        if (discovered.TryGetValue(item.Id, out var known))
        {
            return known.HasWiki;
        }

        var repository = await gitHubApi.GetRepositoryAsync(item.Owner, item.Name, cancellationToken);
        return repository.HasWiki;
    }

    /// <summary>
    /// Brings the clone of a repository or wiki up to date, archives it when it changed and records the archive; in a
    /// dry run, only decides what a backup run would do and names the archive it would create.
    /// </summary>
    /// <param name="item">The active repository, which supplies the archive folder and the state file.</param>
    /// <param name="source">The repository or its wiki.</param>
    /// <param name="settings">The tools, the token, the temporary folder and the mode of the run.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// The status, the archive created or the archive a dry run predicts, the warning, the branch and its commit, and
    /// the refs that changed since the last archive when there is an archive.
    /// </returns>
    private async Task<BackupOutcome> BackupAsync(
        BackupWorkItem item, BackupSource source, RunSettings settings, CancellationToken cancellationToken)
    {
        var state = await stateStore.LoadAsync(item.ArchiveFolder, item.Id, item.FullName, cancellationToken);
        var request = new ChangeDetectionRequest
        {
            Git = new()
            {
                GitPath = settings.Tools.Git.Path,
                ClonePath = source.CloneFolder,
                Branch = source.Branch,
                Token = settings.Token.Value,
            },
            RemoteUrl = source.RemoteUrl,
            ArchiveFolder = item.ArchiveFolder,
            LastArchive = state.GetLastArchive(source.Target),
        };
        var detection = settings.DryRun
            ? await changeDetector.PreviewAsync(request, cancellationToken)
            : await changeDetector.DetectAsync(request, cancellationToken);
        if (detection.Decision == ChangeDecision.CloneNeeded)
        {
            return new(BackupItemStatus.WouldClone, null, null);
        }

        string? archive = null;
        if (detection.Decision == ChangeDecision.ArchiveNeeded)
        {
            archive = settings.DryRun
                ? nameBuilder.Build(item.Owner, item.Name, source.Target, GetHeadCommit(source, detection).Commit).FileName
                : await ArchiveAsync(item, source, settings, detection, cancellationToken);
        }

        string? warning = null;
        BackupItemStatus status;
        if (detection.BranchFallback)
        {
            warning = $"The branch '{source.Branch}' does not exist on the server; the default branch '{detection.Branch}' is used.";
            BackupOrchestratorLog.RepositoryWarning(logger, warning);
            status = BackupItemStatus.Warning;
        }
        else
        {
            status = archive is null ? BackupItemStatus.UpToDate
                : settings.DryRun ? BackupItemStatus.WouldArchive
                : BackupItemStatus.Archived;
        }

        // Only a new archive captures refs, so an item without one reports no changed refs even when the server moved.
        return new(status, archive, warning)
        {
            Branch = detection.Branch,
            HeadCommit = detection.Branch is { } branch
                && detection.Refs.Refs.TryGetValue(RefSnapshot.BranchPrefix + branch, out var commit) ? commit : null,
            ChangedRefs = archive is null ? [] : detection.Refs.GetChangedRefs(request.LastArchive?.Refs),
        };
    }

    /// <summary>
    /// Creates the archive of the synchronized clone and records it in the state file.
    /// </summary>
    /// <remarks>
    /// The state is written only after the archive is in its final place, so a failed archive never appears in the
    /// state file. From then on the archive exists, so its record is written even when the run is cancelled: without
    /// the record the next run would not know the archive and would create the same one again.
    /// </remarks>
    /// <param name="item">The repository, which supplies the archive folder and the state file.</param>
    /// <param name="source">The repository or its wiki.</param>
    /// <param name="settings">The located tools and the folder in which the archive is built.</param>
    /// <param name="detection">The result of the change detection, with the branch and the refs of the clone.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The file name of the new archive.</returns>
    /// <exception cref="InvalidOperationException">The refs of the clone hold no commit for the checked-out branch.</exception>
    private async Task<string> ArchiveAsync(
        BackupWorkItem item,
        BackupSource source,
        RunSettings settings,
        ChangeDetection detection,
        CancellationToken cancellationToken)
    {
        var (branch, headCommit) = GetHeadCommit(source, detection);
        var name = nameBuilder.Build(item.Owner, item.Name, source.Target, headCommit);
        var result = await archiver.CreateAsync(
            new()
            {
                SevenZipPath = settings.Tools.SevenZip.Path,
                SourceFolder = source.CloneFolder,
                TemporaryFolder = settings.TemporaryFolder,
                DestinationFolder = item.ArchiveFolder,
                FileName = name.FileName,
            },
            cancellationToken);

        var record = new ArchiveRecord(name.FileName, name.CreatedAt, branch, headCommit, result.SizeBytes, detection.Refs);
        await stateStore.AppendAsync(item.ArchiveFolder, item.Id, item.FullName, source.Target, record, CancellationToken.None);
        return name.FileName;
    }

    /// <summary>
    /// Returns the branch to archive and the commit it points at.
    /// </summary>
    /// <param name="source">The repository or its wiki, named in the error.</param>
    /// <param name="detection">The result of the change detection, with the branch and the refs.</param>
    /// <returns>The branch and the commit id.</returns>
    /// <exception cref="InvalidOperationException">The refs hold no commit for the branch.</exception>
    private static (string Branch, string Commit) GetHeadCommit(BackupSource source, ChangeDetection detection)
    {
        var branch = detection.Branch;
        if (branch is null || !detection.Refs.Refs.TryGetValue(RefSnapshot.BranchPrefix + branch, out var headCommit))
        {
            throw new InvalidOperationException(
                $"The clone of {source.FullName} has no commit for its checked-out branch '{branch}'.");
        }

        return (branch, headCommit);
    }

    /// <summary>
    /// Holds what every repository of one run shares.
    /// </summary>
    /// <param name="Tools">The located tools.</param>
    /// <param name="Token">The token passed to git.</param>
    /// <param name="TemporaryFolder">The folder in which archives are built.</param>
    /// <param name="DryRun">Whether the run only shows what a backup run would do.</param>
    private readonly record struct RunSettings(ToolSet Tools, GitHubToken Token, string TemporaryFolder, bool DryRun);

    /// <summary>
    /// Holds what the backup of one repository or wiki did, before its duration is known.
    /// </summary>
    /// <param name="Status">The outcome.</param>
    /// <param name="Archive">The archive created or the archive a dry run predicts, or <see langword="null"/>.</param>
    /// <param name="Message">The error, the warning or the reason for skipping, or <see langword="null"/>.</param>
    private sealed record BackupOutcome(BackupItemStatus Status, string? Archive, string? Message)
    {
        /// <summary>Gets the branch of the clone, or <see langword="null"/> when the item did not reach one.</summary>
        public string? Branch { get; init; }

        /// <summary>Gets the commit <see cref="Branch"/> points at, or <see langword="null"/>.</summary>
        public string? HeadCommit { get; init; }

        /// <summary>Gets the refs that changed since the last archive, empty without a new archive.</summary>
        public IReadOnlyList<string> ChangedRefs { get; init; } = [];

        /// <summary>
        /// Creates the result of the item.
        /// </summary>
        /// <param name="fullName">The repository as <c>owner/name</c> or its wiki as <c>owner/name.wiki</c>.</param>
        /// <param name="target">Whether the item is the repository or its wiki.</param>
        /// <param name="duration">The time spent on the item.</param>
        /// <returns>The result.</returns>
        public BackupItemResult ToResult(string fullName, BackupTarget target, TimeSpan duration) =>
            new(fullName, Status, Archive, duration, Message)
            {
                Target = target,
                Branch = Branch,
                HeadCommit = HeadCommit,
                ChangedRefs = ChangedRefs,
            };
    }

    /// <summary>
    /// Describes what one backup step works on: a repository or its wiki.
    /// </summary>
    /// <param name="Target">Whether the source is the repository or its wiki.</param>
    /// <param name="FullName">The source as <c>owner/name</c> or <c>owner/name.wiki</c>, used in messages.</param>
    /// <param name="CloneFolder">The absolute clone folder.</param>
    /// <param name="RemoteUrl">The URL git clones the source from, without credentials.</param>
    /// <param name="Branch">The branch to check out, or <see langword="null"/> for the server's default branch.</param>
    private readonly record struct BackupSource(
        BackupTarget Target, string FullName, string CloneFolder, string RemoteUrl, string? Branch);
}
