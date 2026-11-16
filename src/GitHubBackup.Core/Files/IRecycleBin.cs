namespace GitHubBackup.Core.Files;

/// <summary>
/// Moves files to the Windows Recycle Bin, from which the user can restore them.
/// </summary>
public interface IRecycleBin
{
    /// <summary>
    /// Moves one file to the Recycle Bin.
    /// </summary>
    /// <remarks>
    /// The file is never deleted permanently: when the Recycle Bin cannot take it (the volume has none, the Recycle
    /// Bin is turned off or too small for the file), the file stays at its path and the outcome is a failure.
    /// </remarks>
    /// <param name="path">The absolute path of the file.</param>
    /// <param name="cancellationToken">Cancels before the file is moved.</param>
    /// <returns>Whether the file went to the Recycle Bin and, when it did not, why.</returns>
    Task<RecycleOutcome> RecycleAsync(string path, CancellationToken cancellationToken);
}
