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
    public Task<RepositoryOverview> LoadAsync(CancellationToken cancellationToken) =>
        BuildAsync(ReadStateAsync, cancellationToken);

    /// <inheritdoc />
    public Task<RepositoryOverview> LoadAfterRunAsync(
        RepositoryOverview previous, BackupRunResult result, CancellationToken cancellationToken)
    {
        var processed = result.Items
            .Select(item => item.Target == BackupTarget.Wiki ? TrimWikiSuffix(item.Repository) : item.Repository)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byId = new Dictionary<long, RepositoryOverviewEntry>();
        var byName = new Dictionary<string, RepositoryOverviewEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in previous.Accounts.SelectMany(account => account.Repositories)
                     .Concat(previous.StandaloneRepositories))
        {
            byName.TryAdd(entry.FullName, entry);
            if (entry.RepositoryId != 0)
            {
                byId.TryAdd(entry.RepositoryId, entry);
            }
        }

        return BuildAsync(
            (item, token) =>
            {
                var old = item.Id != 0 && byId.TryGetValue(item.Id, out var found) ? found
                    : byName.GetValueOrDefault(item.FullName);
                return old is not null
                       && string.Equals(old.ArchiveFolder, item.ArchiveFolder, StringComparison.OrdinalIgnoreCase)
                       && !processed.Contains(item.FullName)
                    ? Task.FromResult(new StateReading(old.LastArchiveAt, old.StateError))
                    : ReadStateAsync(item, token);
            },
            cancellationToken);
    }

    /// <summary>
    /// Removes the <c>.wiki</c> suffix of the name a wiki result carries.
    /// </summary>
    /// <param name="name">The <c>owner/name.wiki</c> of a wiki result.</param>
    /// <returns>The <c>owner/name</c> of its repository.</returns>
    private static string TrimWikiSuffix(string name) =>
        name.EndsWith(".wiki", StringComparison.OrdinalIgnoreCase) ? name[..^".wiki".Length] : name;

    /// <summary>
    /// Reads the configuration and builds the overview, taking the state of each repository from
    /// <paramref name="getState"/>.
    /// </summary>
    /// <param name="getState">Returns the date of the last archive and the state error of a repository.</param>
    /// <param name="cancellationToken">Cancels reading the files.</param>
    /// <returns>The accounts and the standalone repositories.</returns>
    private async Task<RepositoryOverview> BuildAsync(
        Func<BackupWorkItem, CancellationToken, Task<StateReading>> getState, CancellationToken cancellationToken)
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
                repositories.Add(await CreateEntryAsync(config, items[next++], getState, cancellationToken));
            }

            accounts.Add(new(login, account.Url, repositories) { IsPaused = account.Paused });
        }

        var standalone = new List<RepositoryOverviewEntry>(items.Count - next);
        while (next < items.Count)
        {
            standalone.Add(await CreateEntryAsync(config, items[next++], getState, cancellationToken));
        }

        return new(accounts, standalone);
    }

    /// <summary>
    /// Creates the entry of one repository with its status and the date of its last archive.
    /// </summary>
    /// <param name="config">The configuration that holds the exclusion masks.</param>
    /// <param name="item">The repository with its archive folder resolved.</param>
    /// <param name="getState">Returns the date of the last archive and the state error of the repository.</param>
    /// <param name="cancellationToken">Cancels reading the state file.</param>
    /// <returns>The entry.</returns>
    private async Task<RepositoryOverviewEntry> CreateEntryAsync(
        BackupConfig config,
        BackupWorkItem item,
        Func<BackupWorkItem, CancellationToken, Task<StateReading>> getState,
        CancellationToken cancellationToken)
    {
        var status = exclusionMatcher.Match(config, item.Owner, item.Name) is not null ? RepositoryOverviewStatus.Excluded
            : item.IsActive ? RepositoryOverviewStatus.Active
            : RepositoryOverviewStatus.Unavailable;
        var state = await getState(item, cancellationToken);
        return new(item.Owner, item.Name, item.AccountLogin, status, state.LastArchiveAt, state.StateError)
        {
            RepositoryId = item.Id,
            ArchiveFolder = item.ArchiveFolder,
            IsPaused = item.IsPaused,
            IsAccountPaused = item.IsAccountPaused,
        };
    }

    /// <summary>
    /// Reads the state file of one repository.
    /// </summary>
    /// <param name="item">The repository with its archive folder resolved.</param>
    /// <param name="cancellationToken">Cancels reading the state file.</param>
    /// <returns>The date of the last archive, or the reason the state file cannot be read.</returns>
    private async Task<StateReading> ReadStateAsync(BackupWorkItem item, CancellationToken cancellationToken)
    {
        try
        {
            var state = await stateStore.LoadAsync(item.ArchiveFolder, item.Id, item.FullName, cancellationToken);
            return new(state.GetLastArchive(BackupTarget.Repository)?.CreatedAt, null);
        }
        catch (BackupStateException exception)
        {
            return new(null, exception.Message);
        }
    }

    /// <summary>
    /// The state of a repository as the overview shows it.
    /// </summary>
    /// <param name="LastArchiveAt">The local time of the last archive, or <see langword="null"/>.</param>
    /// <param name="StateError">Why the state file cannot be read, or <see langword="null"/>.</param>
    private readonly record struct StateReading(DateTimeOffset? LastArchiveAt, string? StateError);
}
