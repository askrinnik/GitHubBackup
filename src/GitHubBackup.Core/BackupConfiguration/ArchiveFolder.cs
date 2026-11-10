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
            ? ContainedFolder.Combine(accountPath, repository)
            : ContainedFolder.Combine(backupRoot, owner, repository);
    }
}
