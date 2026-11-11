namespace GitHubBackup.Core.GitHub;

/// <summary>
/// Implements <see cref="IRemoteUrlProvider"/> with the HTTPS URLs of github.com,
/// <c>https://github.com/&lt;owner&gt;/&lt;name&gt;.git</c> and <c>https://github.com/&lt;owner&gt;/&lt;name&gt;.wiki.git</c>.
/// </summary>
/// <remarks>
/// The names are checked before they enter the URL, so a name from the GitHub API or <c>backup-config.json</c> cannot
/// point git at another host or path.
/// </remarks>
public sealed class GitHubRemoteUrlProvider : IRemoteUrlProvider
{
    /// <inheritdoc />
    public string GetRepositoryUrl(string owner, string name)
    {
        EnsureValidNames(owner, name);
        return $"https://github.com/{owner}/{name}.git";
    }

    /// <inheritdoc />
    public string GetWikiUrl(string owner, string name)
    {
        EnsureValidNames(owner, name);
        return $"https://github.com/{owner}/{name}.wiki.git";
    }

    /// <summary>
    /// Checks that <paramref name="owner"/> is a GitHub login and <paramref name="name"/> a GitHub repository name.
    /// </summary>
    /// <param name="owner">The login of the repository owner.</param>
    /// <param name="name">The repository name.</param>
    /// <exception cref="ArgumentException">A name is not valid.</exception>
    private static void EnsureValidNames(string owner, string name)
    {
        if (!GitHubUrl.IsValidLogin(owner))
        {
            throw new ArgumentException($"'{owner}' is not a GitHub login.", nameof(owner));
        }

        if (!GitHubUrl.IsValidRepositoryName(name))
        {
            throw new ArgumentException($"'{name}' is not a GitHub repository name.", nameof(name));
        }
    }
}
