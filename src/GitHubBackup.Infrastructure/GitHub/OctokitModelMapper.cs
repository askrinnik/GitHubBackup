using GitHubBackup.Core.GitHub;
using Octokit;

namespace GitHubBackup.Infrastructure.GitHub;

/// <summary>
/// Converts Octokit models into the domain records of <c>GitHubBackup.Core</c>.
/// </summary>
internal static class OctokitModelMapper
{
    /// <summary>
    /// Converts a repository.
    /// </summary>
    /// <param name="repository">The repository returned by Octokit.</param>
    /// <returns>The domain repository.</returns>
    public static GitHubRepository ToRepository(Repository repository) => new(
        repository.Id,
        repository.Owner.Login,
        repository.Name,
        repository.Private,
        repository.Fork,
        repository.Archived,
        repository.HasWiki,
        repository.DefaultBranch,
        repository.HtmlUrl);

    /// <summary>
    /// Converts an account; every type other than an organization, such as a bot, is a user because its
    /// repositories are listed through <c>/users/{login}/repos</c>.
    /// </summary>
    /// <param name="account">The account returned by Octokit.</param>
    /// <returns>The domain account.</returns>
    public static GitHubAccount ToAccount(Account account) => new(
        account.Login,
        account.Type == AccountType.Organization ? GitHubAccountType.Organization : GitHubAccountType.User);
}
