namespace GitHubBackup.Core.Discovery;

/// <summary>
/// Moves the clone of a renamed repository to the folder of its new name and points it at the new URL.
/// </summary>
public interface ICloneRelocator
{
    /// <summary>
    /// Moves <paramref name="oldFolder"/> to <paramref name="newFolder"/> and sets <c>origin</c> to
    /// <paramref name="remoteUrl"/>; when either step fails, deletes the clone so that the next backup clones it again.
    /// </summary>
    /// <remarks>
    /// Failures are logged and reported in the result, never thrown, so one repository cannot stop the others. A
    /// folder that already exists under the new name is never deleted, and a folder that is a junction or a symbolic
    /// link is never touched.
    /// </remarks>
    /// <param name="gitPath">The fully qualified path of the git executable.</param>
    /// <param name="oldFolder">The absolute clone folder under the old name.</param>
    /// <param name="newFolder">The absolute clone folder under the new name.</param>
    /// <param name="remoteUrl">The URL of the repository under the new name, without credentials.</param>
    /// <param name="cancellationToken">Cancels the relocation.</param>
    /// <returns>What happened to the clone.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<CloneRelocationResult> RelocateAsync(
        string gitPath, string oldFolder, string newFolder, string remoteUrl, CancellationToken cancellationToken);
}
