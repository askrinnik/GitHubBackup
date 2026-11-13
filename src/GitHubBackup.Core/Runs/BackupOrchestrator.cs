using System.Diagnostics;
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
using GitHubBackup.Core.Telemetry;
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
/// <param name="discoveryService">
/// Finds the IDs of the entries that have none, and new, missing and renamed repositories.
/// </param>
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
/// <param name="timeProvider">Measures the time spent on the run and on each repository and wiki.</param>
/// <param name="telemetry">
/// Traces the run, the discovery, each repository and wiki and the archiving, and records the metrics of the run.
/// </param>
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
    BackupTelemetry telemetry,
    ILogger<BackupOrchestrator> logger) : IBackupOrchestrator
{
    /// <summary>The error that stops a run while another run holds the lock.</summary>
    public const string RunLockHeldMessage = "A backup is already running.";

    /// <summary>The message of the repository or wiki that was being processed when the run was cancelled.</summary>
    public const string CancelledMessage = "The run was cancelled while this item was being processed.";

    /// <summary>The message of each repository that a cancelled run did not start.</summary>
    public const string NotProcessedMessage = "Not processed: the run was cancelled.";

    /// <summary>The message of an entry without an ID that GitHub does not know or does not show.</summary>
    public const string RepositoryNotFoundMessage = "The repository was not found on GitHub.";

    /// <inheritdoc />
    /// <remarks>
    /// The run is traced as one span, with the discovery and each repository and wiki as its children, and every
    /// item of the result is counted, so the metrics account for the same items as the summary.
    /// </remarks>
    public async Task<BackupRunResult> RunAsync(BackupRunRequest request, CancellationToken cancellationToken)
    {
        using var activity = telemetry.StartRun(request.DryRun);
        var started = timeProvider.GetTimestamp();
        var outcome = BackupTelemetry.CancelledOutcome;
        try
        {
            var result = await RunCoreAsync(request, activity, cancellationToken);
            outcome = result.CriticalErrors.Count > 0 ? BackupTelemetry.StoppedOutcome
                : result.IsCancelled ? BackupTelemetry.CancelledOutcome
                : BackupTelemetry.CompletedOutcome;
            foreach (var item in result.Items)
            {
                telemetry.RecordItem(item, request.DryRun);
            }

            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            outcome = BackupTelemetry.FailedOutcome;
            BackupTelemetry.SetError(activity, exception);
            throw;
        }
        finally
        {
            activity?.SetTag(BackupTelemetry.RunOutcomeTag, outcome);
            telemetry.RecordRun(outcome, request.DryRun, timeProvider.GetElapsedTime(started));
        }
    }

    /// <summary>
    /// Loads the configuration, locates the tools, takes the lock and runs the backup.
    /// </summary>
    /// <param name="request">The repositories the run covers, the handler of discovery decisions and the mode.</param>
    /// <param name="run">The span of the run, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The result of the run.</returns>
    private async Task<BackupRunResult> RunCoreAsync(
        BackupRunRequest request, Activity? run, CancellationToken cancellationToken)
    {
        BackupConfigLoadResult loaded;
        try
        {
            loaded = await configStore.LoadAsync(cancellationToken);
        }
        catch (BackupConfigException exception)
        {
            return Stop(run, exception.Errors, [], exception);
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
            return Stop(run, [exception.Message], loaded.Warnings, exception);
        }

        if (filter.IsRepository && !items.Any(filter.Includes))
        {
            return Stop(run, [$"The repository '{filter.RepositoryFullName}' is not in backup-config.json."], loaded.Warnings, null);
        }

        var token = await tokenProvider.GetTokenAsync(cancellationToken);

        // The lock is taken before the folder of temporary archives is emptied, because that folder may hold the
        // archive another run is building right now.
        using var runLockHandle = await runLock.TryAcquireAsync(cancellationToken);
        if (runLockHandle is null)
        {
            return Stop(run, [RunLockHeldMessage], loaded.Warnings, null);
        }

        try
        {
            return await RunLockedAsync(request, run, loaded, tools, items, token, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            BackupOrchestratorLog.RunCancelled(logger, 0, 0);
            return new([], loaded.Warnings, []) { IsCancelled = true, IsDryRun = request.DryRun };
        }
    }

    /// <summary>
    /// Runs the part of the backup that needs the lock: empties the folder of temporary archives, resolves the missing
    /// IDs, discovers, applies the decisions and processes the repositories.
    /// </summary>
    /// <remarks>
    /// IDs are resolved before discovery, so discovery compares every resolved entry by its ID. A backup run writes the
    /// IDs together with the decisions in one save, so <c>backup-config.json.bak</c> keeps the file as it was before
    /// the run; a dry run writes only the IDs the decision handler confirms.
    /// </remarks>
    /// <param name="request">The repositories the run covers, the handler of discovery decisions and the mode.</param>
    /// <param name="run">The span of the run, or <see langword="null"/>.</param>
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
        Activity? run,
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
        RepositoryIdResolution resolution;
        IReadOnlyList<string> savedIds;
        using (var discoveryActivity = filter.IsRepository ? null : telemetry.StartDiscovery())
        {
            try
            {
                resolution = await ResolveIdsAsync(config, items, filter, cancellationToken);
                savedIds = dryRun
                    ? await SetIdsForDryRunAsync(config, resolution, request.DecisionHandler, cancellationToken)
                    : SetIds(config, resolution.Resolved);
                if (resolution.Resolved.Count > 0)
                {
                    items = BackupWorkItem.CreateAll(config, pathResolver);
                }

                discovery = await DiscoverAsync(config, filter, request.Progress, cancellationToken);

                // The applier always saves the configuration and may move folders, so a dry run only shows what
                // discovery found and leaves the decisions to a backup run.
                if (discovery.HasChanges && !dryRun)
                {
                    applied = await decisionApplier.ApplyAsync(
                        config, discovery, request.DecisionHandler, tools.Git.Path, cancellationToken);
                    items = BackupWorkItem.CreateAll(config, pathResolver);
                }

                if (!dryRun && savedIds.Count > 0 && applied?.ConfigSaved != true)
                {
                    await configStore.SaveAsync(config, cancellationToken);
                }
            }
            catch (BackupConfigException exception)
            {
                BackupTelemetry.SetError(discoveryActivity, exception);
                return Stop(run, exception.Errors, loaded.Warnings, exception);
            }
            catch (Exception exception) when (
                exception is GitHubApiException or ArgumentException or IOException or UnauthorizedAccessException)
            {
                // Without a complete discovery the run cannot tell which repositories to back up, and a failed save
                // leaves the configuration in memory out of step with the file, so neither may lead to a partial run.
                BackupTelemetry.SetError(discoveryActivity, exception);
                return Stop(run, [exception.Message], loaded.Warnings, exception);
            }
        }

        if (dryRun)
        {
            items = ReactivateReturned(items, discovery);
        }

        var missing = dryRun ? discovery.Missing.Select(repository => repository.Id).ToHashSet() : [];
        var settings = new RunSettings(tools, token, temporaryFolder, dryRun, request.Progress);
        var work = ExcludeMatching(config, SelectIncluded(items, filter));
        var results = new List<BackupItemResult>(work.Count);
        var unresolved = GetUnresolved(resolution);
        var discovered = MergeResolved(discovery.Repositories, resolution);
        var cancelled = await ProcessAllAsync(work, missing, unresolved, discovered, settings, results, cancellationToken);

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
                SavedRepositoryIds = savedIds,
            }
            : new(results, loaded.Warnings, [])
            {
                Discovery = applied,
                IsCancelled = cancelled,
                SavedRepositoryIds = savedIds,
            };
    }

    /// <summary>
    /// Looks up by name the entries without an ID that the run covers: those the filter keeps and no exclusion mask
    /// matches.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <param name="items">The repositories of the configuration.</param>
    /// <param name="filter">The filter of the run.</param>
    /// <param name="cancellationToken">Cancels the lookups.</param>
    /// <returns>The outcome, or <see cref="RepositoryIdResolution.Empty"/> when no entry of the run lacks an ID.</returns>
    /// <exception cref="GitHubApiException">A lookup failed for another reason than 404 or 403.</exception>
    private async Task<RepositoryIdResolution> ResolveIdsAsync(
        BackupConfig config, IReadOnlyList<BackupWorkItem> items, BackupRunFilter filter, CancellationToken cancellationToken)
    {
        List<BackupWorkItem> withoutId =
        [
            .. items.Where(item =>
                item.Id == 0 && filter.Includes(item) && exclusionMatcher.Match(config, item.Owner, item.Name) is null),
        ];
        return withoutId.Count == 0
            ? RepositoryIdResolution.Empty
            : await discoveryService.ResolveIdsAsync(config, withoutId, cancellationToken);
    }

    /// <summary>
    /// Sets every resolved ID in the configuration.
    /// </summary>
    /// <param name="config">The configuration to change.</param>
    /// <param name="resolved">The resolved IDs.</param>
    /// <returns>The <c>owner/name</c> of each entry whose ID was set.</returns>
    private static List<string> SetIds(BackupConfig config, IEnumerable<ResolvedRepositoryId> resolved)
    {
        var set = new List<string>();
        foreach (var entry in resolved)
        {
            if (BackupConfigEntries.SetId(config, entry.AccountLogin, entry.Owner, entry.Name, entry.Repository.Id))
            {
                set.Add(entry.FullName);
            }
        }

        return set;
    }

    /// <summary>
    /// Asks about every resolved ID, writes the confirmed ones to <c>backup-config.json</c> and keeps the others in
    /// memory for the rest of the dry run.
    /// </summary>
    /// <remarks>
    /// Every answer is collected before the file is written, so a cancelled question leaves the file unchanged. The
    /// IDs kept in memory are set after the save, so the file holds only the confirmed ones.
    /// </remarks>
    /// <param name="config">The configuration, which a dry run changes in no other way.</param>
    /// <param name="resolution">The resolved IDs.</param>
    /// <param name="handler">Confirms each ID.</param>
    /// <param name="cancellationToken">Cancels the questions and the save.</param>
    /// <returns>The <c>owner/name</c> of each entry whose ID was written.</returns>
    private async Task<IReadOnlyList<string>> SetIdsForDryRunAsync(
        BackupConfig config,
        RepositoryIdResolution resolution,
        IDiscoveryDecisionHandler handler,
        CancellationToken cancellationToken)
    {
        var confirmed = new List<ResolvedRepositoryId>();
        var declined = new List<ResolvedRepositoryId>();
        foreach (var entry in resolution.Resolved)
        {
            var save = await handler.ConfirmIdSaveAsync(new(entry.FullName, entry.Repository.Id), cancellationToken);
            (save ? confirmed : declined).Add(entry);
        }

        var saved = SetIds(config, confirmed);
        if (saved.Count > 0)
        {
            await configStore.SaveAsync(config, cancellationToken);
        }

        SetIds(config, declined);
        return saved;
    }

    /// <summary>
    /// Returns the error of each entry whose missing ID could not be resolved, by <c>owner/name</c>.
    /// </summary>
    /// <param name="resolution">The outcome of the lookups.</param>
    /// <returns>The errors, keyed without regard to case.</returns>
    private static Dictionary<string, string> GetUnresolved(RepositoryIdResolution resolution)
    {
        var unresolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fullName in resolution.NotFound)
        {
            unresolved.TryAdd(fullName, RepositoryNotFoundMessage);
        }

        foreach (var conflict in resolution.Conflicts)
        {
            unresolved.TryAdd(conflict.FullName, conflict.Reason);
        }

        return unresolved;
    }

    /// <summary>
    /// Adds the repositories found while resolving IDs to the repositories discovery reported, so the wiki of a
    /// resolved entry needs no second request.
    /// </summary>
    /// <param name="discovered">The repositories discovery reported, by ID.</param>
    /// <param name="resolution">The outcome of the lookups.</param>
    /// <returns>The repositories by ID.</returns>
    private static IReadOnlyDictionary<long, GitHubRepository> MergeResolved(
        IReadOnlyDictionary<long, GitHubRepository> discovered, RepositoryIdResolution resolution)
    {
        if (resolution.Resolved.Count == 0)
        {
            return discovered;
        }

        var merged = new Dictionary<long, GitHubRepository>(discovered);
        foreach (var entry in resolution.Resolved)
        {
            merged.TryAdd(entry.Repository.Id, entry.Repository);
        }

        return merged;
    }

    /// <summary>
    /// Reports an entry whose missing ID could not be resolved as failed; its wiki is not processed either.
    /// </summary>
    /// <param name="item">The entry.</param>
    /// <param name="message">Why the ID could not be resolved.</param>
    /// <returns>The result of the entry.</returns>
    private BackupItemResult FailUnresolved(BackupWorkItem item, string message)
    {
        using var scope = logger.BeginRepositoryScope(item.FullName);
        BackupOrchestratorLog.RepositoryIdUnresolved(logger, message);
        return new(item.FullName, BackupItemStatus.Failed, null, TimeSpan.Zero, message);
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
    /// <param name="unresolved">
    /// The error of each entry whose missing ID could not be resolved, by <c>owner/name</c>.
    /// </param>
    /// <param name="discovered">The repositories GitHub reported during discovery, by id.</param>
    /// <param name="settings">The tools, the token, the temporary folder and the mode of the run.</param>
    /// <param name="results">Receives the result of each repository and wiki.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns><see langword="true"/> when the run was cancelled before all the work was done.</returns>
    private async Task<bool> ProcessAllAsync(
        List<BackupWorkItem> work,
        HashSet<long> missing,
        Dictionary<string, string> unresolved,
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

            var stages = ItemProgress.Create(settings.Progress, item.FullName, index + 1, work.Count);
            stages?.Report(BackupProgressStage.Preparing);

            if (item.Id == 0 && unresolved.TryGetValue(item.FullName, out var reason))
            {
                results.Add(FailUnresolved(item, reason));
                continue;
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
                results.Add(await ProcessAsync(item, settings, stages, cancellationToken));
                started = timeProvider.GetTimestamp();
                (current, target) = (item.WikiFullName, BackupTarget.Wiki);
                var wikiStages = ItemProgress.Create(settings.Progress, item.WikiFullName, index + 1, work.Count);
                var wiki = await ProcessWikiAsync(item, discovered, settings, wikiStages, cancellationToken);
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
    /// <param name="progress">Receives the start of the discovery, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the discovery.</param>
    /// <returns>The classified repositories, or <see cref="DiscoveryResult.Empty"/> when discovery is skipped.</returns>
    /// <exception cref="GitHubApiException">Listing the repositories failed.</exception>
    /// <exception cref="ArgumentException">The account of <paramref name="filter"/> is not tracked.</exception>
    private async Task<DiscoveryResult> DiscoverAsync(
        BackupConfig config,
        BackupRunFilter filter,
        IProgress<BackupProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (filter.IsRepository)
        {
            BackupOrchestratorLog.DiscoverySkipped(logger, filter.RepositoryFullName!);
            return DiscoveryResult.Empty;
        }

        progress?.Report(new(BackupProgressStage.Discovering, null, 0, 0));

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
    /// Logs the errors that stop the run, marks its span failed and returns its result.
    /// </summary>
    /// <param name="run">The span of the run, or <see langword="null"/>.</param>
    /// <param name="errors">The errors, one sentence each.</param>
    /// <param name="configWarnings">The warnings about <c>backup-config.json</c> found so far.</param>
    /// <param name="exception">The exception that reported the errors, or <see langword="null"/> when the run found them itself.</param>
    /// <returns>The result with <see cref="BackupRunResult.CriticalErrors"/> set.</returns>
    private BackupRunResult Stop(
        Activity? run, IReadOnlyList<string> errors, IReadOnlyList<string> configWarnings, Exception? exception)
    {
        BackupTelemetry.SetError(run, exception);
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
    /// <param name="stages">Receives each operation on the repository, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the work; cancellation is rethrown, not recorded as a failure.</param>
    /// <returns>The result of the repository.</returns>
    private async Task<BackupItemResult> ProcessAsync(
        BackupWorkItem item,
        RunSettings settings,
        IProgress<BackupProgressStage>? stages,
        CancellationToken cancellationToken)
    {
        using var scope = logger.BeginRepositoryScope(item.FullName);
        using var activity = telemetry.StartRepository(item.FullName, BackupTarget.Repository);
        var started = timeProvider.GetTimestamp();
        BackupOutcome outcome;
        Exception? failure = null;
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
                outcome = await BackupAsync(item, source, settings, stages, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                BackupTelemetry.SetStatus(activity, BackupItemStatus.Cancelled);
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // The failure of one repository must not stop the run, so every exception except the requested
                // cancellation becomes the result of this repository.
                BackupOrchestratorLog.RepositoryFailed(logger, exception);
                outcome = new(BackupItemStatus.Failed, null, exception.Message);
                failure = exception;
            }
        }

        BackupTelemetry.SetStatus(activity, outcome.Status, failure);
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
    /// skipped, and its clone and archives from earlier runs are kept. The span of the wiki starts once the wiki is
    /// known to be enabled, so a repository without a wiki adds no span; a failed lookup gets a span of its own.
    /// </remarks>
    /// <param name="item">The repository that owns the wiki.</param>
    /// <param name="discovered">The repositories GitHub reported during discovery, by id.</param>
    /// <param name="settings">The tools, the token, the temporary folder and the mode of the run.</param>
    /// <param name="stages">
    /// Receives each operation on the wiki once the wiki is known to be enabled, or <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the work; cancellation is rethrown, not recorded as a failure.</param>
    /// <returns>
    /// The result of the wiki, or <see langword="null"/> when the repository is not active, the wiki feature is
    /// disabled or the wiki has no git repository on the server.
    /// </returns>
    private async Task<BackupItemResult?> ProcessWikiAsync(
        BackupWorkItem item,
        IReadOnlyDictionary<long, GitHubRepository> discovered,
        RunSettings settings,
        IProgress<BackupProgressStage>? stages,
        CancellationToken cancellationToken)
    {
        if (!item.IsActive)
        {
            return null;
        }

        using var scope = logger.BeginRepositoryScope(item.WikiFullName);
        var started = timeProvider.GetTimestamp();
        bool enabled;
        try
        {
            enabled = await IsWikiEnabledAsync(item, discovered, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Without the answer the wiki may go unprotected, so the failed lookup is the failure of the wiki; the
            // repository keeps its own result.
            BackupOrchestratorLog.WikiLookupFailed(logger, exception);
            using var failed = telemetry.StartRepository(item.WikiFullName, BackupTarget.Wiki);
            return FinishWiki(item, started, new(BackupItemStatus.Failed, null, exception.Message), failed, exception);
        }

        if (!enabled)
        {
            BackupOrchestratorLog.WikiDisabled(logger);
            return null;
        }

        using var activity = telemetry.StartRepository(item.WikiFullName, BackupTarget.Wiki);
        stages?.Report(BackupProgressStage.Preparing);
        BackupOrchestratorLog.RepositoryStarted(logger, item.WikiCloneFolder, item.ArchiveFolder);
        BackupOutcome outcome;
        Exception? failure = null;
        try
        {
            var source = new BackupSource(
                BackupTarget.Wiki,
                item.WikiFullName,
                item.WikiCloneFolder,
                remoteUrlProvider.GetWikiUrl(item.Owner, item.Name),
                Branch: null);
            outcome = await BackupAsync(item, source, settings, stages, cancellationToken);
        }
        catch (GitRepositoryNotFoundException)
        {
            BackupOrchestratorLog.WikiNotFound(logger);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            BackupTelemetry.SetStatus(activity, BackupItemStatus.Cancelled);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            BackupOrchestratorLog.RepositoryFailed(logger, exception);
            outcome = new(BackupItemStatus.Failed, null, exception.Message);
            failure = exception;
        }

        return FinishWiki(item, started, outcome, activity, failure);
    }

    /// <summary>
    /// Logs the outcome of a wiki, tags its span and returns its result.
    /// </summary>
    /// <param name="item">The repository that owns the wiki.</param>
    /// <param name="started">The timestamp taken when the wiki was started.</param>
    /// <param name="outcome">What the backup of the wiki did.</param>
    /// <param name="activity">The span of the wiki, or <see langword="null"/>.</param>
    /// <param name="failure">The exception that failed the wiki, or <see langword="null"/>.</param>
    /// <returns>The result of the wiki.</returns>
    private BackupItemResult FinishWiki(
        BackupWorkItem item, long started, BackupOutcome outcome, Activity? activity, Exception? failure)
    {
        BackupTelemetry.SetStatus(activity, outcome.Status, failure);
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
    /// <param name="stages">Receives each git operation and the archiving, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// The status, the archive created or the archive a dry run predicts, the warning, the branch and its commit, and
    /// the refs that changed since the last archive when there is an archive.
    /// </returns>
    private async Task<BackupOutcome> BackupAsync(
        BackupWorkItem item,
        BackupSource source,
        RunSettings settings,
        IProgress<BackupProgressStage>? stages,
        CancellationToken cancellationToken)
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
            OperationProgress = stages,
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
            if (!settings.DryRun)
            {
                stages?.Report(BackupProgressStage.Archiving);
            }

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
    private Task<string> ArchiveAsync(
        BackupWorkItem item,
        BackupSource source,
        RunSettings settings,
        ChangeDetection detection,
        CancellationToken cancellationToken) =>
        telemetry.TraceOperationAsync(BackupTelemetry.ArchiveOperation, async () =>
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
            telemetry.RecordArchiveSize(result.SizeBytes, source.Target);

            var record = new ArchiveRecord(name.FileName, name.CreatedAt, branch, headCommit, result.SizeBytes, detection.Refs);
            await stateStore.AppendAsync(item.ArchiveFolder, item.Id, item.FullName, source.Target, record, CancellationToken.None);
            return name.FileName;
        });

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
    /// <param name="Progress">Receives the current repository and operation, or <see langword="null"/>.</param>
    private readonly record struct RunSettings(
        ToolSet Tools, GitHubToken Token, string TemporaryFolder, bool DryRun, IProgress<BackupProgress>? Progress);

    /// <summary>
    /// Turns the operations on one repository or wiki into <see cref="BackupProgress"/> reports that name the item and
    /// its position in the run.
    /// </summary>
    /// <param name="progress">Receives the reports.</param>
    /// <param name="repository">The repository as <c>owner/name</c> or its wiki as <c>owner/name.wiki</c>.</param>
    /// <param name="index">The 1-based position of the repository in the run.</param>
    /// <param name="total">The number of repositories of the run.</param>
    private sealed class ItemProgress(IProgress<BackupProgress> progress, string repository, int index, int total)
        : IProgress<BackupProgressStage>
    {
        /// <summary>
        /// Creates the receiver of the operations on one item, or none when the run reports no progress.
        /// </summary>
        /// <param name="progress">Receives the reports of the run, or <see langword="null"/>.</param>
        /// <param name="repository">The repository as <c>owner/name</c> or its wiki as <c>owner/name.wiki</c>.</param>
        /// <param name="index">The 1-based position of the repository in the run.</param>
        /// <param name="total">The number of repositories of the run.</param>
        /// <returns>The receiver, or <see langword="null"/> when <paramref name="progress"/> is <see langword="null"/>.</returns>
        public static ItemProgress? Create(IProgress<BackupProgress>? progress, string repository, int index, int total) =>
            progress is null ? null : new(progress, repository, index, total);

        /// <inheritdoc />
        public void Report(BackupProgressStage value) => progress.Report(new(value, repository, index, total));
    }

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
