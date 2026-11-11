namespace GitHubBackup.Core.GitHub;

/// <summary>
/// Returns the URL that git clones and fetches a repository from.
/// </summary>
public interface IRemoteUrlProvider
{
    /// <summary>
    /// Returns the URL of the repository <paramref name="owner"/>/<paramref name="name"/>, without credentials.
    /// </summary>
    /// <param name="owner">The login of the repository owner.</param>
    /// <param name="name">The repository name.</param>
    /// <returns>The URL passed to git.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="owner"/> is not a GitHub login or <paramref name="name"/> is not a GitHub repository name.
    /// </exception>
    string GetRepositoryUrl(string owner, string name);
}
