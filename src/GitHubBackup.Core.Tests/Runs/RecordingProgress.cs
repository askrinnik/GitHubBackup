namespace GitHubBackup.Core.Tests.Runs;

/// <summary>
/// Implements <see cref="IProgress{T}"/> by keeping every reported value, in the order it was reported, on the
/// reporting thread.
/// </summary>
/// <typeparam name="T">The type of the reported value.</typeparam>
internal sealed class RecordingProgress<T> : IProgress<T>
{
    /// <summary>Gets the reported values.</summary>
    public List<T> Values { get; } = [];

    /// <inheritdoc />
    public void Report(T value) => Values.Add(value);
}
