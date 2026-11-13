using System.Collections.Immutable;
using System.Globalization;
using GitHubBackup.Core.Security;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Passes each entry the logger writes to the subscribers of the live log feed, with secrets masked.
/// </summary>
/// <remarks>
/// The subscribers are kept in an immutable array that is swapped on every change, so writing threads read a stable
/// snapshot without a lock while a window subscribes or unsubscribes. Without subscribers an entry is neither rendered
/// nor masked.
/// </remarks>
/// <param name="masker">The masker applied to the rendered message, so a token never reaches the feed.</param>
internal sealed class LiveLogSink(ISecretMasker masker) : ILogEventSink, ILiveLogFeed
{
    /// <summary>Renders the message the way the text log does: strings without quotes, structures as JSON.</summary>
    private static readonly MessageTemplateTextFormatter _messageFormatter = new("{Message:lj}", CultureInfo.InvariantCulture);

    /// <summary>The current subscriptions; replaced as a whole, never changed in place.</summary>
    private ImmutableArray<Subscription> _subscriptions = [];

    /// <inheritdoc />
    public IDisposable Subscribe(Action<LiveLogEntry> handler)
    {
        var subscription = new Subscription(this, handler);
        ImmutableInterlocked.Update(ref _subscriptions, static (current, added) => current.Add(added), subscription);
        return subscription;
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        var subscriptions = _subscriptions;
        if (subscriptions.IsEmpty)
        {
            return;
        }

        var entry = new LiveLogEntry(logEvent.Timestamp, ToLogLevel(logEvent.Level), Render(logEvent));
        foreach (var subscription in subscriptions)
        {
            subscription.Handler(entry);
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
    /// <param name="subscription">The subscription to remove.</param>
    private void Unsubscribe(Subscription subscription) =>
        ImmutableInterlocked.Update(ref _subscriptions, static (current, removed) => current.Remove(removed), subscription);

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
