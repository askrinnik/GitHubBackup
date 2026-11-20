using System.Text.Json;

namespace GitHubBackup.TestUtilities;

/// <summary>
/// Builds the text of a <c>backup-config.json</c> with standalone repositories, accounts, global exclusion masks and
/// the default clone and archive paths of every repository.
/// </summary>
/// <param name="backupRoot">The absolute path of the archive root.</param>
public sealed class BackupConfigJson(string backupRoot)
{
    private readonly List<string> _accounts = [];
    private readonly List<string> _repositories = [];
    private readonly List<string> _exclude = [];
    private bool _discoverAccessible;

    /// <summary>
    /// Adds a standalone repository on its default branch.
    /// </summary>
    /// <param name="owner">The owner login.</param>
    /// <param name="name">The repository name.</param>
    /// <param name="id">The repository id.</param>
    /// <param name="status">The status of the entry, <c>Active</c> or <c>Unavailable</c>.</param>
    /// <param name="paused">Whether the entry sets <c>"paused": true</c>.</param>
    /// <returns>This builder.</returns>
    public BackupConfigJson WithRepository(string owner, string name, long id, string status = "Active", bool paused = false)
    {
        _repositories.Add(
            $$"""
            { "url": "https://github.com/{{owner}}/{{name}}", "id": {{id}}, "branch": null, "path": null{{PausedKey(paused)}}, "status": "{{status}}" }
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
    public BackupConfigJson WithAccount(string login, params (string Name, long Id)[] repositories) =>
        WithAccount(login, paused: false, [.. repositories.Select(repository => (repository.Name, repository.Id, false))]);

    /// <summary>
    /// Adds an account with its active repositories, each of them and the account paused or not.
    /// </summary>
    /// <param name="login">The account login.</param>
    /// <param name="paused">Whether the account sets <c>"paused": true</c>.</param>
    /// <param name="repositories">The name, id and pause of each repository of the account.</param>
    /// <returns>This builder.</returns>
    public BackupConfigJson WithAccount(string login, bool paused, params (string Name, long Id, bool Paused)[] repositories)
    {
        var entries = repositories.Select(repository =>
            $$"""
            { "name": "{{repository.Name}}", "id": {{repository.Id}}, "branch": null, "path": null{{PausedKey(repository.Paused)}}, "status": "Active" }
            """);
        _accounts.Add(
            $$"""
            { "url": "https://github.com/{{login}}", "path": null, "exclude": []{{PausedKey(paused)}}, "repositories": [ {{string.Join(",", entries)}} ] }
            """);
        return this;
    }

    /// <summary>
    /// Adds a global exclusion mask.
    /// </summary>
    /// <param name="mask">The mask, matched against <c>owner/name</c>.</param>
    /// <returns>This builder.</returns>
    public BackupConfigJson WithExclude(string mask)
    {
        _exclude.Add(JsonSerializer.Serialize(mask));
        return this;
    }

    /// <summary>
    /// Sets <c>discoverAccessibleRepositories</c> to <see langword="true"/>.
    /// </summary>
    /// <returns>This builder.</returns>
    public BackupConfigJson WithDiscoverAccessibleRepositories()
    {
        _discoverAccessible = true;
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
          "exclude": [ {{string.Join(",", _exclude)}} ],
          "discoverAccessibleRepositories": {{(_discoverAccessible ? "true" : "false")}},
          "accounts": [ {{string.Join(",", _accounts)}} ],
          "repositories": [ {{string.Join(",", _repositories)}} ]
        }
        """;

    /// <summary>
    /// Returns the <c>paused</c> key with its leading comma, written only for <see langword="true"/> as the program
    /// writes it.
    /// </summary>
    /// <param name="paused">Whether the entry is paused.</param>
    /// <returns>The key, or an empty string.</returns>
    private static string PausedKey(bool paused) => paused ? ", \"paused\": true" : string.Empty;

    /// <summary>
    /// Writes the JSON text to <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    public void WriteTo(string path) => File.WriteAllText(path, Build());
}
