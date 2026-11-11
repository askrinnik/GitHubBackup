using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.GitHub;

namespace GitHubBackup.Core.Runs;

/// <summary>
/// Describes one repository of <c>backup-config.json</c> with its folders resolved, ready for a backup run.
/// </summary>
/// <param name="Owner">The login of the repository owner.</param>
/// <param name="Name">The repository name.</param>
/// <param name="Id">The GitHub id of the repository.</param>
/// <param name="Branch">The branch to check out, or <see langword="null"/> for the default branch.</param>
/// <param name="ArchiveFolder">The absolute archive folder.</param>
/// <param name="CloneFolder">The absolute clone folder.</param>
/// <param name="IsActive">Whether the repository is <see cref="RepositoryStatus.Active"/>.</param>
/// <param name="WikiCloneFolder">The absolute clone folder of the repository's wiki.</param>
public sealed record BackupWorkItem(
    string Owner,
    string Name,
    long Id,
    string? Branch,
    string ArchiveFolder,
    string CloneFolder,
    bool IsActive,
    string WikiCloneFolder)
{
    /// <summary>Gets the repository as <c>owner/name</c>.</summary>
    public string FullName => $"{Owner}/{Name}";

    /// <summary>Gets the wiki of the repository as <c>owner/name.wiki</c>.</summary>
    public string WikiFullName => $"{FullName}.wiki";

    /// <summary>
    /// Gets the login of the tracked account that lists the repository, as configured, or <see langword="null"/> for
    /// a standalone repository.
    /// </summary>
    public string? AccountLogin { get; init; }

    /// <summary>
    /// Lists the repositories of <paramref name="config"/>: the repositories of each account in file order, then the
    /// standalone repositories in file order.
    /// </summary>
    /// <param name="config">A configuration that has passed <see cref="IBackupConfigValidator"/>.</param>
    /// <param name="pathResolver">Resolves the archive, clone and wiki clone folders.</param>
    /// <returns>One item per repository, unavailable ones included.</returns>
    /// <exception cref="ArgumentException">A URL, a name or a folder in <paramref name="config"/> is not valid.</exception>
    public static IReadOnlyList<BackupWorkItem> CreateAll(BackupConfig config, IRepositoryPathResolver pathResolver)
    {
        var items = new List<BackupWorkItem>();
        foreach (var account in config.Accounts)
        {
            if (!GitHubUrl.TryParseAccount(account.Url, out var login))
            {
                throw new ArgumentException($"The account URL '{account.Url}' is not a GitHub account URL.", nameof(config));
            }

            foreach (var repository in account.Repositories)
            {
                items.Add(new(
                    login,
                    repository.Name,
                    repository.Id,
                    repository.Branch,
                    pathResolver.GetArchiveFolder(config, account, repository),
                    pathResolver.GetCloneFolder(config, login, repository.Name),
                    repository.Status == RepositoryStatus.Active,
                    pathResolver.GetWikiCloneFolder(config, login, repository.Name))
                {
                    AccountLogin = login,
                });
            }
        }

        foreach (var repository in config.Repositories)
        {
            if (!GitHubUrl.TryParseRepository(repository.Url, out var owner, out var name))
            {
                throw new ArgumentException(
                    $"The repository URL '{repository.Url}' is not a GitHub repository URL.", nameof(config));
            }

            items.Add(new(
                owner,
                name,
                repository.Id,
                repository.Branch,
                pathResolver.GetArchiveFolder(config, repository),
                pathResolver.GetCloneFolder(config, owner, name),
                repository.Status == RepositoryStatus.Active,
                pathResolver.GetWikiCloneFolder(config, owner, name)));
        }

        return items;
    }
}
