using System.IO.Abstractions;

namespace GitHubBackup.Infrastructure.Tools;

/// <summary>
/// Finds an executable in the folders listed in the <c>PATH</c> variable.
/// </summary>
/// <remarks>
/// Entries that are empty or not fully qualified are skipped, so the current directory is never searched: a file
/// planted in the folder the application runs from must not be started in place of the tool.
/// </remarks>
internal static class ExecutableSearchPath
{
    /// <summary>
    /// Returns the path of the first <paramref name="fileName"/> that exists in a folder of
    /// <paramref name="pathVariable"/>.
    /// </summary>
    /// <param name="fileSystem">Checks that a candidate exists.</param>
    /// <param name="pathVariable">The value of <c>PATH</c>: folders separated by <c>;</c>, each possibly quoted.</param>
    /// <param name="fileName">The file name of the executable, such as <c>git.exe</c>.</param>
    /// <returns>The fully qualified path of the executable, or <see langword="null"/> when no folder has it.</returns>
    public static string? Find(IFileSystem fileSystem, string? pathVariable, string fileName)
    {
        foreach (var entry in (pathVariable ?? string.Empty).Split(';'))
        {
            var directory = entry.Trim().Trim('"');
            if (directory.Length == 0 || !fileSystem.Path.IsPathFullyQualified(directory))
            {
                continue;
            }

            var candidate = fileSystem.Path.Combine(directory, fileName);
            if (fileSystem.File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
