using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.Runs;

/// <summary>
/// Defines the log entries of <see cref="NamedMutexRunLock"/>.
/// </summary>
internal static partial class NamedMutexRunLockLog
{
    /// <summary>
    /// Logs that the previous owner of the mutex ended without releasing it, so this run takes the lock over.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="name">The name of the mutex.</param>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "The run lock {Name} was abandoned by a run that ended unexpectedly; this run takes it over")]
    public static partial void Abandoned(ILogger logger, string name);

    /// <summary>
    /// Logs that the mutex exists but this user may not open it, so the lock counts as held by another run.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="name">The name of the mutex.</param>
    /// <param name="exception">The reason.</param>
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Access to the run lock {Name} is denied; a run of another user is assumed to hold it")]
    public static partial void AccessDenied(ILogger logger, string name, Exception exception);
}
