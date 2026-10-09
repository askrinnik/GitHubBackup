namespace GitHubBackup.Core.BackupConfiguration;

/// <summary>
/// Describes a standalone repository, tracked by its URL without tracking its account.
/// </summary>
public sealed class RepositoryConfig
{
    /// <summary>Gets or sets the repository URL, <c>https://github.com/&lt;owner&gt;/&lt;repo&gt;</c>.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets the GitHub ID, which survives a rename of the repository.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the branch checked out in the clone; <see langword="null"/> selects the default branch.</summary>
    public string? Branch { get; set; }

    /// <summary>
    /// Gets or sets the absolute path of the archive folder of this repository; <see langword="null"/> selects
    /// <c>&lt;backupRoot&gt;\&lt;owner&gt;\&lt;repo&gt;</c>.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>Gets or sets whether the repository is still found on GitHub.</summary>
    public RepositoryStatus Status { get; set; }
}
