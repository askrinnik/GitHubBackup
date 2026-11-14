using GitHubBackup.Core.GitHub;

namespace GitHubBackup.Core.BackupConfiguration;

/// <inheritdoc />
/// <param name="defaultSourcesRoot">
/// The absolute clone root used when <see cref="BackupConfig.SourcesRoot"/> is <see langword="null"/>.
/// </param>
public sealed class RepositoryPathResolver(string defaultSourcesRoot) : IRepositoryPathResolver
{
    /// <summary>The name of the folder under the clone root that holds archives while they are created.</summary>
    public const string TemporaryArchiveFolderName = ".tmp";

    /// <summary>The suffix that turns a repository name into the folder name of its wiki clone.</summary>
    public const string WikiSuffix = ".wiki";

    /// <inheritdoc />
    public string GetArchiveFolder(BackupConfig config, AccountConfig account, AccountRepositoryConfig repository)
    {
        if (!GitHubUrl.TryParseAccount(account.Url, out var login))
        {
            throw new ArgumentException($"The account URL '{account.Url}' is not a GitHub account URL.", nameof(account));
        }

        return ArchiveFolder.Resolve(config.BackupRoot, account.Path, login, repository.Name, repository.Path);
    }

    /// <inheritdoc />
    public string GetArchiveFolder(BackupConfig config, RepositoryConfig repository)
    {
        if (!GitHubUrl.TryParseRepository(repository.Url, out var owner, out var name))
        {
            throw new ArgumentException(
                $"The repository URL '{repository.Url}' is not a GitHub repository URL.", nameof(repository));
        }

        return ArchiveFolder.Resolve(config.BackupRoot, null, owner, name, repository.Path);
    }

    /// <inheritdoc />
    public string GetSourcesRoot(BackupConfig config) => config.SourcesRoot ?? defaultSourcesRoot;

    /// <inheritdoc />
    public string GetCloneFolder(BackupConfig config, string owner, string repository, string? clonePath)
    {
        EnsureValidNames(owner, repository);
        return clonePath is not null
            ? Path.TrimEndingDirectorySeparator(clonePath)
            : ContainedFolder.Combine(GetSourcesRoot(config), owner, repository);
    }

    /// <inheritdoc />
    public string GetWikiCloneFolder(BackupConfig config, string owner, string repository, string? clonePath)
    {
        EnsureValidNames(owner, repository);
        return clonePath is not null
            ? Path.TrimEndingDirectorySeparator(clonePath) + WikiSuffix
            : ContainedFolder.Combine(GetSourcesRoot(config), owner, repository + WikiSuffix);
    }

    /// <summary>
    /// Checks that <paramref name="owner"/> is a GitHub login and <paramref name="repository"/> a GitHub repository
    /// name.
    /// </summary>
    /// <remarks>
    /// The names come from the GitHub API and become folder names; staying inside the clone root is not enough, because
    /// a separator, a device name or a trailing dot or space would map different names to one folder or open a device.
    /// </remarks>
    /// <param name="owner">The login of the repository owner.</param>
    /// <param name="repository">The repository name.</param>
    /// <exception cref="ArgumentException">A name is not valid.</exception>
    private static void EnsureValidNames(string owner, string repository)
    {
        if (!GitHubUrl.IsValidLogin(owner))
        {
            throw new ArgumentException($"'{owner}' is not a GitHub login.", nameof(owner));
        }

        if (!GitHubUrl.IsValidRepositoryName(repository))
        {
            throw new ArgumentException($"'{repository}' is not a GitHub repository name.", nameof(repository));
        }
    }

    /// <inheritdoc />
    public string GetTemporaryArchiveFolder(BackupConfig config) =>
        Path.Combine(GetSourcesRoot(config), TemporaryArchiveFolderName);
}
