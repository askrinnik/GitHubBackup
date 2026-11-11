using GitHubBackup.Core.GitHub;

namespace GitHubBackup.Infrastructure.IntegrationTests.Runs;

/// <summary>
/// Answers the GitHub API from repositories and accounts registered in memory, so a backup run discovers without the
/// network.
/// </summary>
/// <remarks>
/// The token's user owns no repository; an account lists the registered repositories whose owner is its login. A
/// repository or account that is not registered answers <see cref="GitHubNotFoundException"/>, as GitHub answers 404.
/// </remarks>
internal sealed class FakeGitHubApi : IGitHubApi
{
    private readonly Dictionary<string, GitHubAccountType> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, GitHubRepository> _repositories = [];

    /// <summary>
    /// Registers an account.
    /// </summary>
    /// <param name="login">The login.</param>
    /// <param name="type">Whether the account is a user or an organization.</param>
    public void AddAccount(string login, GitHubAccountType type) => _accounts[login] = type;

    /// <summary>
    /// Registers a repository, replacing a registered one with the same id.
    /// </summary>
    /// <param name="id">The GitHub id.</param>
    /// <param name="owner">The login of the owner.</param>
    /// <param name="name">The repository name.</param>
    /// <param name="hasWiki">Whether the wiki feature is enabled.</param>
    public void AddRepository(long id, string owner, string name, bool hasWiki) =>
        _repositories[id] = new(
            id, owner, name, IsPrivate: false, IsFork: false, IsArchived: false, hasWiki, "main", $"https://github.com/{owner}/{name}");

    /// <inheritdoc />
    public Task<GitHubAccount> GetAccountAsync(string login, CancellationToken cancellationToken) =>
        _accounts.TryGetValue(login, out var type)
            ? Task.FromResult(new GitHubAccount(login, type))
            : Task.FromException<GitHubAccount>(NotFound($"users/{login}"));

    /// <inheritdoc />
    public Task<IReadOnlyList<GitHubRepository>> GetCurrentUserRepositoriesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GitHubRepository>>([]);

    /// <inheritdoc />
    public Task<IReadOnlyList<GitHubRepository>> GetOrganizationRepositoriesAsync(
        string organization, CancellationToken cancellationToken) => ListOwnedBy(organization);

    /// <inheritdoc />
    public Task<IReadOnlyList<GitHubRepository>> GetUserRepositoriesAsync(string login, CancellationToken cancellationToken) =>
        ListOwnedBy(login);

    /// <inheritdoc />
    public Task<GitHubRepository> GetRepositoryAsync(string owner, string name, CancellationToken cancellationToken)
    {
        var repository = _repositories.Values.FirstOrDefault(item =>
            string.Equals(item.Owner, owner, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        return repository is not null
            ? Task.FromResult(repository)
            : Task.FromException<GitHubRepository>(NotFound($"repos/{owner}/{name}"));
    }

    /// <inheritdoc />
    public Task<GitHubRepository> GetRepositoryByIdAsync(long id, CancellationToken cancellationToken) =>
        _repositories.TryGetValue(id, out var repository)
            ? Task.FromResult(repository)
            : Task.FromException<GitHubRepository>(NotFound($"repositories/{id}"));

    /// <summary>
    /// Creates the answer to a request for something that is not registered.
    /// </summary>
    /// <param name="resource">The API resource.</param>
    /// <returns>The exception.</returns>
    private static GitHubNotFoundException NotFound(string resource) => new($"The GitHub API answered 404 for {resource}.", resource);

    /// <summary>
    /// Lists the registered repositories of one owner.
    /// </summary>
    /// <param name="owner">The login of the owner.</param>
    /// <returns>The repositories in id order.</returns>
    private Task<IReadOnlyList<GitHubRepository>> ListOwnedBy(string owner) =>
        Task.FromResult<IReadOnlyList<GitHubRepository>>(
        [
            .. _repositories.Values
                .Where(item => string.Equals(item.Owner, owner, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Id),
        ]);
}
