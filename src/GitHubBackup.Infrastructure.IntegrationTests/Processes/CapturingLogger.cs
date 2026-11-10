using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.IntegrationTests.Processes;

/// <summary>
/// Records every entry with its message and named values exactly as a logging provider would receive them, before
/// any formatter of a sink runs; safe to use from several threads.
/// </summary>
/// <typeparam name="T">The category type of the logger.</typeparam>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly Lock _lock = new();
    private readonly List<CapturedEntry> _entries = [];

    /// <summary>Gets a copy of the entries logged so far, in order.</summary>
    public IReadOnlyList<CapturedEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var properties = state is IEnumerable<KeyValuePair<string, object?>> values
            ? values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            : [];
        var entry = new CapturedEntry(logLevel, eventId, formatter(state, exception), properties);

        lock (_lock)
        {
            _entries.Add(entry);
        }
    }
}
