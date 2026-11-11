using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Discovery;

/// <summary>
/// Defines the log entries of <see cref="DiscoveryDecisionApplier"/>.
/// </summary>
internal static partial class DiscoveryDecisionApplierLog
{
    /// <summary>
    /// Logs that a new repository was added to the configuration.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="repository">The <c>owner/name</c> of the repository.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Added {Repository} to the configuration")]
    public static partial void Added(ILogger logger, string repository);

    /// <summary>
    /// Logs that a new repository was excluded by a mask.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="repository">The <c>owner/name</c> of the repository.</param>
    /// <param name="mask">The mask that excludes it.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Excluded {Repository} with the mask {Mask}")]
    public static partial void Excluded(ILogger logger, string repository, string mask);

    /// <summary>
    /// Logs that a configured repository is no longer found on GitHub; its clone and archives are kept.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="repository">The <c>owner/name</c> of the repository.</param>
    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "{Repository} is not found on GitHub; it is marked unavailable and its clone and archives are kept")]
    public static partial void MarkedUnavailable(ILogger logger, string repository);

    /// <summary>
    /// Logs that an unavailable repository is found on GitHub again.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="repository">The <c>owner/name</c> of the repository.</param>
    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Information,
        Message = "{Repository} is available on GitHub again and is backed up from now on")]
    public static partial void Reactivated(ILogger logger, string repository);

    /// <summary>
    /// Logs that a renamed repository was written to the configuration under its new name.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="oldName">The <c>owner/name</c> before the rename.</param>
    /// <param name="newName">The <c>owner/name</c> after the rename.</param>
    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Renamed {OldName} to {NewName} in the configuration")]
    public static partial void Renamed(ILogger logger, string oldName, string newName);

    /// <summary>
    /// Logs that an archive folder was renamed after its repository.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="oldFolder">The folder before the rename.</param>
    /// <param name="newFolder">The folder after the rename.</param>
    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Renamed the archive folder {OldFolder} to {NewFolder}")]
    public static partial void ArchiveFolderRenamed(ILogger logger, string oldFolder, string newFolder);

    /// <summary>
    /// Logs that the archive folder keeps its name because the folder of the new name exists.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="oldFolder">The archive folder, kept in <c>path</c>.</param>
    /// <param name="newFolder">The existing folder of the new name.</param>
    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Warning,
        Message = "Cannot rename the archive folder {OldFolder}: {NewFolder} already exists; the folder keeps its name")]
    public static partial void ArchiveFolderTargetExists(ILogger logger, string oldFolder, string newFolder);

    /// <summary>
    /// Logs that the archive folder keeps its name because renaming it failed.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="oldFolder">The archive folder, kept in <c>path</c>.</param>
    /// <param name="newFolder">The folder of the new name.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Warning,
        Message = "Cannot rename the archive folder {OldFolder} to {NewFolder}; the folder keeps its name")]
    public static partial void ArchiveFolderNotRenamed(ILogger logger, string oldFolder, string newFolder, Exception exception);

    /// <summary>
    /// Logs that an archive folder renamed before a failed save could not be renamed back.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="folder">The folder under the new name.</param>
    /// <param name="originalFolder">The folder name the configuration file still refers to.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(
        EventId = 9,
        Level = LogLevel.Warning,
        Message = "Cannot rename the archive folder {Folder} back to {OriginalFolder} after the configuration was not saved")]
    public static partial void ArchiveFolderNotRestored(
        ILogger logger, string folder, string originalFolder, Exception exception);
}
