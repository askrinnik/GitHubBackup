using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.IntegrationTests.Processes;

/// <summary>
/// One entry recorded by <see cref="CapturingLogger{T}"/>, as the logger received it.
/// </summary>
/// <param name="Level">The level of the entry.</param>
/// <param name="EventId">The event id of the entry.</param>
/// <param name="Message">The formatted message.</param>
/// <param name="Properties">The named values of the entry.</param>
internal sealed record CapturedEntry(
    LogLevel Level, EventId EventId, string Message, IReadOnlyDictionary<string, object?> Properties);
