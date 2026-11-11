using System.IO.Abstractions;
using GitHubBackup.Core.Archiving;
using GitHubBackup.Core.Authentication;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Changes;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Core.Logging;
using GitHubBackup.Core.State;
using GitHubBackup.Core.Tools;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Runs;

/// <summary>
/// Implements <see cref="IBackupOrchestrator"/> for the repositories listed in <c>backup-config.json</c>.
/// </summary>
/// <param name="configStore">Reads <c>backup-config.json</c>.</param>
/// <param name="toolLocator">Locates git and 7-Zip.</param>
/// <param name="tokenProvider">Supplies the GitHub token, or anonymous access.</param>
/// <param name="pathResolver">Resolves the archive, clone and temporary folders.</param>
/// <param name="exclusionMatcher">Finds the repositories of the list that an exclusion mask removes from the run.</param>
/// <param name="remoteUrlProvider">Supplies the URL git clones each repository from.</param>
/// <param name="changeDetector">Brings each clone up to date and decides whether it needs an archive.</param>
/// <param name="nameBuilder">Names each new archive.</param>
/// <param name="archiver">Creates each archive.</param>
/// <param name="stateStore">Reads and records the archives of each repository.</param>
/// <param name="fileSystem">Empties the folder of temporary archives.</param>
/// <param name="timeProvider">Measures the time spent on each repository.</param>
/// <param name="logger">The logger.</param>
public sealed class BackupOrchestrator(
    IBackupConfigStore configStore,
    IToolLocator toolLocator,
    ITokenProvider tokenProvider,
    IRepositoryPathResolver pathResolver,
    IExclusionMatcher exclusionMatcher,
    IRemoteUrlProvider remoteUrlProvider,
    IChangeDetector changeDetector,
    IArchiveNameBuilder nameBuilder,
    IArchiver archiver,
    IBackupStateStore stateStore,
    IFileSystem fileSystem,
    TimeProvider timeProvider,
    ILogger<BackupOrchestrator> logger) : IBackupOrchestrator
{
    /// <inheritdoc />
    public async Task<BackupRunResult> RunAsync(CancellationToken cancellationToken)
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
        ToolSet tools;
        List<BackupWorkItem> items;
        try
        {
            tools = await toolLocator.LocateAsync(cancellationToken);
            items = ExcludeMatching(config, BackupWorkItem.CreateAll(config, pathResolver));
        }
        catch (Exception exception) when (exception is ToolUnavailableException or ArgumentException)
        {
            return Stop([exception.Message], loaded.Warnings, exception);
        }

        var token = await tokenProvider.GetTokenAsync(cancellationToken);
        var temporaryFolder = pathResolver.GetTemporaryArchiveFolder(config);
        EmptyTemporaryFolder(temporaryFolder);

        var results = new List<BackupItemResult>(items.Count);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ProcessAsync(item, tools, token, temporaryFolder, cancellationToken));
        }

        return new(results, loaded.Warnings, []);
    }

    /// <summary>
    /// Removes the repositories that an exclusion mask matches and logs a warning for each; an exclusion wins over an
    /// entry of the list.
    /// </summary>
    /// <param name="config">The configuration that holds the masks.</param>
    /// <param name="items">The repositories of the list.</param>
    /// <returns>The repositories that no mask matches, in their original order.</returns>
    private List<BackupWorkItem> ExcludeMatching(BackupConfig config, IReadOnlyList<BackupWorkItem> items)
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
    /// <param name="exception">The exception that reported the errors.</param>
    /// <returns>The result with <see cref="BackupRunResult.CriticalErrors"/> set.</returns>
    private BackupRunResult Stop(IReadOnlyList<string> errors, IReadOnlyList<string> configWarnings, Exception exception)
    {
        var reported = errors.Count > 0 ? errors : [exception.Message];
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
                (status, archive, message) = await BackupAsync(item, tools, token, temporaryFolder, cancellationToken);
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
    /// Brings the clone up to date, archives it when it changed and records the archive.
    /// </summary>
    /// <param name="item">The active repository.</param>
    /// <param name="tools">The located tools.</param>
    /// <param name="token">The token passed to git.</param>
    /// <param name="temporaryFolder">The folder in which archives are built.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The status, the archive created or <see langword="null"/>, and the warning or <see langword="null"/>.</returns>
    private async Task<(BackupItemStatus Status, string? Archive, string? Warning)> BackupAsync(
        BackupWorkItem item, ToolSet tools, GitHubToken token, string temporaryFolder, CancellationToken cancellationToken)
    {
        var state = await stateStore.LoadAsync(item.ArchiveFolder, item.Id, item.FullName, cancellationToken);
        var request = new ChangeDetectionRequest
        {
            Git = new()
            {
                GitPath = tools.Git.Path,
                ClonePath = item.CloneFolder,
                Branch = item.Branch,
                Token = token.Value,
            },
            RemoteUrl = remoteUrlProvider.GetRepositoryUrl(item.Owner, item.Name),
            ArchiveFolder = item.ArchiveFolder,
            LastArchive = state.GetLastArchive(BackupTarget.Repository),
        };
        var detection = await changeDetector.DetectAsync(request, cancellationToken);

        var archive = detection.Decision == ChangeDecision.ArchiveNeeded
            ? await ArchiveAsync(item, tools, detection, temporaryFolder, cancellationToken)
            : null;

        if (detection.BranchFallback)
        {
            var warning = $"The branch '{item.Branch}' does not exist on the server; the default branch '{detection.Branch}' is used.";
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
    /// <param name="item">The repository.</param>
    /// <param name="tools">The located tools.</param>
    /// <param name="detection">The result of the change detection, with the branch and the refs of the clone.</param>
    /// <param name="temporaryFolder">The folder in which the archive is built.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The file name of the new archive.</returns>
    /// <exception cref="InvalidOperationException">The refs of the clone hold no commit for the checked-out branch.</exception>
    private async Task<string> ArchiveAsync(
        BackupWorkItem item, ToolSet tools, ChangeDetection detection, string temporaryFolder, CancellationToken cancellationToken)
    {
        var branch = detection.Branch;
        if (branch is null || !detection.Refs.Refs.TryGetValue(RefSnapshot.BranchPrefix + branch, out var headCommit))
        {
            throw new InvalidOperationException(
                $"The clone of {item.FullName} has no commit for its checked-out branch '{branch}'.");
        }

        var name = nameBuilder.Build(item.Owner, item.Name, BackupTarget.Repository, headCommit);
        var result = await archiver.CreateAsync(
            new()
            {
                SevenZipPath = tools.SevenZip.Path,
                SourceFolder = item.CloneFolder,
                TemporaryFolder = temporaryFolder,
                DestinationFolder = item.ArchiveFolder,
                FileName = name.FileName,
            },
            cancellationToken);

        var record = new ArchiveRecord(name.FileName, name.CreatedAt, branch, headCommit, result.SizeBytes, detection.Refs);
        await stateStore.AppendAsync(item.ArchiveFolder, item.Id, item.FullName, BackupTarget.Repository, record, cancellationToken);
        return name.FileName;
    }
}
