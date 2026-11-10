namespace GitHubBackup.Core.BackupConfiguration;

/// <summary>
/// Builds folders from a trusted root and untrusted names without letting the names leave the root.
/// </summary>
/// <remarks>
/// Every path is a Windows path: it starts with a drive letter or a UNC prefix, uses <c>\</c> as the separator, and
/// is compared without regard to case. The containment check relies on <see cref="Path"/> behaving as it does on
/// Windows, the only operating system the application runs on.
/// </remarks>
internal static class ContainedFolder
{
    /// <summary>
    /// Combines <paramref name="root"/> with <paramref name="names"/> and checks that the result lies strictly inside
    /// <paramref name="root"/>.
    /// </summary>
    /// <remarks>
    /// Owner and repository names come from the configuration and the network; a name that is rooted or contains
    /// <c>..</c> must not move a clone or an archive out of the folder the user chose.
    /// </remarks>
    /// <param name="root">The absolute root the folder must stay in.</param>
    /// <param name="names">The untrusted folder names appended to <paramref name="root"/>.</param>
    /// <returns>The combined folder.</returns>
    /// <exception cref="ArgumentException">The combined folder is not inside <paramref name="root"/>.</exception>
    public static string Combine(string root, params ReadOnlySpan<string> names)
    {
        var folder = root;
        foreach (var name in names)
        {
            folder = Path.Combine(folder, name);
        }

        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullFolder = Path.GetFullPath(folder);
        if (!fullFolder.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) || fullFolder.Length == fullRoot.Length)
        {
            throw new ArgumentException($"The folder '{folder}' is not inside '{root}'.", nameof(names));
        }

        return folder;
    }
}
