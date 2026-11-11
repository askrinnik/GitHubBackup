namespace GitHubBackup.Core.Runs;

/// <summary>
/// The outcome of one repository in a backup run, as shown in the summary.
/// </summary>
/// <param name="Repository">The repository, as <c>owner/name</c>.</param>
/// <param name="Status">The outcome.</param>
/// <param name="ArchiveFileName">The file name of the archive created in this run, or <see langword="null"/> when none was.</param>
/// <param name="Duration">The time spent on the repository.</param>
/// <param name="Message">The error, the warning or the reason for skipping, or <see langword="null"/>.</param>
public sealed record BackupItemResult(
    string Repository, BackupItemStatus Status, string? ArchiveFileName, TimeSpan Duration, string? Message);
