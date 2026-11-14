using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Discovery;

/// <summary>
/// Defines the log entries of <see cref="CloneRelocator"/>.
/// </summary>
internal static partial class CloneRelocatorLog
{
    /// <summary>
    /// Logs that a clone was moved to the folder of the new name.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="oldFolder">The folder under the old name.</param>
    /// <param name="newFolder">The folder under the new name.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Moved the clone {OldFolder} to {NewFolder}")]
    public static partial void Moved(ILogger logger, string oldFolder, string newFolder);

    /// <summary>
    /// Logs that the clone folder is a junction or a symbolic link and is left alone.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="folder">The clone folder.</param>
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "The clone {Folder} is a junction or a symbolic link; it is neither moved nor deleted")]
    public static partial void CloneIsLink(ILogger logger, string folder);

    /// <summary>
    /// Logs that the folder of the new name already exists, so neither folder is touched.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="oldFolder">The folder under the old name.</param>
    /// <param name="newFolder">The existing folder under the new name.</param>
    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Cannot move the clone {OldFolder} to {NewFolder}: the target already exists. Neither folder was "
            + "changed; the user must clear the target before the renamed repository can be backed up")]
    public static partial void TargetExists(ILogger logger, string oldFolder, string newFolder);

    /// <summary>
    /// Logs that moving the clone failed.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="oldFolder">The folder under the old name.</param>
    /// <param name="newFolder">The folder under the new name.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Cannot move the clone {OldFolder} to {NewFolder}")]
    public static partial void MoveFailed(ILogger logger, string oldFolder, string newFolder, Exception exception);

    /// <summary>
    /// Logs that pointing the moved clone at the new URL failed.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="folder">The clone folder.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Cannot set the origin URL of the clone {Folder}")]
    public static partial void RemoteUrlNotSet(ILogger logger, string folder, Exception exception);

    /// <summary>
    /// Logs that a clone was deleted so that the next backup clones it again.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="folder">The deleted clone folder.</param>
    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Warning,
        Message = "Deleted the clone {Folder}; the next backup clones the repository again")]
    public static partial void DeletedForReclone(ILogger logger, string folder);

    /// <summary>
    /// Logs that a clone that could not be moved could not be deleted either.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="folder">The clone folder.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message = "Cannot delete the clone {Folder}")]
    public static partial void DeleteFailed(ILogger logger, string folder, Exception exception);

    /// <summary>
    /// Logs that the old folder holds no <c>.git</c> folder, so it is neither moved nor deleted.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="folder">The old folder.</param>
    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Information,
        Message = "The folder {Folder} holds no git clone; it is neither moved nor deleted")]
    public static partial void NotAClone(ILogger logger, string folder);

    /// <summary>
    /// Logs that the clone could not be checked for local work, so it is not moved.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="folder">The clone folder.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(
        EventId = 9,
        Level = LogLevel.Warning,
        Message = "Cannot check the clone {Folder} for local changes; it is not moved")]
    public static partial void CheckFailed(ILogger logger, string folder, Exception exception);

    /// <summary>
    /// Logs that the clone in the old folder belongs to another repository, so it is neither moved nor changed.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="folder">The old folder.</param>
    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Warning,
        Message = "The origin of the clone {Folder} points at another repository; it is neither moved nor changed")]
    public static partial void ForeignOrigin(ILogger logger, string folder);
}
