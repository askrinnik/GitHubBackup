namespace GitHubBackup.Core.GitHub;

/// <summary>
/// A GitHub repository with the attributes discovery and backup decide on.
/// </summary>
/// <param name="Id">The numeric id, which stays the same when the repository is renamed or transferred.</param>
/// <param name="Owner">The login of the owner, in the letter case the server uses.</param>
/// <param name="Name">The repository name, in the letter case the server uses.</param>
/// <param name="IsPrivate">Whether the repository is private.</param>
/// <param name="IsFork">Whether the repository is a fork.</param>
/// <param name="IsArchived">Whether the repository is archived.</param>
/// <param name="HasWiki">Whether the wiki feature is enabled.</param>
/// <param name="DefaultBranch">The default branch.</param>
/// <param name="HtmlUrl">The web address of the repository.</param>
public sealed record GitHubRepository(
    long Id,
    string Owner,
    string Name,
    bool IsPrivate,
    bool IsFork,
    bool IsArchived,
    bool HasWiki,
    string DefaultBranch,
    string HtmlUrl)
{
    /// <summary>Gets the <c>owner/name</c> form of the repository.</summary>
    public string FullName => $"{Owner}/{Name}";
}
