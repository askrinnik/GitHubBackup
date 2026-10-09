using System.Diagnostics.CodeAnalysis;

namespace GitHubBackup.Core.GitHub;

/// <summary>
/// Parses the account and repository URLs accepted in <c>backup-config.json</c>.
/// </summary>
/// <remarks>
/// Only <c>https://github.com/&lt;login&gt;</c> and <c>https://github.com/&lt;owner&gt;/&lt;repo&gt;</c> are
/// accepted, with an optional single trailing <c>/</c>. User info, a port, a query, a fragment, escaped characters
/// and a <c>.git</c> suffix are rejected, so a URL that carries credentials or points elsewhere never reaches git.
/// The scheme and the host are compared without regard to case; the login and the name keep their case.
/// </remarks>
public static class GitHubUrl
{
    /// <summary>The scheme and host every accepted URL starts with.</summary>
    private const string _prefix = "https://github.com/";

    /// <summary>The longest login GitHub allows.</summary>
    private const int _maxLoginLength = 39;

    /// <summary>The longest repository name GitHub allows.</summary>
    private const int _maxRepositoryNameLength = 100;

    /// <summary>
    /// Parses an account URL.
    /// </summary>
    /// <param name="url">The URL to parse.</param>
    /// <param name="login">The login of the account when the URL is valid.</param>
    /// <returns><see langword="true"/> when <paramref name="url"/> is a valid account URL.</returns>
    public static bool TryParseAccount(string? url, [NotNullWhen(true)] out string? login)
    {
        login = null;
        if (!TrySplitPath(url, out var segments) || segments.Length != 1 || !IsValidLogin(segments[0]))
        {
            return false;
        }

        login = segments[0];
        return true;
    }

    /// <summary>
    /// Parses a repository URL.
    /// </summary>
    /// <param name="url">The URL to parse.</param>
    /// <param name="owner">The login of the owner when the URL is valid.</param>
    /// <param name="name">The repository name when the URL is valid.</param>
    /// <returns><see langword="true"/> when <paramref name="url"/> is a valid repository URL.</returns>
    public static bool TryParseRepository(
        string? url, [NotNullWhen(true)] out string? owner, [NotNullWhen(true)] out string? name)
    {
        owner = null;
        name = null;
        if (!TrySplitPath(url, out var segments)
            || segments.Length != 2
            || !IsValidLogin(segments[0])
            || !IsValidRepositoryName(segments[1]))
        {
            return false;
        }

        owner = segments[0];
        name = segments[1];
        return true;
    }

    /// <summary>
    /// Returns whether <paramref name="login"/> is a GitHub user or organization login: 1 to 39 letters, digits and
    /// hyphens, not starting or ending with a hyphen and not a Windows device name.
    /// </summary>
    /// <remarks>The login becomes a folder name, so a device name such as <c>CON</c> is rejected.</remarks>
    /// <param name="login">The login to check.</param>
    /// <returns><see langword="true"/> when the login is valid.</returns>
    public static bool IsValidLogin(string? login) =>
        !string.IsNullOrEmpty(login)
        && login.Length <= _maxLoginLength
        && login[0] != '-'
        && login[^1] != '-'
        && login.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
        && !IsWindowsDeviceName(login);

    /// <summary>
    /// Returns whether <paramref name="name"/> is a GitHub repository name: 1 to 100 letters, digits, <c>.</c>,
    /// <c>_</c> and <c>-</c>, not ending with <c>.</c>, without a <c>.git</c> suffix and not a Windows device name.
    /// </summary>
    /// <remarks>
    /// The name becomes a folder name, so the rule also keeps it from escaping its parent folder (<c>..</c>), from
    /// being silently shortened by Windows (a trailing dot) and from naming a device (<c>NUL</c>, <c>con.txt</c>).
    /// </remarks>
    /// <param name="name">The name to check.</param>
    /// <returns><see langword="true"/> when the name is valid.</returns>
    public static bool IsValidRepositoryName(string? name) =>
        !string.IsNullOrEmpty(name)
        && name.Length <= _maxRepositoryNameLength
        && !name.EndsWith('.')
        && !name.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')
        && !IsWindowsDeviceName(name);

    /// <summary>
    /// Returns whether <paramref name="name"/> is a reserved Windows device name (<c>CON</c>, <c>PRN</c>,
    /// <c>AUX</c>, <c>NUL</c>, <c>COM1</c>–<c>COM9</c>, <c>LPT1</c>–<c>LPT9</c>) in any case, with or without an
    /// extension.
    /// </summary>
    /// <param name="name">A file or folder name.</param>
    /// <returns><see langword="true"/> when Windows would open a device instead of a file system entry.</returns>
    private static bool IsWindowsDeviceName(string name)
    {
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        var stem = dot < 0 ? name : name[..dot];
        return stem.ToUpperInvariant() switch
        {
            "CON" or "PRN" or "AUX" or "NUL" => true,
            ['C', 'O', 'M', >= '1' and <= '9'] or ['L', 'P', 'T', >= '1' and <= '9'] => true,
            _ => false,
        };
    }

    /// <summary>
    /// Splits the path of a URL that starts with <see cref="_prefix"/> into its segments.
    /// </summary>
    /// <param name="url">The URL to split.</param>
    /// <param name="segments">The path segments, possibly empty strings, when the URL starts with the prefix.</param>
    /// <returns><see langword="true"/> when <paramref name="url"/> starts with <see cref="_prefix"/>.</returns>
    private static bool TrySplitPath(string? url, out string[] segments)
    {
        segments = [];
        if (url is null || !url.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = url.AsSpan(_prefix.Length);
        if (path.EndsWith("/"))
        {
            path = path[..^1];
        }

        segments = path.ToString().Split('/');
        return true;
    }
}
