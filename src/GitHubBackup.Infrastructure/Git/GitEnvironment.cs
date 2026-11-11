using System.Globalization;
using System.Text;
using GitHubBackup.Core.Processes;

namespace GitHubBackup.Infrastructure.Git;

/// <summary>
/// Builds the environment variables of a git process: prompts disabled and configuration entries, including the
/// authorization header, passed through <c>GIT_CONFIG_COUNT</c>, <c>GIT_CONFIG_KEY_n</c> and
/// <c>GIT_CONFIG_VALUE_n</c>.
/// </summary>
/// <remarks>
/// Configuration given through the environment applies to that one process and the git processes it starts, such as
/// submodule clones, and is never written to <c>.git/config</c>, so the token cannot end up inside the clone.
/// </remarks>
internal static class GitEnvironment
{
    /// <summary>The configuration key of the extra HTTP header git sends to GitHub.</summary>
    internal const string ExtraHeaderKey = "http.https://github.com/.extraHeader";

    /// <summary>The user name GitHub expects together with a token in Basic authentication.</summary>
    private const string _tokenUserName = "x-access-token";

    /// <summary>
    /// Returns the environment variables for one git process.
    /// </summary>
    /// <param name="token">The GitHub token, or <see langword="null"/> or white space for anonymous access.</param>
    /// <param name="additionalConfiguration">
    /// Configuration entries set after <c>core.longpaths</c>, the empty <c>credential.helper</c> and
    /// <c>core.symlinks</c>, and before the authorization header.
    /// </param>
    /// <param name="ceilingDirectory">
    /// The folder above which git does not search for a repository, or <see langword="null"/> for no ceiling; a path that contains <c>;</c> also gets none, because git
    /// splits the variable at that character and would apply a wrong entry. Set
    /// to the parent of a clone, it makes git fail in a clone folder without <c>.git</c> instead of operating on a
    /// repository that contains the folder.
    /// </param>
    /// <returns>
    /// <c>GIT_TERMINAL_PROMPT</c>, <c>GCM_INTERACTIVE</c>, <c>GIT_CEILING_DIRECTORIES</c> when a ceiling is given,
    /// <c>GIT_CONFIG_COUNT</c> and a key and value per entry; the value of the header is marked as a secret.
    /// </returns>
    public static IReadOnlyList<ProcessEnvironmentVariable> Create(
        string? token,
        IReadOnlyList<KeyValuePair<string, string>> additionalConfiguration,
        string? ceilingDirectory)
    {
        // An empty credential.helper clears the helpers from the user's configuration, so a rejected or missing token
        // fails the command instead of letting a helper supply or store other credentials. With core.symlinks off, a
        // symbolic link in the repository is checked out as a plain file holding the target path, so the archive of the
        // clone never picks up the content of a file outside the clone.
        List<(string Key, string Value, bool IsSecret)> entries =
        [
            ("core.longpaths", "true", false),
            ("credential.helper", string.Empty, false),
            ("core.symlinks", "false", false),
        ];
        entries.AddRange(additionalConfiguration.Select(entry => (entry.Key, entry.Value, false)));
        if (!string.IsNullOrWhiteSpace(token))
        {
            entries.Add((ExtraHeaderKey, CreateAuthorizationHeader(token), true));
        }

        List<ProcessEnvironmentVariable> variables =
        [
            new("GIT_TERMINAL_PROMPT", "0"),
            new("GCM_INTERACTIVE", "never"),
            new("GIT_CONFIG_COUNT", entries.Count.ToString(CultureInfo.InvariantCulture)),
        ];

        if (!string.IsNullOrEmpty(ceilingDirectory) && !ceilingDirectory.Contains(';', StringComparison.Ordinal))
        {
            variables.Add(new("GIT_CEILING_DIRECTORIES", ceilingDirectory));
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var suffix = index.ToString(CultureInfo.InvariantCulture);
            variables.Add(new("GIT_CONFIG_KEY_" + suffix, entries[index].Key));
            variables.Add(new("GIT_CONFIG_VALUE_" + suffix, entries[index].Value, entries[index].IsSecret));
        }

        return variables;
    }

    /// <summary>
    /// Returns the Basic credentials that carry <paramref name="token"/>, encoded as Base64.
    /// </summary>
    /// <param name="token">The GitHub token.</param>
    /// <returns>The Base64 text of <c>x-access-token:&lt;token&gt;</c>.</returns>
    public static string EncodeCredentials(string token) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(_tokenUserName + ":" + token));

    /// <summary>
    /// Returns the <c>Authorization</c> header that carries <paramref name="token"/> with Basic authentication, the
    /// form GitHub accepts from git over HTTPS.
    /// </summary>
    /// <param name="token">The GitHub token.</param>
    /// <returns>The header line, for example <c>Authorization: Basic eC1hY2Nlc3MtdG9rZW46…</c>.</returns>
    public static string CreateAuthorizationHeader(string token) => "Authorization: Basic " + EncodeCredentials(token);
}
