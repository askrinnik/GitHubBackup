namespace GitHubBackup.Core.GitHub;

/// <summary>
/// Implements <see cref="IRemoteUrlProvider"/> with the HTTPS URL of github.com,
/// <c>https://github.com/&lt;owner&gt;/&lt;name&gt;.git</c>.
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
        if (!GitHubUrl.IsValidLogin(owner))
        {
            throw new ArgumentException($"'{owner}' is not a GitHub login.", nameof(owner));
        }

        if (!GitHubUrl.IsValidRepositoryName(name))
        {
            throw new ArgumentException($"'{name}' is not a GitHub repository name.", nameof(name));
        }

        return $"https://github.com/{owner}/{name}.git";
    }
}
