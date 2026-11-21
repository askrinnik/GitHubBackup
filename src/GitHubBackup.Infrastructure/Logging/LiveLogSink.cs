using System.Collections.Immutable;
using System.Globalization;
using GitHubBackup.Core.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Passes each entry the logger writes to the subscribers of the live log feed, with secrets masked, and keeps the
/// most recent entries for the subscribers that come later.
/// </summary>
/// <remarks>
/// One lock guards the kept entries and the subscriptions, and entries are delivered while it is held. A subscriber
/// therefore receives the kept entries and then the new ones strictly in order, and an entry written while it
/// subscribes is neither lost nor delivered twice. The entry is rendered and masked before the lock is taken. Without
/// subscribers and without kept entries an entry is neither rendered nor masked.
/// </remarks>
/// <param name="masker">The masker applied to the rendered message, so a token never reaches the feed.</param>
/// <param name="options">The number of entries kept for later subscribers.</param>
internal sealed class LiveLogSink(ISecretMasker masker, IOptions<LiveLogOptions> options) : ILogEventSink, ILiveLogFeed
{
    /// <summary>Renders the message the way the text log does: strings without quotes, structures as JSON.</summary>
    private static readonly MessageTemplateTextFormatter _messageFormatter = new("{Message:lj}", CultureInfo.InvariantCulture);

    /// <summary>The number of the most recent entries kept in <see cref="_buffer"/>.</summary>
    private readonly int _capacity = options.Value.BufferCapacity;

    /// <summary>Guards <see cref="_buffer"/> and <see cref="_subscriptions"/> and serialises the delivery.</summary>
    private readonly Lock _gate = new();

    /// <summary>The kept entries, oldest first, at most <see cref="_capacity"/>.</summary>
    private readonly Queue<LiveLogEntry> _buffer = new();

    /// <summary>
    /// The current subscriptions; replaced as a whole under <see cref="_gate"/>, so <see cref="Emit"/> can check for
    /// subscribers without the lock.
    /// </summary>
    private ImmutableArray<Subscription> _subscriptions = [];

    /// <inheritdoc />
    public IDisposable Subscribe(Action<LiveLogEntry> handler)
    {
        var subscription = new Subscription(this, handler);
        lock (_gate)
        {
            // The lock is reentrant, so a handler that writes to the log adds to the buffer on this thread; the replay
            // walks a copy to stay valid.
            foreach (var entry in _buffer.ToArray())
            {
                handler(entry);
            }

            _subscriptions = _subscriptions.Add(subscription);
        }

        return subscription;
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        if (_capacity == 0 && _subscriptions.IsEmpty)
        {
            return;
        }

        var entry = new LiveLogEntry(logEvent.Timestamp, ToLogLevel(logEvent.Level), Render(logEvent));
        lock (_gate)
        {
            if (_capacity > 0)
            {
                if (_buffer.Count == _capacity)
                {
                    _buffer.Dequeue();
                }

                _buffer.Enqueue(entry);
            }

            foreach (var subscription in _subscriptions)
            {
                subscription.Handler(entry);
            }
        }
    }

    /// <summary>
    /// Returns the level of <c>Microsoft.Extensions.Logging</c> that matches a Serilog level.
    /// </summary>
    /// <param name="level">The Serilog level.</param>
    /// <returns>The matching level.</returns>
    private static LogLevel ToLogLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => LogLevel.Trace,
        LogEventLevel.Debug => LogLevel.Debug,
        LogEventLevel.Information => LogLevel.Information,
        LogEventLevel.Warning => LogLevel.Warning,
        LogEventLevel.Error => LogLevel.Error,
        _ => LogLevel.Critical,
    };

    /// <summary>
    /// Renders the message of <paramref name="logEvent"/>, followed by the type and message of its exception, and masks
    /// the result.
    /// </summary>
    /// <remarks>
    /// The stack trace stays in the log files: one line per entry keeps the feed readable.
    /// </remarks>
    /// <param name="logEvent">The entry.</param>
    /// <returns>The masked text.</returns>
    private string Render(LogEvent logEvent)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        _messageFormatter.Format(logEvent, writer);
        if (logEvent.Exception is { } exception)
        {
            writer.Write($" ({exception.GetType().Name}: {exception.Message})");
        }

        return masker.Mask(writer.ToString());
    }

    /// <summary>
    /// Removes <paramref name="subscription"/> from the subscriptions; removing it again has no effect.
    /// </summary>
    /// <remarks>
    /// The removal waits for a delivery in progress, so no entry reaches the handler after this method returns.
    /// </remarks>
    /// <param name="subscription">The subscription to remove.</param>
    private void Unsubscribe(Subscription subscription)
    {
        lock (_gate)
        {
            _subscriptions = _subscriptions.Remove(subscription);
        }
    }

    /// <summary>
    /// Holds one handler of the feed; disposing it unsubscribes the handler.
    /// </summary>
    /// <remarks>
    /// Each subscription is its own object, so the same handler subscribed twice is removed one subscription at a time.
    /// </remarks>
    /// <param name="sink">The sink the handler is subscribed to.</param>
    /// <param name="handler">The handler.</param>
    private sealed class Subscription(LiveLogSink sink, Action<LiveLogEntry> handler) : IDisposable
    {
        /// <summary>Gets the handler that receives each entry.</summary>
        public Action<LiveLogEntry> Handler { get; } = handler;

        /// <inheritdoc />
        public void Dispose() => sink.Unsubscribe(this);
    }
}
