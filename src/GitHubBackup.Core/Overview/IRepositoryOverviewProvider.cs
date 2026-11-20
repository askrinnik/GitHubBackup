using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Runs;

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

    /// <summary>
    /// Reads the configuration again and the state files only of the repositories a backup run changed.
    /// </summary>
    /// <remarks>
    /// A state file is read for a repository the run processed (a wiki result counts for its repository), for an
    /// entry that <paramref name="previous"/> does not hold and for an entry whose archive folder differs. Every other
    /// entry keeps the date and the state error of <paramref name="previous"/>. The status of every entry is computed
    /// from the new configuration.
    /// </remarks>
    /// <param name="previous">The overview read before the run.</param>
    /// <param name="result">The outcome of the run.</param>
    /// <param name="cancellationToken">Cancels reading the files.</param>
    /// <returns>The accounts and the standalone repositories.</returns>
    /// <exception cref="BackupConfigException">As for <see cref="LoadAsync"/>.</exception>
    Task<RepositoryOverview> LoadAfterRunAsync(
        RepositoryOverview previous, BackupRunResult result, CancellationToken cancellationToken);
}
