using System.IO.Abstractions;

namespace GitHubBackup.ArchitectureTests.Fixtures;

/// <summary>
/// Uses only the <c>System.IO</c> types the file system rule allows, including those whose names start like a
/// forbidden type, to prove the rule matches whole type names.
/// </summary>
public static class AllowedFileTypesFixture
{
    /// <summary>
    /// Opens a file through <paramref name="fileSystem"/> with explicit mode, access and share flags.
    /// </summary>
    /// <param name="fileSystem">The injected file system.</param>
    /// <param name="folder">The folder of the file.</param>
    /// <returns>The opened stream; the caller disposes it.</returns>
    public static Stream Open(IFileSystem fileSystem, string folder)
    {
        var path = Path.Combine(folder, "file.txt");
        return fileSystem.File.Exists(path)
            ? fileSystem.FileStream.New(path, FileMode.Open, FileAccess.Read, FileShare.Read)
            : Stream.Null;
    }
}
