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
    /// <param name="exception">The exception that reported the error.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "The backup run stopped: {Error}")]
    public static partial void RunStopped(ILogger logger, string error, Exception exception);

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
}
