using System.Diagnostics;

namespace GitHubBackup.ArchitectureTests.Fixtures;

/// <summary>
/// A deliberate violator of the process rule that exists only to prove the rule detects a direct process start.
/// </summary>
public static class ProcessStartingFixture
{
    /// <summary>
    /// Starts <paramref name="fileName"/> with <see cref="Process"/> instead of the process runner.
    /// </summary>
    /// <param name="fileName">The path of the executable.</param>
    /// <returns>The started process, or <see langword="null"/>.</returns>
    public static Process? Start(string fileName) => Process.Start(new ProcessStartInfo(fileName));
}
