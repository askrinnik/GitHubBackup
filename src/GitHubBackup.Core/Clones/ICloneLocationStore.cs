namespace GitHubBackup.Core.Clones;

/// <summary>
/// Reads and writes <see cref="CloneLocations.FileName"/> in the clone root.
/// </summary>
public interface ICloneLocationStore
{
    /// <summary>
    /// Reads the recorded clone folders.
    /// </summary>
    /// <remarks>
    /// A missing file or one that cannot be read gives an empty set, with a warning for the unreadable file: without
    /// a record the previous folder is unknown, so no clone is moved and nothing is lost.
    /// </remarks>
    /// <param name="sourcesRoot">The absolute clone root.</param>
    /// <param name="cancellationToken">Cancels reading the file.</param>
    /// <returns>The recorded locations.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<CloneLocations> LoadAsync(string sourcesRoot, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the file atomically with <paramref name="locations"/>.
    /// </summary>
    /// <param name="sourcesRoot">The absolute clone root, created when it does not exist.</param>
    /// <param name="locations">The locations to write.</param>
    /// <param name="cancellationToken">Cancels the write before the file is replaced.</param>
    /// <returns>A task that completes when the file holds the locations.</returns>
    /// <exception cref="IOException">The file cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be written.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task SaveAsync(string sourcesRoot, CloneLocations locations, CancellationToken cancellationToken);
}
