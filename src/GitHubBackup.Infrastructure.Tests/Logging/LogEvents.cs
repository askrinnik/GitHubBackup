using Serilog.Events;
using Serilog.Parsing;

namespace GitHubBackup.Infrastructure.Tests.Logging;

/// <summary>
/// Creates Serilog log entries for formatter and enricher tests.
/// </summary>
internal static class LogEvents
{
    /// <summary>The fixed instant of every created entry.</summary>
    public static readonly DateTimeOffset Timestamp = new(2026, 10, 7, 12, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// Creates an <see cref="LogEventLevel.Information"/> entry.
    /// </summary>
    /// <param name="messageTemplate">The message template.</param>
    /// <param name="exception">The exception of the entry, if any.</param>
    /// <param name="properties">The properties of the entry, as names and scalar values.</param>
    /// <returns>The entry.</returns>
    public static LogEvent Create(string messageTemplate, Exception? exception = null, params (string Name, object? Value)[] properties) =>
        new(
            Timestamp,
            LogEventLevel.Information,
            exception,
            new MessageTemplateParser().Parse(messageTemplate),
            properties.Select(property => new LogEventProperty(property.Name, new ScalarValue(property.Value))));
}
