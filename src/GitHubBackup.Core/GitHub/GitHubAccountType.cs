namespace GitHubBackup.Core.GitHub;

/// <summary>
/// Tells which repository list belongs to a GitHub account.
/// </summary>
public enum GitHubAccountType
{
    /// <summary>A personal account; its repositories are listed through <c>/users/{login}/repos</c>.</summary>
    User,

    /// <summary>An organization; its repositories are listed through <c>/orgs/{org}/repos</c>.</summary>
    Organization,
}
