namespace GitHubBackup.TestUtilities.Git;

/// <summary>
/// Writes a git configuration file that redirects <c>https://github.com/</c> to a local folder of bare repositories,
/// for an executable under test that clones from GitHub addresses.
/// </summary>
public sealed class GitRemoteRedirect
{
    /// <summary>The name of the git configuration file in the target folder.</summary>
    public const string FileName = "redirect.gitconfig";

    /// <summary>
    /// Writes <see cref="FileName"/> into <paramref name="folder"/>.
    /// </summary>
    /// <param name="folder">The absolute path of the folder that receives the configuration file.</param>
    /// <param name="remotesRoot">
    /// The folder of the bare repositories; <c>https://github.com/&lt;owner&gt;/&lt;name&gt;.git</c> is redirected to
    /// <c>&lt;remotesRoot&gt;\&lt;owner&gt;\&lt;name&gt;.git</c>.
    /// </param>
    public GitRemoteRedirect(string folder, string remotesRoot)
    {
        ConfigPath = Path.Combine(folder, FileName);
        var remotesUrl = new Uri(Path.TrimEndingDirectorySeparator(remotesRoot) + Path.DirectorySeparatorChar).AbsoluteUri;
        File.WriteAllText(
            ConfigPath,
            $"""
            [url "{remotesUrl}"]
                insteadOf = https://github.com/
            """);
    }

    /// <summary>Gets the absolute path of the git configuration file.</summary>
    public string ConfigPath { get; }

    /// <summary>
    /// Returns the environment variables that make git read <see cref="ConfigPath"/> as its global configuration and
    /// skip the system configuration of the machine.
    /// </summary>
    /// <returns>The variable names and values.</returns>
    public IReadOnlyDictionary<string, string> EnvironmentVariables() =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GIT_CONFIG_GLOBAL"] = ConfigPath,
            ["GIT_CONFIG_NOSYSTEM"] = "1",
        };
}
