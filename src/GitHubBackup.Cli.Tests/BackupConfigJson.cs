using System.Text.Json;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Builds the text of a <c>backup-config.json</c> with standalone repositories and accounts, no exclusion masks and
/// the default clone and archive paths of every repository.
/// </summary>
/// <param name="backupRoot">The absolute path of the archive root.</param>
internal sealed class BackupConfigJson(string backupRoot)
{
    private readonly List<string> _accounts = [];
    private readonly List<string> _repositories = [];

    /// <summary>
    /// Adds a standalone repository on its default branch.
    /// </summary>
    /// <param name="owner">The owner login.</param>
    /// <param name="name">The repository name.</param>
    /// <param name="id">The repository id.</param>
    /// <param name="status">The status of the entry, <c>Active</c> or <c>Unavailable</c>.</param>
    /// <returns>This builder.</returns>
    public BackupConfigJson WithRepository(string owner, string name, long id, string status = "Active")
    {
        _repositories.Add(
            $$"""
            { "url": "https://github.com/{{owner}}/{{name}}", "id": {{id}}, "branch": null, "path": null, "status": "{{status}}" }
            """);
        return this;
    }

    /// <summary>
    /// Adds an active standalone repository on its default branch without an <c>id</c>.
    /// </summary>
    /// <param name="owner">The owner login.</param>
    /// <param name="name">The repository name.</param>
    /// <returns>This builder.</returns>
    public BackupConfigJson WithRepositoryWithoutId(string owner, string name)
    {
        _repositories.Add(
            $$"""
            { "url": "https://github.com/{{owner}}/{{name}}", "branch": null, "path": null, "status": "Active" }
            """);
        return this;
    }

    /// <summary>
    /// Adds an account with its active repositories.
    /// </summary>
    /// <param name="login">The account login.</param>
    /// <param name="repositories">The name and id of each repository of the account.</param>
    /// <returns>This builder.</returns>
    public BackupConfigJson WithAccount(string login, params (string Name, long Id)[] repositories)
    {
        var entries = repositories.Select(repository =>
            $$"""
            { "name": "{{repository.Name}}", "id": {{repository.Id}}, "branch": null, "path": null, "status": "Active" }
            """);
        _accounts.Add(
            $$"""
            { "url": "https://github.com/{{login}}", "path": null, "exclude": [], "repositories": [ {{string.Join(",", entries)}} ] }
            """);
        return this;
    }

    /// <summary>
    /// Returns the JSON text.
    /// </summary>
    /// <returns>The content of <c>backup-config.json</c>.</returns>
    public string Build() =>
        $$"""
        {
          "schemaVersion": 1,
          "backupRoot": {{JsonSerializer.Serialize(backupRoot)}},
          "sourcesRoot": null,
          "exclude": [],
          "accounts": [ {{string.Join(",", _accounts)}} ],
          "repositories": [ {{string.Join(",", _repositories)}} ]
        }
        """;

    /// <summary>
    /// Writes the JSON text to <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    public void WriteTo(string path) => File.WriteAllText(path, Build());
}
