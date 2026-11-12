namespace GitHubBackup.Core.History;

/// <summary>
/// Reports that the run history database cannot be opened, migrated, read or written.
/// </summary>
/// <param name="databasePath">The path of the database file.</param>
/// <param name="reason">What failed, as one sentence.</param>
/// <param name="innerException">The exception that caused the failure, or <see langword="null"/>.</param>
public sealed class RunHistoryException(string databasePath, string reason, Exception? innerException = null)
    : Exception($"The run history database '{databasePath}' cannot be used: {reason}", innerException)
{
    /// <summary>Gets the path of the database file.</summary>
    public string DatabasePath { get; } = databasePath;
}
