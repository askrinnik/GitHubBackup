using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.GitHub;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Discovery;

/// <summary>
/// Classifies the repositories GitHub reports against <c>backup-config.json</c>.
/// </summary>
/// <param name="gitHubApi">Reads the repository lists and looks up single repositories.</param>
/// <param name="exclusionMatcher">Decides which repositories the <c>exclude</c> masks remove from discovery.</param>
/// <param name="logger">The logger.</param>
public sealed class RepositoryDiscoveryService(
    IGitHubApi gitHubApi,
    IExclusionMatcher exclusionMatcher,
    ILogger<RepositoryDiscoveryService> logger) : IRepositoryDiscoveryService
{
    /// <inheritdoc />
    public async Task<DiscoveryResult> DiscoverAsync(
        BackupConfig config, string? accountLogin, CancellationToken cancellationToken)
    {
        var accounts = ParseAccounts(config, out var tracked);
        var scopeAccounts = SelectScopeAccounts(accounts, accountLogin);
        var scopeTracked = accountLogin is null
            ? tracked
            : [.. tracked.Where(item => item.AccountLogin is not null && IsSameName(item.AccountLogin, scopeAccounts[0]))];

        var collected = await CollectAsync(scopeAccounts, cancellationToken);
        var knownIds = tracked.Select(item => item.Id).ToHashSet();

        var newInAccounts = new List<NewAccountRepository>();
        var newStandalone = new List<GitHubRepository>();
        foreach (var repository in collected.Values.OrderBy(item => item.FullName, StringComparer.OrdinalIgnoreCase))
        {
            if (knownIds.Contains(repository.Id) || exclusionMatcher.Match(config, repository.Owner, repository.Name) is not null)
            {
                continue;
            }

            var owningAccount = accounts.Find(login => IsSameName(login, repository.Owner));
            if (owningAccount is not null)
            {
                if (scopeAccounts.Contains(owningAccount))
                {
                    newInAccounts.Add(new(owningAccount, repository));
                }
            }
            else if (accountLogin is null)
            {
                newStandalone.Add(repository);
            }
        }

        var missing = new List<TrackedRepository>();
        var returned = new List<ReturnedRepository>();
        var renamed = new List<RenamedRepository>();
        foreach (var item in scopeTracked)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (exclusionMatcher.Match(config, item.Owner, item.Name) is not null)
            {
                continue;
            }

            var current = collected.GetValueOrDefault(item.Id) ?? await FindAsync(item, cancellationToken);
            if (current is null)
            {
                if (item.Status == RepositoryStatus.Active)
                {
                    missing.Add(item);
                }

                continue;
            }

            if (item.Status == RepositoryStatus.Unavailable)
            {
                returned.Add(new(item, current));
            }

            if (!IsSameName(item.FullName, current.FullName))
            {
                renamed.Add(new(item, current));
            }
        }

        var result = new DiscoveryResult(newInAccounts, newStandalone, missing, returned, renamed);
        RepositoryDiscoveryServiceLog.Classified(
            logger, newInAccounts.Count, newStandalone.Count, missing.Count, returned.Count, renamed.Count);
        return result;
    }

    /// <summary>
    /// Compares two logins or <c>owner/name</c> strings without regard to case.
    /// </summary>
    /// <param name="left">The first name.</param>
    /// <param name="right">The second name.</param>
    /// <returns><see langword="true"/> when the names are equal ignoring case.</returns>
    private static bool IsSameName(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Lists the tracked account logins in file order and the configured repositories of the accounts and of the
    /// standalone list in file order.
    /// </summary>
    /// <param name="config">The configuration to read.</param>
    /// <param name="tracked">The configured repositories.</param>
    /// <returns>The account logins.</returns>
    /// <exception cref="ArgumentException">A URL in <paramref name="config"/> is not valid.</exception>
    private static List<string> ParseAccounts(BackupConfig config, out List<TrackedRepository> tracked)
    {
        var logins = new List<string>();
        tracked = [];
        foreach (var account in config.Accounts)
        {
            if (!GitHubUrl.TryParseAccount(account.Url, out var login))
            {
                throw new ArgumentException($"The account URL '{account.Url}' is not a GitHub account URL.", nameof(config));
            }

            logins.Add(login);
            tracked.AddRange(account.Repositories.Select(
                repository => new TrackedRepository(login, repository.Name, repository.Id, repository.Status, login)));
        }

        foreach (var repository in config.Repositories)
        {
            if (!GitHubUrl.TryParseRepository(repository.Url, out var owner, out var name))
            {
                throw new ArgumentException(
                    $"The repository URL '{repository.Url}' is not a GitHub repository URL.", nameof(config));
            }

            tracked.Add(new(owner, name, repository.Id, repository.Status, null));
        }

        return logins;
    }

    /// <summary>
    /// Returns the accounts discovery covers: all of them, or the one named by <paramref name="accountLogin"/>.
    /// </summary>
    /// <param name="accounts">The tracked account logins.</param>
    /// <param name="accountLogin">The requested login, or <see langword="null"/> for every account.</param>
    /// <returns>The logins in scope, as configured.</returns>
    /// <exception cref="ArgumentException"><paramref name="accountLogin"/> is not a tracked account.</exception>
    private static List<string> SelectScopeAccounts(List<string> accounts, string? accountLogin)
    {
        if (accountLogin is null)
        {
            return accounts;
        }

        var match = accounts.Find(login => IsSameName(login, accountLogin))
            ?? throw new ArgumentException($"The account '{accountLogin}' is not tracked.", nameof(accountLogin));
        return [match];
    }

    /// <summary>
    /// Lists the repositories of the token's user and of each account in scope, one source after another, keeping the
    /// first occurrence of every id.
    /// </summary>
    /// <param name="scopeAccounts">The account logins to list.</param>
    /// <param name="cancellationToken">The token that cancels the requests.</param>
    /// <returns>The repositories by id.</returns>
    private async Task<Dictionary<long, GitHubRepository>> CollectAsync(
        List<string> scopeAccounts, CancellationToken cancellationToken)
    {
        var collected = new Dictionary<long, GitHubRepository>();

        cancellationToken.ThrowIfCancellationRequested();
        Add(collected, "user", await gitHubApi.GetCurrentUserRepositoriesAsync(cancellationToken));

        foreach (var login in scopeAccounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var account = await gitHubApi.GetAccountAsync(login, cancellationToken);
            var repositories = account.Type == GitHubAccountType.Organization
                ? await gitHubApi.GetOrganizationRepositoriesAsync(login, cancellationToken)
                : await gitHubApi.GetUserRepositoriesAsync(login, cancellationToken);
            Add(collected, login, repositories);
        }

        return collected;
    }

    /// <summary>
    /// Adds the repositories of one source whose id is not collected yet and logs the size of the source.
    /// </summary>
    /// <param name="collected">The repositories collected so far.</param>
    /// <param name="source">The name of the source, for the log.</param>
    /// <param name="repositories">The repositories the source returned.</param>
    private void Add(Dictionary<long, GitHubRepository> collected, string source, IReadOnlyList<GitHubRepository> repositories)
    {
        RepositoryDiscoveryServiceLog.SourceListed(logger, source, repositories.Count);
        foreach (var repository in repositories)
        {
            collected.TryAdd(repository.Id, repository);
        }
    }

    /// <summary>
    /// Looks up a configured repository that no list contained: by its configured name, then by id when the name now
    /// belongs to another repository.
    /// </summary>
    /// <param name="item">The configured repository.</param>
    /// <param name="cancellationToken">The token that cancels the requests.</param>
    /// <returns>The repository, or <see langword="null"/> when GitHub answers 404 or 403.</returns>
    /// <exception cref="GitHubApiException">The request failed for another reason, such as an exhausted rate limit.</exception>
    private async Task<GitHubRepository?> FindAsync(TrackedRepository item, CancellationToken cancellationToken)
    {
        var byName = await TryGetAsync(
            () => gitHubApi.GetRepositoryAsync(item.Owner, item.Name, cancellationToken));
        if (byName is not null && byName.Id == item.Id)
        {
            return byName;
        }

        return await TryGetAsync(() => gitHubApi.GetRepositoryByIdAsync(item.Id, cancellationToken));
    }

    /// <summary>
    /// Runs a repository lookup and turns "not found" and "access denied" into <see langword="null"/>.
    /// </summary>
    /// <param name="lookup">The lookup.</param>
    /// <returns>The repository, or <see langword="null"/> when it does not exist or is not visible.</returns>
    private static async Task<GitHubRepository?> TryGetAsync(Func<Task<GitHubRepository>> lookup)
    {
        try
        {
            return await lookup();
        }
        catch (Exception exception) when (exception is GitHubNotFoundException or GitHubAccessDeniedException)
        {
            return null;
        }
    }
}
