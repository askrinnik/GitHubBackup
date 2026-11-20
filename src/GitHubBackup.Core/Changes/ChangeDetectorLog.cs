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
    /// <param name="branches">The number of branches on the server.</param>
    /// <param name="tags">The number of tags on the server.</param>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Change detection for {Path}: {Decision} ({Reason}), branch {Branch}, {Branches} branches and {Tags} tags on the server")]
    public static partial void Decided(
        ILogger logger, string path, ChangeDecision decision, ChangeReason reason, string? branch, int branches, int tags);

    /// <summary>
    /// Logs the decision for one clone whose server was not asked, so no ref counts exist.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The clone folder.</param>
    /// <param name="decision">The decision.</param>
    /// <param name="reason">The reason of the decision.</param>
    /// <param name="branch">The branch checked out, or <see langword="null"/> when none is.</param>
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Change detection for {Path}: {Decision} ({Reason}), branch {Branch}, server not queried")]
    public static partial void DecidedWithoutServer(
        ILogger logger, string path, ChangeDecision decision, ChangeReason reason, string? branch);
}
