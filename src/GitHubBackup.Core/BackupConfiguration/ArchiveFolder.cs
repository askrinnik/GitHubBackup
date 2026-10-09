namespace GitHubBackup.Core.BackupConfiguration;

/// <summary>
/// Resolves the folder that receives the archives of one repository.
/// </summary>
internal static class ArchiveFolder
{
    /// <summary>
    /// Returns the archive folder of a repository: the repository <c>path</c> when set, otherwise
    /// <c>&lt;account path&gt;\&lt;repo&gt;</c> when the account sets one, otherwise
    /// <c>&lt;backupRoot&gt;\&lt;owner&gt;\&lt;repo&gt;</c>.
    /// </summary>
    /// <param name="backupRoot">The absolute root folder of the archives.</param>
    /// <param name="accountPath">The absolute folder of the account, or <see langword="null"/>.</param>
    /// <param name="owner">The login of the repository owner.</param>
    /// <param name="repository">The repository name.</param>
    /// <param name="repositoryPath">The absolute archive folder set on the repository, or <see langword="null"/>.</param>
    /// <returns>The archive folder.</returns>
    /// <exception cref="ArgumentException">
    /// A folder derived from <paramref name="owner"/> and <paramref name="repository"/> lies outside its root.
    /// </exception>
    public static string Resolve(
        string backupRoot, string? accountPath, string owner, string repository, string? repositoryPath)
    {
        if (repositoryPath is not null)
        {
            return repositoryPath;
        }

        return accountPath is not null
            ? EnsureInside(accountPath, Path.Combine(accountPath, repository))
            : EnsureInside(backupRoot, Path.Combine(backupRoot, owner, repository));
    }

    /// <summary>
    /// Returns <paramref name="folder"/> after checking that it lies strictly inside <paramref name="root"/>.
    /// </summary>
    /// <remarks>
    /// Owner and repository names come from the configuration and the network; a name that is rooted or contains
    /// <c>..</c> must not move the archives out of the folder the user chose.
    /// </remarks>
    /// <param name="root">The absolute root the folder must stay in.</param>
    /// <param name="folder">The folder built from <paramref name="root"/> and untrusted names.</param>
    /// <returns><paramref name="folder"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="folder"/> is not inside <paramref name="root"/>.</exception>
    private static string EnsureInside(string root, string folder)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullFolder = Path.GetFullPath(folder);
        if (!fullFolder.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) || fullFolder.Length == fullRoot.Length)
        {
            throw new ArgumentException($"The archive folder '{folder}' is not inside '{root}'.", nameof(folder));
        }

        return folder;
    }
}
