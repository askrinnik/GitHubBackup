using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Changes;

/// <summary>
/// Defines the log entries of <see cref="ChangeDetector"/>.
/// </summary>
internal static partial class ChangeDetectorLog
{
    /// <summary>
    /// Logs the decision for one clone.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The clone folder.</param>
    /// <param name="decision">The decision.</param>
    /// <param name="reason">The reason of the decision.</param>
    /// <param name="branch">The branch checked out, or <see langword="null"/> for an empty repository.</param>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Change detection for {Path}: {Decision} ({Reason}), branch {Branch}")]
    public static partial void Decided(ILogger logger, string path, ChangeDecision decision, ChangeReason reason, string? branch);
}
