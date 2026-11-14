namespace GitHubBackup.Core.Discovery;

/// <summary>
/// Reports what <see cref="ICloneRelocator"/> did, or would do, with one clone.
/// </summary>
/// <param name="Result">The outcome.</param>
/// <param name="Reason">
/// The sentence that explains any other outcome, naming the folders; <see langword="null"/> for
/// <see cref="CloneRelocationResult.Moved"/> and <see cref="CloneRelocationResult.NotPresent"/>.
/// </param>
public sealed record CloneRelocation(CloneRelocationResult Result, string? Reason)
{
    /// <summary>Gets the outcome of a clone that is in the new folder with its new URL.</summary>
    public static CloneRelocation Moved { get; } = new(CloneRelocationResult.Moved, null);

    /// <summary>Gets the outcome of a folder that holds no clone to move.</summary>
    public static CloneRelocation NotPresent { get; } = new(CloneRelocationResult.NotPresent, null);

    /// <summary>
    /// Gets a value indicating whether the clone, if there is one, is in the new folder, so the backup can go on there.
    /// </summary>
    public bool Succeeded => Result is CloneRelocationResult.Moved or CloneRelocationResult.NotPresent;
}
