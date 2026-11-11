namespace GitHubBackup.Core.BackupConfiguration;

/// <summary>
/// Decides whether the <c>exclude</c> masks of <c>backup-config.json</c> exclude a repository.
/// </summary>
public interface IExclusionMatcher
{
    /// <summary>
    /// Finds the first mask that excludes the repository.
    /// </summary>
    /// <remarks>
    /// Masks use <c>*</c> and <c>?</c> and ignore case. The masks of the account whose login equals
    /// <paramref name="owner"/> are tested first, in file order, against <paramref name="name"/>; then the global masks
    /// are tested against <c>owner/name</c>.
    /// </remarks>
    /// <param name="config">The configuration that holds the masks.</param>
    /// <param name="owner">The login of the repository owner.</param>
    /// <param name="name">The repository name.</param>
    /// <returns>The mask that excludes the repository, or <see langword="null"/> when none does.</returns>
    ExclusionMatch? Match(BackupConfig config, string owner, string name);
}
