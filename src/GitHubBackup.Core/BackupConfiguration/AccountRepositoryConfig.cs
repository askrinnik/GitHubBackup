namespace GitHubBackup.Core.BackupConfiguration;

/// <summary>
/// Describes a repository of a tracked account, identified by its name under the account.
/// </summary>
public sealed class AccountRepositoryConfig
{
    /// <summary>Gets or sets the repository name, without the owner.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the GitHub ID, which survives a rename of the repository.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the branch checked out in the clone; <see langword="null"/> selects the default branch.</summary>
    public string? Branch { get; set; }

    /// <summary>
    /// Gets or sets the absolute path of the archive folder of this repository; <see langword="null"/> keeps the
    /// folder derived from the account.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>Gets or sets whether the repository is still found on GitHub.</summary>
    public RepositoryStatus Status { get; set; }
}
