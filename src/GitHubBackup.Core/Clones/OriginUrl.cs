namespace GitHubBackup.Core.Clones;

/// <summary>
/// Compares the <c>origin</c> of a clone with the URL of a repository.
/// </summary>
public static class OriginUrl
{
    /// <summary>
    /// Returns whether two repository URLs name the same repository, ignoring case, a trailing <c>/</c> and a trailing
    /// <c>.git</c>.
    /// </summary>
    /// <param name="first">A repository URL.</param>
    /// <param name="second">Another repository URL.</param>
    /// <returns><see langword="true"/> when the URLs match.</returns>
    public static bool Matches(string first, string second) =>
        string.Equals(Normalize(first), Normalize(second), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Removes the surrounding white space, the trailing <c>/</c> and the trailing <c>.git</c> of a repository URL.
    /// </summary>
    /// <param name="url">The URL.</param>
    /// <returns>The URL in the form used for comparison.</returns>
    private static string Normalize(string url)
    {
        var trimmed = url.Trim().TrimEnd('/');
        if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        return trimmed.TrimEnd('/');
    }
}
