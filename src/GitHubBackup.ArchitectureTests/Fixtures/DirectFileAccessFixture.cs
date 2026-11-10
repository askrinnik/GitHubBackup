namespace GitHubBackup.ArchitectureTests.Fixtures;

/// <summary>
/// A deliberate violator of the file system rule that exists only to prove the rule detects a static
/// <see cref="File"/> call.
/// </summary>
public static class DirectFileAccessFixture
{
    /// <summary>
    /// Checks a file with <see cref="File.Exists(string)"/> instead of an injected file system.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns><see langword="true"/> when the file exists.</returns>
    public static bool Exists(string path) => File.Exists(path);
}
