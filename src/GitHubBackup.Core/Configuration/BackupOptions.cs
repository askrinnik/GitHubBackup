namespace GitHubBackup.Core.Configuration;

/// <summary>
/// Holds the <c>Backup</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class BackupOptions
{
    /// <summary>The configuration section the options bind to.</summary>
    public const string SectionName = "Backup";

    /// <summary>The smallest accepted <see cref="ShortHashLength"/>.</summary>
    public const int MinShortHashLength = 4;

    /// <summary>The largest accepted <see cref="ShortHashLength"/>: the length of a full SHA-1 commit hash.</summary>
    public const int MaxShortHashLength = 40;

    /// <summary>
    /// Gets or sets the path of <c>backup-config.json</c>; a relative path is resolved against the application folder.
    /// </summary>
    public string ConfigPath { get; set; } = "backup-config.json";

    /// <summary>Gets or sets the number of commit hash characters used in archive names.</summary>
    public int ShortHashLength { get; set; } = 10;

    /// <summary>Gets or sets a value indicating whether each new archive is tested after it is created.</summary>
    public bool VerifyArchive { get; set; } = true;
}
