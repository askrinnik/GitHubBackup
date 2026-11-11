using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Tests.Logging;

/// <summary>
/// Records the level and the formatted message of every entry written to it.
/// </summary>
/// <typeparam name="T">The category type.</typeparam>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    /// <summary>
    /// Gets the entries in the order they were written.
    /// </summary>
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception)));
}
