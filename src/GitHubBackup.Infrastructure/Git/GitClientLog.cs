using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.Git;

/// <summary>
/// Defines the log entries of <see cref="GitClient"/>.
/// </summary>
internal static partial class GitClientLog
{
    /// <summary>
    /// Logs that a repository is about to be cloned.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="url">The remote URL, with secrets masked.</param>
    /// <param name="path">The clone folder.</param>
    /// <param name="branch">The requested branch, or a label for the server's default branch.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Cloning {Url} into {Path}, branch {Branch}")]
    public static partial void Cloning(ILogger logger, string url, string path, string branch);

    /// <summary>
    /// Logs that a clone is about to be updated from its server.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The clone folder.</param>
    /// <param name="branch">The requested branch, or a label for the server's default branch.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Updating {Path}, branch {Branch}")]
    public static partial void Updating(ILogger logger, string path, string branch);

    /// <summary>
    /// Logs that the requested branch is missing on the server and the default branch is checked out instead.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="branch">The requested branch.</param>
    /// <param name="path">The clone folder.</param>
    /// <param name="defaultBranch">The server's default branch.</param>
    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Branch {Branch} does not exist on the server of {Path}; checking out the default branch {DefaultBranch}")]
    public static partial void BranchNotFound(ILogger logger, string branch, string path, string defaultBranch);

    /// <summary>
    /// Logs that the server has no branch, so nothing is checked out.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The clone folder.</param>
    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Information,
        Message = "The repository of {Path} is empty on the server; nothing to check out")]
    public static partial void EmptyRepository(ILogger logger, string path);

    /// <summary>
    /// Logs that no branch was requested and the server's default branch is checked out.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The clone folder.</param>
    /// <param name="branch">The server's default branch.</param>
    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Information,
        Message = "Checking out the default branch {Branch} in {Path}")]
    public static partial void UsingDefaultBranch(ILogger logger, string path, string branch);

    /// <summary>
    /// Logs that <c>origin</c> of a clone is about to be pointed at another URL.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The clone folder.</param>
    /// <param name="url">The new remote URL, with secrets masked.</param>
    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Setting the origin of {Path} to {Url}")]
    public static partial void SettingRemoteUrl(ILogger logger, string path, string url);
}
