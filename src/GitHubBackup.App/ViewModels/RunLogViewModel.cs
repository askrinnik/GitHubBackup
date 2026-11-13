using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GitHubBackup.App.Runs;
using GitHubBackup.Infrastructure.Logging;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Backs the live log of a run: receives log entries from any thread and shows the most recent ones on the UI thread.
/// </summary>
/// <remarks>
/// Entries wait in a queue, and at most one move of the queue to <see cref="Entries"/> is posted to the UI thread at a
/// time. A chatty git process therefore costs one dispatcher call per batch rather than per line, and the batch grows
/// while the UI thread is busy, without a timer that would delay a quiet run.
/// </remarks>
/// <param name="uiDispatcher">Runs the move of the waiting entries on the UI thread.</param>
public sealed class RunLogViewModel(IUiDispatcher uiDispatcher) : ObservableObject
{
    /// <summary>The number of entries kept; older entries are dropped so a long run does not grow the memory without bound.</summary>
    public const int MaxEntries = 5000;

    /// <summary>Guards <see cref="_pending"/> and <see cref="_flushPosted"/>, which writing threads and the UI thread share.</summary>
    private readonly Lock _gate = new();

    /// <summary>The entries received and not yet shown, at most <see cref="MaxEntries"/>.</summary>
    private readonly Queue<LiveLogEntry> _pending = new();

    /// <summary>Whether a move of the waiting entries is posted and has not run yet.</summary>
    private bool _flushPosted;

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

        uiDispatcher.Post(Flush);
    }

    /// <summary>
    /// Removes the shown entries and the ones still waiting. Runs on the UI thread.
    /// </summary>
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
}
