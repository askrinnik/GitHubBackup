namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Holds the <c>History</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class HistoryOptions
{
    /// <summary>The configuration section the options bind to.</summary>
    public const string SectionName = "History";

    /// <summary>
    /// Gets or sets the path of the run history database; a relative path is resolved against the application folder.
    /// </summary>
    public string DatabasePath { get; set; } = @"data\history.db";
}
