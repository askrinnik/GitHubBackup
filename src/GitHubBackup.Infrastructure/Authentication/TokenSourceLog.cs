using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.Authentication;

/// <summary>
/// Defines the log entries of <see cref="CredentialManagerTokenSource"/> and <see cref="GitHubCliTokenSource"/>. No
/// entry carries a token.
/// </summary>
internal static partial class TokenSourceLog
{
    /// <summary>
    /// Logs that the token store cannot be read.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="source">The name of the source.</param>
    /// <param name="error">The reason the read failed.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Cannot read the GitHub token from {Source}: {Error}")]
    public static partial void StoreReadFailed(ILogger logger, string source, string error);

    /// <summary>
    /// Logs that the GitHub CLI is not in <c>PATH</c>, so it is skipped.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="fileName">The executable searched for.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "{FileName} was not found in PATH; the GitHub CLI is skipped")]
    public static partial void GitHubCliNotFound(ILogger logger, string fileName);

    /// <summary>
    /// Logs that the GitHub CLI cannot be started.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The path of the executable.</param>
    /// <param name="error">The reason it cannot be started.</param>
    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "The GitHub CLI cannot be started from {Path}: {Error}")]
    public static partial void GitHubCliStartFailed(ILogger logger, string path, string error);

    /// <summary>
    /// Logs that the GitHub CLI returned no token, usually because no account is logged in.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exitCode">The exit code of the GitHub CLI.</param>
    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "The GitHub CLI returned no token (exit code {ExitCode})")]
    public static partial void GitHubCliNoToken(ILogger logger, int exitCode);
}
