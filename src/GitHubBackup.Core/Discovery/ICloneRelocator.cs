namespace GitHubBackup.Core.Discovery;

/// <summary>
/// Moves a clone to another folder, after a rename of its repository or a change of its configured folder, and points
/// it at the URL of the repository.
/// </summary>
public interface ICloneRelocator
{
    /// <summary>
    /// Moves <see cref="CloneRelocationRequest.OldFolder"/> to <see cref="CloneRelocationRequest.NewFolder"/> and sets
    /// <c>origin</c> to <see cref="CloneRelocationRequest.RemoteUrl"/>.
    /// </summary>
    /// <remarks>
    /// Failures are logged and reported in the result, never thrown, so one repository cannot stop the others. A clone
    /// is moved only after the check for local work passes; a clone with local work is not touched. A folder that
    /// already exists under the new name is never deleted and leaves the old clone in place
    /// (<see cref="CloneRelocationResult.TargetOccupied"/>), a folder that is a junction or a symbolic link is never
    /// touched, and a clone is deleted only when <see cref="CloneRelocationRequest.DeleteOnFailure"/> allows it. When
    /// both folders are the same, only <c>origin</c> changes.
    /// </remarks>
    /// <param name="request">The folders, the URL, the branch, the token and whether a failed clone may be deleted.</param>
    /// <param name="cancellationToken">Cancels the relocation.</param>
    /// <returns>What happened to the clone.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<CloneRelocation> RelocateAsync(CloneRelocationRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Decides what <see cref="RelocateAsync"/> would do, by the same checks, without moving or changing anything.
    /// </summary>
    /// <param name="request">The folders, the URL, the branch and the token.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>
    /// <see cref="CloneRelocation.Moved"/> when the clone would be moved, otherwise the outcome that would stop it.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<CloneRelocation> PreviewAsync(CloneRelocationRequest request, CancellationToken cancellationToken);
}
