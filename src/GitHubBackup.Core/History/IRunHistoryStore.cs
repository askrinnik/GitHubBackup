namespace GitHubBackup.Core.History;

/// <summary>
/// Stores finished backup runs in the run history.
/// </summary>
public interface IRunHistoryStore
{
    /// <summary>
    /// Adds a run with its items and discovery events, all or nothing.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>A task that completes when the run is stored.</returns>
    /// <exception cref="RunHistoryException">
    /// The history cannot be written, or it already holds a run with <see cref="RunHistoryEntry.RunId"/>.
    /// </exception>
    Task AddAsync(RunHistoryEntry run, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a run with its items and discovery events in the order they were added.
    /// </summary>
    /// <param name="runId">The identifier of the run.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The run, or <see langword="null"/> when the history holds no run with <paramref name="runId"/>.</returns>
    /// <exception cref="RunHistoryException">The history cannot be read.</exception>
    Task<RunHistoryEntry?> GetAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the runs that <paramref name="query"/> selects, each with its items and discovery events in the order
    /// they were added.
    /// </summary>
    /// <remarks>
    /// The period compares instants, so runs recorded with different offsets from UTC are selected by the moment they
    /// started.
    /// </remarks>
    /// <param name="query">The filter of the runs.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The runs, the most recently recorded first; empty when none matches.</returns>
    /// <exception cref="RunHistoryException">The history cannot be read.</exception>
    Task<IReadOnlyList<RunHistoryEntry>> QueryAsync(RunHistoryQuery query, CancellationToken cancellationToken);
}
