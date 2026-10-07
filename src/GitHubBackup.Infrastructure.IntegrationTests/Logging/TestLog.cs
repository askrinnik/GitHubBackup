using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.IntegrationTests.Logging;

/// <summary>
/// Defines the log entries that the logging tests write.
/// </summary>
internal static partial class TestLog
{
    /// <summary>
    /// Logs <paramref name="text"/> at <paramref name="level"/>.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="level">The level of the entry.</param>
    /// <param name="text">The text, kept in the <c>Text</c> property.</param>
    [LoggerMessage(EventId = 1, Message = "{Text}")]
    public static partial void Write(ILogger logger, LogLevel level, string text);

    /// <summary>
    /// Logs a failed call whose message, properties and exception carry secrets.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The exception of the entry.</param>
    /// <param name="token">A token.</param>
    /// <param name="otherToken">Another token.</param>
    /// <param name="header">An <c>Authorization</c> header.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Calling with {Token}, {OtherToken} and {Header}")]
    public static partial void CallFailed(ILogger logger, Exception exception, string token, string otherToken, string header);
}
