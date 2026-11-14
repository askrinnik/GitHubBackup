using System.Globalization;
using GitHubBackup.Core.History;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// One archive of the archive history, every value as display text.
/// </summary>
public sealed record ArchiveHistoryRow
{
    /// <summary>The format of the creation time of an archive.</summary>
    public const string DateFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>The file state of an archive whose file is in the archive folder.</summary>
    public const string PresentText = "Present";

    /// <summary>The file state of an archive whose file is not in the archive folder.</summary>
    public const string MissingText = "Missing";

    /// <summary>The number of characters of the commit id the list shows.</summary>
    public const int ShortHashLength = 10;

    /// <summary>The units of a file size, each 1024 times the previous one.</summary>
    private static readonly string[] _sizeUnits = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>Gets whether the archive holds the repository or its wiki.</summary>
    public required string Target { get; init; }

    /// <summary>Gets the file name of the archive.</summary>
    public required string File { get; init; }

    /// <summary>Gets the absolute path of the archive file.</summary>
    public required string FullPath { get; init; }

    /// <summary>Gets the local time the archive was created, as recorded.</summary>
    public required string CreatedAt { get; init; }

    /// <summary>Gets the branch checked out in the archived clone.</summary>
    public required string Branch { get; init; }

    /// <summary>Gets the full id of the archived commit.</summary>
    public required string HeadCommit { get; init; }

    /// <summary>Gets the first <see cref="ShortHashLength"/> characters of the archived commit.</summary>
    public required string ShortHash { get; init; }

    /// <summary>Gets the size of the archive with a unit.</summary>
    public required string Size { get; init; }

    /// <summary>Gets the refs that changed since the previous archive, separated by commas.</summary>
    public required string ChangedRefs { get; init; }

    /// <summary>Gets whether the archive file is not in the archive folder.</summary>
    public required bool IsMissing { get; init; }

    /// <summary>Gets <see cref="MissingText"/> or <see cref="PresentText"/>.</summary>
    public string FileState => IsMissing ? MissingText : PresentText;

    /// <summary>
    /// Creates the row of an archive.
    /// </summary>
    /// <param name="entry">The archive.</param>
    /// <returns>The row.</returns>
    public static ArchiveHistoryRow Create(ArchiveHistoryEntry entry) => new()
    {
        Target = entry.Target.ToString(),
        File = entry.File,
        FullPath = entry.FullPath,
        CreatedAt = entry.CreatedAt.ToString(DateFormat, CultureInfo.InvariantCulture),
        Branch = entry.Branch,
        HeadCommit = entry.HeadCommit,
        ShortHash = entry.HeadCommit.Length > ShortHashLength ? entry.HeadCommit[..ShortHashLength] : entry.HeadCommit,
        Size = FormatSize(entry.SizeBytes),
        ChangedRefs = string.Join(", ", entry.ChangedRefs),
        IsMissing = !entry.FileExists,
    };

    /// <summary>
    /// Formats a size in bytes with the largest unit that keeps the value at least one, with one decimal above bytes.
    /// </summary>
    /// <param name="bytes">The size in bytes; not negative.</param>
    /// <returns>The size, such as <c>512 B</c> or <c>1.5 MB</c>.</returns>
    public static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes} B");
        }

        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < _sizeUnits.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{value:0.0} {_sizeUnits[unit]}");
    }
}
