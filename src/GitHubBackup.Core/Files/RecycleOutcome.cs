namespace GitHubBackup.Core.Files;

/// <summary>
/// Tells whether a file went to the Recycle Bin and, when it did not, why.
/// </summary>
/// <param name="Succeeded">Whether the file is in the Recycle Bin and no longer at its path.</param>
/// <param name="Error">
/// Why the file stays where it is, one sentence; <see langword="null"/> when <paramref name="Succeeded"/> is
/// <see langword="true"/>.
/// </param>
public sealed record RecycleOutcome(bool Succeeded, string? Error)
{
    /// <summary>Gets the outcome of a file that went to the Recycle Bin.</summary>
    public static RecycleOutcome Success { get; } = new(true, null);

    /// <summary>
    /// Creates the outcome of a file that stays where it is.
    /// </summary>
    /// <param name="error">Why the file was not moved, one sentence.</param>
    /// <returns>The failed outcome.</returns>
    public static RecycleOutcome Failed(string error) => new(false, error);
}
