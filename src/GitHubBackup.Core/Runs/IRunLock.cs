namespace GitHubBackup.Core.Runs;

/// <summary>
/// Guards the backup run so that only one runs at a time on the machine.
/// </summary>
public interface IRunLock
{
    /// <summary>
    /// Takes the lock without waiting for it.
    /// </summary>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    /// <returns>
    /// A handle that releases the lock when disposed, or <see langword="null"/> when another run holds the lock.
    /// </returns>
    Task<IDisposable?> TryAcquireAsync(CancellationToken cancellationToken);
}
