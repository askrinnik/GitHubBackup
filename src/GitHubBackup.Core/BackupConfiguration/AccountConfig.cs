namespace GitHubBackup.Core.BackupConfiguration;

/// <summary>
/// Describes a tracked GitHub account and the repositories of it that the user confirmed.
/// </summary>
public sealed class AccountConfig
{
    /// <summary>Gets or sets the account URL, <c>https://github.com/&lt;login&gt;</c>.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path of the folder that replaces <c>&lt;backupRoot&gt;\&lt;owner&gt;</c> for the
    /// archives of this account; <see langword="null"/> keeps the default.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>Gets or sets the exclusion masks applied to the repositories of this account.</summary>
    public List<string> Exclude { get; set; } = [];

    /// <summary>Gets or sets the confirmed repositories of this account.</summary>
    public List<AccountRepositoryConfig> Repositories { get; set; } = [];
}
