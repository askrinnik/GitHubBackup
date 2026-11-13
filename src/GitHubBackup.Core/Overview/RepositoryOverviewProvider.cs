using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.State;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Core.Overview;

/// <summary>
/// Builds the overview from the repositories a backup run would process, so the overview and the run resolve the
/// same archive folders.
/// </summary>
/// <param name="configStore">Reads <c>backup-config.json</c>.</param>
/// <param name="stateStore">Reads the state file of a repository.</param>
/// <param name="pathResolver">Resolves the archive folder of a repository.</param>
/// <param name="exclusionMatcher">Decides whether a mask excludes a repository.</param>
/// <param name="options">Supplies the path of <c>backup-config.json</c> for the errors.</param>
public sealed class RepositoryOverviewProvider(
    IBackupConfigStore configStore,
    IBackupStateStore stateStore,
    IRepositoryPathResolver pathResolver,
    IExclusionMatcher exclusionMatcher,
    IOptions<BackupOptions> options) : IRepositoryOverviewProvider
{
    /// <inheritdoc />
    public async Task<RepositoryOverview> LoadAsync(CancellationToken cancellationToken)
    {
        var config = (await configStore.LoadAsync(cancellationToken)).Config;
        IReadOnlyList<BackupWorkItem> items;
        try
        {
            items = BackupWorkItem.CreateAll(config, pathResolver);
        }
        catch (ArgumentException exception)
        {
            throw new BackupConfigException(options.Value.ConfigPath, [exception.Message], exception);
        }

        // BackupWorkItem.CreateAll lists the repositories of each account in file order before the standalone ones,
        // so each account takes as many items as it lists repositories.
        var next = 0;
        var accounts = new List<AccountOverview>(config.Accounts.Count);
        foreach (var account in config.Accounts)
        {
            // BackupWorkItem.CreateAll has already rejected an account URL that does not parse, so the URL fallback
            // only satisfies the compiler.
            var login = GitHubUrl.TryParseAccount(account.Url, out var parsed) ? parsed : account.Url;
            var repositories = new List<RepositoryOverviewEntry>(account.Repositories.Count);
            for (var index = 0; index < account.Repositories.Count; index++)
            {
                repositories.Add(await CreateEntryAsync(config, items[next++], cancellationToken));
            }

            accounts.Add(new(login, account.Url, repositories));
        }

        var standalone = new List<RepositoryOverviewEntry>(items.Count - next);
        while (next < items.Count)
        {
            standalone.Add(await CreateEntryAsync(config, items[next++], cancellationToken));
        }

        return new(accounts, standalone);
    }

    /// <summary>
    /// Creates the entry of one repository with its status and the date of its last archive.
    /// </summary>
    /// <param name="config">The configuration that holds the exclusion masks.</param>
    /// <param name="item">The repository with its archive folder resolved.</param>
    /// <param name="cancellationToken">Cancels reading the state file.</param>
    /// <returns>The entry.</returns>
    private async Task<RepositoryOverviewEntry> CreateEntryAsync(
        BackupConfig config, BackupWorkItem item, CancellationToken cancellationToken)
    {
        var status = exclusionMatcher.Match(config, item.Owner, item.Name) is not null ? RepositoryOverviewStatus.Excluded
            : item.IsActive ? RepositoryOverviewStatus.Active
            : RepositoryOverviewStatus.Unavailable;
        DateTimeOffset? lastArchiveAt = null;
        string? stateError = null;
        try
        {
            var state = await stateStore.LoadAsync(item.ArchiveFolder, item.Id, item.FullName, cancellationToken);
            lastArchiveAt = state.GetLastArchive(BackupTarget.Repository)?.CreatedAt;
        }
        catch (BackupStateException exception)
        {
            stateError = exception.Message;
        }

        return new(item.Owner, item.Name, item.AccountLogin, status, lastArchiveAt, stateError);
    }
}
