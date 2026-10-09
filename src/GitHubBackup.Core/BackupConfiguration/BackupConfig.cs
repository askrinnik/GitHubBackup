namespace GitHubBackup.Core.BackupConfiguration;

/// <summary>
/// Holds the contents of <c>backup-config.json</c>: the backup roots, the tracked accounts and the standalone
/// repositories.
/// </summary>
public sealed class BackupConfig
{
    /// <summary>The only <see cref="SchemaVersion"/> this version of the application reads and writes.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>The name of the clone folder in the application folder used when <see cref="SourcesRoot"/> is <see langword="null"/>.</summary>
    public const string DefaultSourcesFolderName = "sources";

    /// <summary>Gets or sets the version of the file format.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Gets or sets the absolute path of the root folder of the archives.</summary>
    public string BackupRoot { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path of the root folder of the clones; <see langword="null"/> selects
    /// <see cref="DefaultSourcesFolderName"/> in the application folder.
    /// </summary>
    public string? SourcesRoot { get; set; }

    /// <summary>Gets or sets the exclusion masks applied to every repository.</summary>
    public List<string> Exclude { get; set; } = [];

    /// <summary>Gets or sets the tracked accounts.</summary>
    public List<AccountConfig> Accounts { get; set; } = [];

    /// <summary>Gets or sets the standalone repositories, tracked without their account.</summary>
    public List<RepositoryConfig> Repositories { get; set; } = [];
}
