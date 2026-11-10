namespace GitHubBackup.Core.BackupConfiguration;

/// <summary>
/// Resolves where the archives and the clones of a repository from <c>backup-config.json</c> live on disk.
/// </summary>
/// <remarks>
/// Archives and clones live in separate trees: the <c>path</c> of an account or a repository moves only the archive
/// folder, and every clone stays under the clone root. The same rule applies to repositories of any owner. The
/// methods expect a configuration that has passed <see cref="IBackupConfigValidator"/>.
/// </remarks>
public interface IRepositoryPathResolver
{
    /// <summary>
    /// Returns the archive folder of a repository of a tracked account: the repository <c>path</c> when set,
    /// otherwise <c>&lt;account path&gt;\&lt;repo&gt;</c> when the account sets one, otherwise
    /// <c>&lt;backupRoot&gt;\&lt;login&gt;\&lt;repo&gt;</c>.
    /// </summary>
    /// <param name="config">The backup configuration.</param>
    /// <param name="account">The account the repository belongs to.</param>
    /// <param name="repository">The repository of <paramref name="account"/>.</param>
    /// <returns>The absolute archive folder.</returns>
    /// <exception cref="ArgumentException">
    /// The account URL is invalid, or a folder derived from the login or the name lies outside its root.
    /// </exception>
    string GetArchiveFolder(BackupConfig config, AccountConfig account, AccountRepositoryConfig repository);

    /// <summary>
    /// Returns the archive folder of a standalone repository: the repository <c>path</c> when set, otherwise
    /// <c>&lt;backupRoot&gt;\&lt;owner&gt;\&lt;repo&gt;</c>.
    /// </summary>
    /// <param name="config">The backup configuration.</param>
    /// <param name="repository">The standalone repository.</param>
    /// <returns>The absolute archive folder.</returns>
    /// <exception cref="ArgumentException">
    /// The repository URL is invalid, or a folder derived from the owner or the name lies outside the backup root.
    /// </exception>
    string GetArchiveFolder(BackupConfig config, RepositoryConfig repository);

    /// <summary>
    /// Returns the root folder of the clones: <see cref="BackupConfig.SourcesRoot"/> when set, otherwise
    /// <see cref="BackupConfig.DefaultSourcesFolderName"/> in the application folder.
    /// </summary>
    /// <param name="config">The backup configuration.</param>
    /// <returns>The absolute clone root.</returns>
    string GetSourcesRoot(BackupConfig config);

    /// <summary>
    /// Returns the clone folder of a repository, <c>&lt;sourcesRoot&gt;\&lt;owner&gt;\&lt;repo&gt;</c>.
    /// </summary>
    /// <param name="config">The backup configuration.</param>
    /// <param name="owner">The login of the repository owner.</param>
    /// <param name="repository">The repository name.</param>
    /// <returns>The absolute clone folder.</returns>
    /// <exception cref="ArgumentException">The login or the repository name is not valid on GitHub.</exception>
    string GetCloneFolder(BackupConfig config, string owner, string repository);

    /// <summary>
    /// Returns the clone folder of the wiki of a repository, <c>&lt;sourcesRoot&gt;\&lt;owner&gt;\&lt;repo&gt;.wiki</c>.
    /// </summary>
    /// <param name="config">The backup configuration.</param>
    /// <param name="owner">The login of the repository owner.</param>
    /// <param name="repository">The repository name.</param>
    /// <returns>The absolute clone folder of the wiki.</returns>
    /// <exception cref="ArgumentException">The login or the repository name is not valid on GitHub.</exception>
    string GetWikiCloneFolder(BackupConfig config, string owner, string repository);

    /// <summary>
    /// Returns the folder that holds archives while they are created, <c>&lt;sourcesRoot&gt;\.tmp</c>.
    /// </summary>
    /// <remarks>The name cannot collide with an owner folder: a GitHub login contains no dot.</remarks>
    /// <param name="config">The backup configuration.</param>
    /// <returns>The absolute folder of the temporary archives.</returns>
    string GetTemporaryArchiveFolder(BackupConfig config);
}
