namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Delivers the entries of the application log as they are written, at the same levels as the log files, so a window
/// can show the log of the session while it works.
/// </summary>
public interface ILiveLogFeed
{
    /// <summary>
    /// Delivers the kept recent entries to <paramref name="handler"/>, oldest first, and then every new entry.
    /// </summary>
    /// <remarks>
    /// The number of kept entries is set by the host through <see cref="LiveLogOptions.BufferCapacity"/>; with none
    /// kept, only new entries arrive. An entry written while the subscription starts arrives exactly once and in order.
    /// The handler runs while the feed holds its lock, on the thread that writes the entry, which can be any thread; it
    /// must return quickly, must not wait for another thread that writes to the log and should not write to the log
    /// itself.
    /// </remarks>
    /// <param name="handler">Receives each entry.</param>
    /// <returns>
    /// The subscription; disposing it stops the delivery, no entry arrives after the disposal returns, and disposing it
    /// again has no effect.
    /// </returns>
    IDisposable Subscribe(Action<LiveLogEntry> handler);
}
