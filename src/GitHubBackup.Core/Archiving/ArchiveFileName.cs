using System.Buffers;
using GitHubBackup.Core.GitHub;

namespace GitHubBackup.Core.Archiving;

/// <summary>
/// Checks that the file name of an archive is safe to combine with the archive folder.
/// </summary>
public static class ArchiveFileName
{
    /// <summary>The printable characters Windows forbids in a file name, on every operating system.</summary>
    private static readonly SearchValues<char> _invalidCharacters = SearchValues.Create(['\\', '/', ':', '<', '>', '"', '|', '?', '*']);

    /// <summary>
    /// Returns whether <paramref name="name"/> is a plain file name: not empty, without a path separator, a wildcard,
    /// another character Windows forbids or a control character, not a relative folder name or a reserved device
    /// name, and not ending with a dot or a space.
    /// </summary>
    /// <remarks>
    /// The name is combined with a folder, so a separator, a relative folder name, a device name or a name Windows
    /// would shorten (a trailing dot or space) must not pass.
    /// </remarks>
    /// <param name="name">The name to check.</param>
    /// <returns><see langword="true"/> when the name is a safe file name.</returns>
    public static bool IsValid(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && !name.AsSpan().ContainsAny(_invalidCharacters)
        && !name.Any(char.IsControl)
        && name is not ("." or "..")
        && name[^1] is not ('.' or ' ')
        && !GitHubUrl.IsWindowsDeviceName(name);
}
