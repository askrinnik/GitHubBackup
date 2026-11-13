using GitHubBackup.App.Runs;

namespace GitHubBackup.App.Tests.Runs;

/// <summary>
/// Implements <see cref="IUiDispatcher"/> with a queue that the test runs when it chooses, which stands in for the UI
/// thread and decides when the posted work runs relative to the code under test.
/// </summary>
internal sealed class ManualUiDispatcher : IUiDispatcher
{
    /// <summary>The posted work that has not run yet, in the order it was posted.</summary>
    private readonly Queue<Action> _pending = new();

    /// <summary>Gets the number of posted actions that have not run yet.</summary>
    public int PendingCount => _pending.Count;

    /// <inheritdoc />
    public void Post(Action action) => _pending.Enqueue(action);

    /// <summary>
    /// Runs every posted action in the order it was posted, including the ones they post.
    /// </summary>
    public void RunAll()
    {
        while (_pending.TryDequeue(out var action))
        {
            action();
        }
    }
}
