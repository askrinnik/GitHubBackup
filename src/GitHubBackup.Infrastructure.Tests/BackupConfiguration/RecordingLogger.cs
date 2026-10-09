using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.Tests.BackupConfiguration;

/// <summary>
/// Records the level and the formatted message of every entry logged through it.
/// </summary>
/// <typeparam name="T">The category type of the logger.</typeparam>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    /// <summary>Gets the entries logged so far, in order.</summary>
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}
