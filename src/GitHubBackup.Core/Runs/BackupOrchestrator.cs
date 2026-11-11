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
        var temporaryFolder = pathResolver.GetTemporaryArchiveFolder(config);
        EmptyTemporaryFolder(temporaryFolder);

        DiscoveryResult discovery;
        DiscoveryApplyResult? applied = null;
        try
        {
            discovery = await DiscoverAsync(config, filter, cancellationToken);
            if (discovery.HasChanges)
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

        var work = ExcludeMatching(config, SelectIncluded(items, filter));
        var results = new List<BackupItemResult>(work.Count);
        foreach (var item in work)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ProcessAsync(item, tools, token, temporaryFolder, cancellationToken));
            var wiki = await ProcessWikiAsync(item, discovery.Repositories, tools, token, temporaryFolder, cancellationToken);
            if (wiki is not null)
            {
                results.Add(wiki);
            }
        }

        foreach (var repository in applied?.Skipped ?? [])
        {
            BackupOrchestratorLog.NewRepositoryNotConfirmed(logger, repository);
            results.Add(new(repository, BackupItemStatus.Skipped, null, TimeSpan.Zero, "The new repository is not confirmed for backup."));
        }

        return new(results, loaded.Warnings, []) { Discovery = applied };
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
    /// <param name="tools">The located tools.</param>
    /// <param name="token">The token passed to git.</param>
    /// <param name="temporaryFolder">The folder in which archives are built.</param>
    /// <param name="cancellationToken">Cancels the work; cancellation is rethrown, not recorded as a failure.</param>
    /// <returns>The result of the repository.</returns>
    private async Task<BackupItemResult> ProcessAsync(
        BackupWorkItem item, ToolSet tools, GitHubToken token, string temporaryFolder, CancellationToken cancellationToken)
    {
        using var scope = logger.BeginRepositoryScope(item.FullName);
        var started = timeProvider.GetTimestamp();
        BackupItemStatus status;
        string? archive = null;
        string? message;
        if (!item.IsActive)
        {
            BackupOrchestratorLog.RepositorySkipped(logger);
            status = BackupItemStatus.Skipped;
            message = "The repository is marked Unavailable in backup-config.json.";
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
                (status, archive, message) = await BackupAsync(item, source, tools, token, temporaryFolder, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // The failure of one repository must not stop the run, so every exception except the requested
                // cancellation becomes the result of this repository.
                BackupOrchestratorLog.RepositoryFailed(logger, exception);
                status = BackupItemStatus.Failed;
                message = exception.Message;
            }
        }

        var duration = timeProvider.GetElapsedTime(started);
        BackupOrchestratorLog.RepositoryFinished(logger, status, archive, duration);
        return new(item.FullName, status, archive, duration, message);
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
    /// <param name="tools">The located tools.</param>
    /// <param name="token">The token passed to git.</param>
    /// <param name="temporaryFolder">The folder in which archives are built.</param>
    /// <param name="cancellationToken">Cancels the work; cancellation is rethrown, not recorded as a failure.</param>
    /// <returns>
    /// The result of the wiki, or <see langword="null"/> when the repository is not active, the wiki feature is
    /// disabled or the wiki has no git repository on the server.
    /// </returns>
    private async Task<BackupItemResult?> ProcessWikiAsync(
        BackupWorkItem item,
        IReadOnlyDictionary<long, GitHubRepository> discovered,
        ToolSet tools,
        GitHubToken token,
        string temporaryFolder,
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
            return FinishWiki(item, started, BackupItemStatus.Failed, null, exception.Message);
        }

        BackupOrchestratorLog.RepositoryStarted(logger, item.WikiCloneFolder, item.ArchiveFolder);
        BackupItemStatus status;
        string? archive = null;
        string? message;
        try
        {
            var source = new BackupSource(
                BackupTarget.Wiki,
                item.WikiFullName,
                item.WikiCloneFolder,
                remoteUrlProvider.GetWikiUrl(item.Owner, item.Name),
                Branch: null);
            (status, archive, message) = await BackupAsync(item, source, tools, token, temporaryFolder, cancellationToken);
        }
        catch (GitRepositoryNotFoundException)
        {
            BackupOrchestratorLog.WikiNotFound(logger);
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            BackupOrchestratorLog.RepositoryFailed(logger, exception);
            status = BackupItemStatus.Failed;
            message = exception.Message;
        }

        return FinishWiki(item, started, status, archive, message);
    }

    /// <summary>
    /// Logs the outcome of a wiki and returns its result.
    /// </summary>
    /// <param name="item">The repository that owns the wiki.</param>
    /// <param name="started">The timestamp taken when the wiki was started.</param>
    /// <param name="status">The outcome.</param>
    /// <param name="archive">The archive created, or <see langword="null"/>.</param>
    /// <param name="message">The error or the warning, or <see langword="null"/>.</param>
    /// <returns>The result of the wiki.</returns>
    private BackupItemResult FinishWiki(
        BackupWorkItem item, long started, BackupItemStatus status, string? archive, string? message)
    {
        var duration = timeProvider.GetElapsedTime(started);
        BackupOrchestratorLog.RepositoryFinished(logger, status, archive, duration);
        return new(item.WikiFullName, status, archive, duration, message) { Target = BackupTarget.Wiki };
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
    /// Brings the clone of a repository or wiki up to date, archives it when it changed and records the archive.
    /// </summary>
    /// <param name="item">The active repository, which supplies the archive folder and the state file.</param>
    /// <param name="source">The repository or its wiki.</param>
    /// <param name="tools">The located tools.</param>
    /// <param name="token">The token passed to git.</param>
    /// <param name="temporaryFolder">The folder in which archives are built.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The status, the archive created or <see langword="null"/>, and the warning or <see langword="null"/>.</returns>
    private async Task<(BackupItemStatus Status, string? Archive, string? Warning)> BackupAsync(
        BackupWorkItem item,
        BackupSource source,
        ToolSet tools,
        GitHubToken token,
        string temporaryFolder,
        CancellationToken cancellationToken)
    {
        var state = await stateStore.LoadAsync(item.ArchiveFolder, item.Id, item.FullName, cancellationToken);
        var request = new ChangeDetectionRequest
        {
            Git = new()
            {
                GitPath = tools.Git.Path,
                ClonePath = source.CloneFolder,
                Branch = source.Branch,
                Token = token.Value,
            },
            RemoteUrl = source.RemoteUrl,
            ArchiveFolder = item.ArchiveFolder,
            LastArchive = state.GetLastArchive(source.Target),
        };
        var detection = await changeDetector.DetectAsync(request, cancellationToken);

        var archive = detection.Decision == ChangeDecision.ArchiveNeeded
            ? await ArchiveAsync(item, source, tools, detection, temporaryFolder, cancellationToken)
            : null;

        if (detection.BranchFallback)
        {
            var warning = $"The branch '{source.Branch}' does not exist on the server; the default branch '{detection.Branch}' is used.";
            BackupOrchestratorLog.RepositoryWarning(logger, warning);
            return (BackupItemStatus.Warning, archive, warning);
        }

        return (archive is null ? BackupItemStatus.UpToDate : BackupItemStatus.Archived, archive, null);
    }

    /// <summary>
    /// Creates the archive of the synchronized clone and records it in the state file.
    /// </summary>
    /// <remarks>
    /// The state is written only after the archive is in its final place, so a failed archive never appears in the
    /// state file.
    /// </remarks>
    /// <param name="item">The repository, which supplies the archive folder and the state file.</param>
    /// <param name="source">The repository or its wiki.</param>
    /// <param name="tools">The located tools.</param>
    /// <param name="detection">The result of the change detection, with the branch and the refs of the clone.</param>
    /// <param name="temporaryFolder">The folder in which the archive is built.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The file name of the new archive.</returns>
    /// <exception cref="InvalidOperationException">The refs of the clone hold no commit for the checked-out branch.</exception>
    private async Task<string> ArchiveAsync(
        BackupWorkItem item,
        BackupSource source,
        ToolSet tools,
        ChangeDetection detection,
        string temporaryFolder,
        CancellationToken cancellationToken)
    {
        var branch = detection.Branch;
        if (branch is null || !detection.Refs.Refs.TryGetValue(RefSnapshot.BranchPrefix + branch, out var headCommit))
        {
            throw new InvalidOperationException(
                $"The clone of {source.FullName} has no commit for its checked-out branch '{branch}'.");
        }

        var name = nameBuilder.Build(item.Owner, item.Name, source.Target, headCommit);
        var result = await archiver.CreateAsync(
            new()
            {
                SevenZipPath = tools.SevenZip.Path,
                SourceFolder = source.CloneFolder,
                TemporaryFolder = temporaryFolder,
                DestinationFolder = item.ArchiveFolder,
                FileName = name.FileName,
            },
            cancellationToken);

        var record = new ArchiveRecord(name.FileName, name.CreatedAt, branch, headCommit, result.SizeBytes, detection.Refs);
        await stateStore.AppendAsync(item.ArchiveFolder, item.Id, item.FullName, source.Target, record, cancellationToken);
        return name.FileName;
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
