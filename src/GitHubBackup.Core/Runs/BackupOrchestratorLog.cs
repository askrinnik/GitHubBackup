using GitHubBackup.Core.BackupConfiguration;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Runs;

/// <summary>
/// Defines the log entries of <see cref="BackupOrchestrator"/>.
/// </summary>
internal static partial class BackupOrchestratorLog
{
    /// <summary>
    /// Logs an error that stops the run before any repository is processed.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="error">The error, one sentence.</param>
    /// <param name="exception">The exception that reported the error, or <see langword="null"/>.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "The backup run stopped: {Error}")]
    public static partial void RunStopped(ILogger logger, string error, Exception? exception);

    /// <summary>
    /// Logs a file or folder left in the folder of temporary archives by an interrupted run and deleted.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The deleted file or folder.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Deleted the leftover temporary archive {Path}")]
    public static partial void TemporaryEntryDeleted(ILogger logger, string path);

    /// <summary>
    /// Logs a leftover in the folder of temporary archives that cannot be deleted; the run goes on.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The file or folder.</param>
    /// <param name="exception">The reason.</param>
    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Cannot delete the leftover temporary archive {Path}")]
    public static partial void TemporaryEntryNotDeleted(ILogger logger, string path, Exception exception);

    /// <summary>
    /// Logs that the folder of temporary archives cannot be listed; the run goes on without emptying it.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The folder.</param>
    /// <param name="exception">The reason.</param>
    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Cannot list the temporary archive folder {Path}")]
    public static partial void TemporaryFolderNotListed(ILogger logger, string path, Exception exception);

    /// <summary>
    /// Logs that the folder of temporary archives is a junction or a symbolic link; the run goes on without emptying
    /// it.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The folder.</param>
    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Warning,
        Message = "The temporary archive folder {Path} is a link and is not emptied")]
    public static partial void TemporaryFolderIsLink(ILogger logger, string path);

    /// <summary>
    /// Logs the start of the backup of a repository.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="cloneFolder">The clone folder.</param>
    /// <param name="archiveFolder">The archive folder.</param>
    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Information,
        Message = "Repository backup started: clone {CloneFolder}, archives {ArchiveFolder}")]
    public static partial void RepositoryStarted(ILogger logger, string cloneFolder, string archiveFolder);

    /// <summary>
    /// Logs a repository that is not processed because it is marked unavailable.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Information,
        Message = "Repository skipped: it is marked Unavailable in backup-config.json")]
    public static partial void RepositorySkipped(ILogger logger);

    /// <summary>
    /// Logs a repository of the list that is not backed up because an exclusion mask matches it.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="repository">The repository as <c>owner/name</c>.</param>
    /// <param name="mask">The mask that matches.</param>
    /// <param name="scope">The list the mask belongs to.</param>
    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Warning,
        Message = "Repository {Repository} is excluded by the {Scope} mask '{Mask}' and is not backed up")]
    public static partial void RepositoryExcluded(ILogger logger, string repository, string mask, ExclusionScope scope);

    /// <summary>
    /// Logs a warning about a repository, such as the default branch used instead of a missing one.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="warning">The warning.</param>
    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message = "{Warning}")]
    public static partial void RepositoryWarning(ILogger logger, string warning);

    /// <summary>
    /// Logs the failure of a repository; the run goes on with the next one.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(EventId = 8, Level = LogLevel.Error, Message = "Repository backup failed")]
    public static partial void RepositoryFailed(ILogger logger, Exception exception);

    /// <summary>
    /// Logs the outcome of a repository.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="status">The outcome.</param>
    /// <param name="archive">The archive created, or <see langword="null"/>.</param>
    /// <param name="duration">The time spent on the repository.</param>
    [LoggerMessage(
        EventId = 9,
        Level = LogLevel.Information,
        Message = "Repository backup finished: {Status}, archive {Archive}, in {Duration}")]
    public static partial void RepositoryFinished(ILogger logger, BackupItemStatus status, string? archive, TimeSpan duration);

    /// <summary>
    /// Logs that the wiki of a repository is not backed up because the wiki feature is disabled.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(EventId = 12, Level = LogLevel.Debug, Message = "Wiki skipped: the wiki feature is disabled")]
    public static partial void WikiDisabled(ILogger logger);

    /// <summary>
    /// Logs that the wiki feature is enabled but the server has no wiki repository, so there is nothing to back up.
    /// </summary>
    /// <remarks>
    /// GitHub gives the same answer when the token cannot read a private repository, so the message names both causes.
    /// </remarks>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = 13,
        Level = LogLevel.Information,
        Message = "Wiki skipped: the wiki is enabled but the server reports no wiki git repository, or the token cannot access it")]
    public static partial void WikiNotFound(ILogger logger);

    /// <summary>
    /// Logs that the GitHub API cannot tell whether the wiki feature is enabled; the wiki is reported as failed.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure of the request.</param>
    [LoggerMessage(
        EventId = 14,
        Level = LogLevel.Error,
        Message = "Cannot read from the GitHub API whether the wiki is enabled")]
    public static partial void WikiLookupFailed(ILogger logger, Exception exception);

    /// <summary>
    /// Logs that discovery is not run because the run is limited to one repository.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="repository">The repository as <c>owner/name</c>.</param>
    [LoggerMessage(
        EventId = 15,
        Level = LogLevel.Information,
        Message = "Discovery skipped: the run is limited to the repository {Repository}")]
    public static partial void DiscoverySkipped(ILogger logger, string repository);

    /// <summary>
    /// Logs a repository of the configuration that the filter of the run leaves out.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="repository">The repository as <c>owner/name</c>.</param>
    [LoggerMessage(
        EventId = 16,
        Level = LogLevel.Debug,
        Message = "Repository {Repository} is outside the filter of the run and is not processed")]
    public static partial void RepositoryFilteredOut(ILogger logger, string repository);

    /// <summary>
    /// Logs a new repository that the decision left out of the configuration, so it is reported as skipped.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="repository">The repository as <c>owner/name</c>.</param>
    [LoggerMessage(
        EventId = 17,
        Level = LogLevel.Information,
        Message = "New repository {Repository} is not confirmed and is not backed up")]
    public static partial void NewRepositoryNotConfirmed(ILogger logger, string repository);

    /// <summary>
    /// Logs that the run is a dry run, which shows what a backup run would do and changes nothing.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = 18,
        Level = LogLevel.Information,
        Message = "Dry run started: nothing is cloned, updated, archived or saved")]
    public static partial void DryRunStarted(ILogger logger);

    /// <summary>
    /// Logs that a dry run leaves the folder of temporary archives as it is.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The folder.</param>
    [LoggerMessage(
        EventId = 19,
        Level = LogLevel.Information,
        Message = "Dry run: the temporary archive folder {Path} is left untouched")]
    public static partial void TemporaryFolderLeftUntouched(ILogger logger, string path);

    /// <summary>
    /// Logs a repository that a dry run finds missing on GitHub; a backup run would mark it unavailable.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = 20,
        Level = LogLevel.Information,
        Message = "Dry run: the repository is missing on GitHub and a backup run would mark it Unavailable")]
    public static partial void RepositoryMissing(ILogger logger);

    /// <summary>
    /// Logs that the run was cancelled before it finished.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="processed">The number of repositories and wikis finished before the cancellation.</param>
    /// <param name="notProcessed">The number of repositories not started.</param>
    [LoggerMessage(
        EventId = 21,
        Level = LogLevel.Warning,
        Message = "The backup run was cancelled: {Processed} items finished, {NotProcessed} repositories not processed")]
    public static partial void RunCancelled(ILogger logger, int processed, int notProcessed);
}
