using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHubBackup.App.Runs;
using GitHubBackup.Infrastructure.Logging;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Backs the live log of the application session: receives log entries from any thread and shows the most recent ones
/// on the UI thread.
/// </summary>
/// <remarks>
/// <para>
/// One instance lives as long as the main window and stays subscribed to the feed until it is disposed with the host,
/// so the entries written before a run, during it and after it all reach the log, and the screens that show it share
/// what was cleared. The feed replays the entries it kept since the start of the application on subscription.
/// </para>
/// <para>
/// Entries wait in a queue, and at most one move of the queue to <see cref="Entries"/> is posted to the UI thread at a
/// time. A chatty git process therefore costs one dispatcher call per batch rather than per line, and the batch grows
/// while the UI thread is busy, without a timer that would delay a quiet run.
/// </para>
/// </remarks>
public sealed partial class RunLogViewModel : ObservableObject, IDisposable
{
    /// <summary>The number of entries kept; older entries are dropped so a long run does not grow the memory without bound.</summary>
    public const int MaxEntries = 5000;

    /// <summary>Guards <see cref="_pending"/> and <see cref="_flushPosted"/>, which writing threads and the UI thread share.</summary>
    private readonly Lock _gate = new();

    /// <summary>The entries received and not yet shown, at most <see cref="MaxEntries"/>.</summary>
    private readonly Queue<LiveLogEntry> _pending = new();

    /// <summary>Runs the move of the waiting entries on the UI thread.</summary>
    private readonly IUiDispatcher _uiDispatcher;

    /// <summary>Whether a move of the waiting entries is posted and has not run yet.</summary>
    private bool _flushPosted;

    /// <summary>The subscription to the feed, or <see langword="null"/> once disposed.</summary>
    private IDisposable? _subscription;

    /// <summary>
    /// Subscribes to <paramref name="liveLogFeed"/>, which first delivers the entries it kept.
    /// </summary>
    /// <param name="uiDispatcher">Runs the move of the waiting entries on the UI thread.</param>
    /// <param name="liveLogFeed">Delivers the entries of the application log.</param>
    public RunLogViewModel(IUiDispatcher uiDispatcher, ILiveLogFeed liveLogFeed)
    {
        _uiDispatcher = uiDispatcher;
        _subscription = liveLogFeed.Subscribe(Add);
    }

    /// <summary>Gets the shown entries, oldest first, at most <see cref="MaxEntries"/>; changed only on the UI thread.</summary>
    public ObservableCollection<LiveLogEntry> Entries { get; } = [];

    /// <summary>
    /// Queues <paramref name="entry"/> to be shown and posts the move to the UI thread unless one is already posted.
    /// </summary>
    /// <param name="entry">The entry; it can arrive on any thread.</param>
    public void Add(LiveLogEntry entry)
    {
        lock (_gate)
        {
            _pending.Enqueue(entry);
            if (_pending.Count > MaxEntries)
            {
                _pending.Dequeue();
            }

            if (_flushPosted)
            {
                return;
            }

            _flushPosted = true;
        }

        _uiDispatcher.Post(Flush);
    }

    /// <summary>
    /// Removes the shown entries and the ones still waiting; entries that arrive later are shown. Runs on the UI thread.
    /// </summary>
    /// <remarks>
    /// A move already posted stays posted, so an entry that arrives after the clearing is shown by that move.
    /// </remarks>
    [RelayCommand]
    public void Clear()
    {
        lock (_gate)
        {
            _pending.Clear();
        }

        Entries.Clear();
    }

    /// <summary>
    /// Moves the waiting entries to <see cref="Entries"/> and drops the oldest shown entries beyond
    /// <see cref="MaxEntries"/>. Runs on the UI thread.
    /// </summary>
    private void Flush()
    {
        LiveLogEntry[] batch;
        lock (_gate)
        {
            batch = [.. _pending];
            _pending.Clear();
            _flushPosted = false;
        }

        var overflow = Entries.Count + batch.Length - MaxEntries;
        if (overflow >= Entries.Count)
        {
            Entries.Clear();
        }
        else
        {
            for (var i = 0; i < overflow; i++)
            {
                Entries.RemoveAt(0);
            }
        }

        foreach (var entry in batch)
        {
            Entries.Add(entry);
        }
    }

    /// <summary>
    /// Stops receiving entries from the feed; disposing again has no effect.
    /// </summary>
    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
    }
}
