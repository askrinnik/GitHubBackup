using GitHubBackup.Core.BackupConfiguration;

namespace GitHubBackup.Core.Overview;

/// <summary>
/// Reads <c>backup-config.json</c> and the state files of its repositories into a <see cref="RepositoryOverview"/>.
/// </summary>
public interface IRepositoryOverviewProvider
{
    /// <summary>
    /// Reads the configuration and the date of the last archive of each repository.
    /// </summary>
    /// <remarks>
    /// A state file that cannot be read affects only its repository: the entry carries
    /// <see cref="RepositoryOverviewEntry.StateError"/> and the other entries are read as usual.
    /// </remarks>
    /// <param name="cancellationToken">Cancels reading the files.</param>
    /// <returns>The accounts and the standalone repositories.</returns>
    /// <exception cref="BackupConfigException">
    /// The configuration file does not exist, cannot be read, fails validation, or holds a URL, a name or a folder
    /// that is not valid.
    /// </exception>
    Task<RepositoryOverview> LoadAsync(CancellationToken cancellationToken);
}
