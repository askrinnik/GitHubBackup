using GitHubBackup.Infrastructure.Logging;

namespace GitHubBackup.App.Tests.Logging;

/// <summary>
/// Implements <see cref="ILiveLogFeed"/> with a list of handlers that the test writes entries to, so it can see when
/// the code under test subscribes and unsubscribes.
/// </summary>
internal sealed class FakeLiveLogFeed : ILiveLogFeed
{
    /// <summary>The subscribed handlers.</summary>
    private readonly List<Action<LiveLogEntry>> _handlers = [];

    /// <summary>Gets the number of subscribed handlers.</summary>
    public int SubscriberCount => _handlers.Count;

    /// <inheritdoc />
    public IDisposable Subscribe(Action<LiveLogEntry> handler)
    {
        _handlers.Add(handler);
        return new Unsubscriber(() => _handlers.Remove(handler));
    }

    /// <summary>
    /// Delivers <paramref name="entry"/> to every subscribed handler.
    /// </summary>
    /// <param name="entry">The entry.</param>
    public void Write(LiveLogEntry entry)
    {
        foreach (var handler in _handlers.ToArray())
        {
            handler(entry);
        }
    }

    /// <summary>
    /// Runs an action once when disposed.
    /// </summary>
    /// <param name="unsubscribe">The action.</param>
    private sealed class Unsubscriber(Action unsubscribe) : IDisposable
    {
        /// <summary>Whether the action has run.</summary>
        private bool _disposed;

        /// <inheritdoc />
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                unsubscribe();
            }
        }
    }
}
