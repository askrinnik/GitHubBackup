namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Delivers the entries of the application log as they are written, at the same levels as the log files, so a window
/// can show a run while it works.
/// </summary>
public interface ILiveLogFeed
{
    /// <summary>
    /// Starts delivering every new entry to <paramref name="handler"/>.
    /// </summary>
    /// <remarks>
    /// The handler runs on the thread that writes the entry, which can be any thread, and must return quickly.
    /// </remarks>
    /// <param name="handler">Receives each entry.</param>
    /// <returns>The subscription; disposing it stops the delivery, and disposing it again has no effect.</returns>
    IDisposable Subscribe(Action<LiveLogEntry> handler);
}
