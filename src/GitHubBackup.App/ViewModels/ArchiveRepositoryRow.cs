using System.Globalization;
using GitHubBackup.Core.Cleanup;
using GitHubBackup.Core.History;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// One repository of the repository table of the archive history: its archive counters with display text and the
/// values the columns sort by.
/// </summary>
/// <remarks>
/// A class rather than a record: two reads of the same repository must be different selections, so a reloaded table
/// shows the archives of the new read.
/// </remarks>
public sealed class ArchiveRepositoryRow
{
    /// <summary>The text of the In state cell of a repository whose files cannot be read.</summary>
    public const string ErrorText = "Error";

    /// <summary>Gets the repository as <c>owner/name</c>.</summary>
    public required string Repository { get; init; }

    /// <summary>Gets the login of the account that lists the repository; empty for a standalone repository.</summary>
    public required string Account { get; init; }

    /// <summary>Gets the GitHub id of the repository.</summary>
    public required long RepositoryId { get; init; }

    /// <summary>Gets the absolute archive folder of the repository.</summary>
    public required string ArchiveFolder { get; init; }

    /// <summary>Gets the archives of the repository and its wiki, or <see langword="null"/> when they cannot be read.</summary>
    public required ArchiveHistory? History { get; init; }

    /// <summary>Gets why the archives cannot be read, masked for display, or <see langword="null"/>.</summary>
    public required string? Error { get; init; }

    /// <summary>
    /// Gets what the cleanup of old archives would remove, or <see langword="null"/> when the archives cannot be read.
    /// </summary>
    public required ArchiveCleanupPlan? CleanupPlan { get; init; }

    /// <summary>Gets whether the archives cannot be read.</summary>
    public bool HasError => Error is not null;

    /// <summary>Gets the number of records in the state file, or <see langword="null"/> on an error.</summary>
    public int? InState => History?.InStateCount;

    /// <summary>Gets <see cref="InState"/> as text, or <see cref="ErrorText"/> on an error.</summary>
    public string InStateText => HasError
        ? ErrorText
        : InState?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>Gets the number of archive files in the folder, or <see langword="null"/> on an error.</summary>
    public int? OnDisk => History?.OnDiskCount;

    /// <summary>Gets the number of records without a file, or <see langword="null"/> on an error.</summary>
    public int? Missing => History?.MissingCount;

    /// <summary>Gets the number of archive files without a record, or <see langword="null"/> on an error.</summary>
    public int? NotInState => History?.NotInStateCount;

    /// <summary>Gets the total size in bytes of the archive files, or <see langword="null"/> on an error.</summary>
    public long? SizeBytes => History?.SizeOnDiskBytes;

    /// <summary>Gets <see cref="SizeBytes"/> with a unit; empty on an error.</summary>
    public string Size => SizeBytes is { } bytes ? ArchiveHistoryRow.FormatSize(bytes) : string.Empty;

    /// <summary>
    /// Gets the creation time of the most recent recorded archive of the repository or its wiki, or
    /// <see langword="null"/> when there is none or on an error.
    /// </summary>
    public DateTimeOffset? LastArchiveAt => History?.LastArchiveAt;

    /// <summary>Gets <see cref="LastArchiveAt"/> as local time text; empty when there is none.</summary>
    public string LastArchive =>
        LastArchiveAt?.ToString(ArchiveHistoryRow.DateFormat, CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>Gets the state of the wiki according to the latest run of the repository.</summary>
    public WikiStatus WikiStatus { get; init; } = WikiStatus.Unknown;

    /// <summary>Gets <see cref="WikiStatus"/> as the text of the Wiki cell.</summary>
    public string WikiText => WikiStatus switch
    {
        WikiStatus.Ok => "OK",
        WikiStatus.NoPages => "No pages",
        WikiStatus.Failed => "Failed",
        WikiStatus.Cancelled => "Cancelled",
        WikiStatus.Off => "Off",
        _ => "—",
    };

    /// <summary>
    /// Gets the tooltip of the Wiki cell: the advice for an enabled wiki without pages, otherwise <see langword="null"/>.
    /// </summary>
    public string? WikiToolTip => WikiStatus == WikiStatus.NoPages
        ? "The wiki is enabled but has no pages. Turn off Wikis in the repository settings on GitHub to stop checking it."
        : null;

    /// <summary>
    /// Creates the row of a repository.
    /// </summary>
    /// <param name="summary">The repository with its archives or its error.</param>
    /// <param name="error">The error of <paramref name="summary"/> masked for display, or <see langword="null"/>.</param>
    /// <returns>The row.</returns>
    public static ArchiveRepositoryRow Create(RepositoryArchiveSummary summary, string? error) => new()
    {
        Repository = summary.Repository.FullName,
        Account = summary.Repository.AccountLogin ?? string.Empty,
        RepositoryId = summary.Repository.RepositoryId,
        ArchiveFolder = summary.Repository.ArchiveFolder,
        History = error is null ? summary.History : null,
        Error = error,
        CleanupPlan = error is null ? summary.CleanupPlan : null,
        WikiStatus = summary.WikiStatus,
    };
}
