namespace GitHubBackup.Core.Archiving;

/// <summary>
/// The file name of a new archive together with the time written into it.
/// </summary>
/// <param name="FileName">The file name, without a folder.</param>
/// <param name="CreatedAt">The local time of the archive with its offset from UTC, as written into the file name.</param>
public sealed record ArchiveName(string FileName, DateTimeOffset CreatedAt);
