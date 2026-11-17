namespace GitHubBackup.TestUtilities;

/// <summary>
/// Describes one state of the GitHub API and of <c>backup-config.json</c> that the desktop app is started in, for the
/// screenshot tests and the test bench.
/// </summary>
/// <param name="Name">The name of the scenario on the command line of the test bench, in kebab case.</param>
/// <param name="Description">What the scenario shows and what to do in the app to see it.</param>
/// <param name="Apply">Sets up the responses of the API stub and adds the entries of the configuration.</param>
public sealed record UiScenario(string Name, string Description, Action<GitHubApiStub, BackupConfigJson> Apply)
{
    /// <summary>
    /// Sets up <paramref name="api"/> and writes the configuration of the scenario to <paramref name="configPath"/>.
    /// </summary>
    /// <param name="api">The API stub the application calls.</param>
    /// <param name="archivesPath">The absolute path of the archive root of the configuration.</param>
    /// <param name="configPath">The path of <c>backup-config.json</c>.</param>
    public void Prepare(GitHubApiStub api, string archivesPath, string configPath)
    {
        var config = new BackupConfigJson(archivesPath);
        Apply(api, config);
        config.WriteTo(configPath);
    }
}
