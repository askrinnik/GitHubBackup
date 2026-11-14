namespace GitHubBackup.Core.Clones;

/// <summary>
/// Reports that a clone holds local work, so the update or the move that would overwrite it is not performed.
/// </summary>
/// <param name="result">The failed check, whose <see cref="CloneCheckResult.Message"/> becomes the message.</param>
public sealed class LocalChangesException(CloneCheckResult result) : Exception(result.Message)
{
    /// <summary>Gets the failed check.</summary>
    public CloneCheckResult Result { get; } = result;
}
