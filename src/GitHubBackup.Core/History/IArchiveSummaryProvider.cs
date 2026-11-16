using GitHubBackup.Core.BackupConfiguration;

namespace GitHubBackup.Core.History;

/// <summary>
/// Reads the archive history of every repository of <c>backup-config.json</c> for the repository table of the archive
/// history.
/// </summary>
public interface IArchiveSummaryProvider
{
    /// <summary>
    /// Reads the repositories of the configuration and the archives of each one.
    /// </summary>
    /// <remarks>
    /// A state file or an archive folder that cannot be read affects only its repository: its summary carries
    /// <see cref="RepositoryArchiveSummary.Error"/> and the other repositories are read as usual.
    /// </remarks>
    /// <param name="cancellationToken">Cancels reading the files.</param>
    /// <returns>
    /// One summary per repository in configuration order: the repositories of the accounts in file order, then the
    /// standalone ones.
    /// </returns>
    /// <exception cref="BackupConfigException">The configuration file cannot be read or is not valid.</exception>
    Task<IReadOnlyList<RepositoryArchiveSummary>> LoadAsync(CancellationToken cancellationToken);
}
