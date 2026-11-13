using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Holds one entry of the live log feed, with the secrets of its message already masked.
/// </summary>
/// <param name="Timestamp">The local instant the entry was written.</param>
/// <param name="Level">The level of the entry.</param>
/// <param name="Message">The rendered and masked message, followed by the message of the exception when there is one.</param>
public sealed record LiveLogEntry(DateTimeOffset Timestamp, LogLevel Level, string Message);
