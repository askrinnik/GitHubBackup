namespace GitHubBackup.Core.GitHub;

/// <summary>
/// Reads accounts and repositories from the GitHub REST API.
/// </summary>
/// <remarks>
/// List methods return every page. A failed request throws a <see cref="GitHubApiException"/> or one of its derived
/// types; an invalid login, owner or name throws <see cref="ArgumentException"/> before any request is sent.
/// </remarks>
public interface IGitHubApi
{
    /// <summary>
    /// Returns the account with the given login (<c>GET /users/{login}</c>).
    /// </summary>
    /// <param name="login">The login of a user or an organization.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The account; a bot account is reported as <see cref="GitHubAccountType.User"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="login"/> is not a valid GitHub login.</exception>
    /// <exception cref="GitHubNotFoundException">The account does not exist.</exception>
    Task<GitHubAccount> GetAccountAsync(string login, CancellationToken cancellationToken);

    /// <summary>
    /// Returns every repository the token's user owns, collaborates on or reaches through an organization membership
    /// (<c>GET /user/repos</c>).
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The repositories; an empty list without a request when no token is configured.</returns>
    Task<IReadOnlyList<GitHubRepository>> GetCurrentUserRepositoriesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns every repository of an organization visible with the current credentials
    /// (<c>GET /orgs/{org}/repos?type=all</c>).
    /// </summary>
    /// <param name="organization">The login of the organization.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The repositories.</returns>
    /// <exception cref="ArgumentException"><paramref name="organization"/> is not a valid GitHub login.</exception>
    Task<IReadOnlyList<GitHubRepository>> GetOrganizationRepositoriesAsync(
        string organization, CancellationToken cancellationToken);

    /// <summary>
    /// Returns every repository of a user visible with the current credentials
    /// (<c>GET /users/{login}/repos?type=all</c>).
    /// </summary>
    /// <param name="login">The login of the user.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The repositories.</returns>
    /// <exception cref="ArgumentException"><paramref name="login"/> is not a valid GitHub login.</exception>
    Task<IReadOnlyList<GitHubRepository>> GetUserRepositoriesAsync(string login, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a repository by owner and name (<c>GET /repos/{owner}/{name}</c>).
    /// </summary>
    /// <param name="owner">The login of the owner.</param>
    /// <param name="name">The repository name.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The repository.</returns>
    /// <exception cref="ArgumentException"><paramref name="owner"/> or <paramref name="name"/> is not valid.</exception>
    /// <exception cref="GitHubNotFoundException">The repository does not exist or is not visible.</exception>
    Task<GitHubRepository> GetRepositoryAsync(string owner, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a repository by its numeric id (<c>GET /repositories/{id}</c>), which finds it after a rename or a
    /// transfer.
    /// </summary>
    /// <param name="id">The repository id.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The repository under its current owner and name.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is not positive.</exception>
    /// <exception cref="GitHubNotFoundException">The repository does not exist or is not visible.</exception>
    Task<GitHubRepository> GetRepositoryByIdAsync(long id, CancellationToken cancellationToken);
}
