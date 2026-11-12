using GitHubBackup.Core.Runs;
using GitHubBackup.Core.State;

namespace GitHubBackup.Infrastructure.History;

/// <summary>
/// A row of the <c>RunItems</c> table: one repository or wiki of a run.
/// </summary>
internal sealed class RunItemEntity
{
    /// <summary>Gets or sets the key of the row, which also keeps the order of the items in the run.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the key of the run the item belongs to.</summary>
    public long RunEntityId { get; set; }

    /// <summary>Gets or sets the repository as <c>owner/name</c>.</summary>
    public required string Repository { get; set; }

    /// <summary>Gets or sets whether the item is the repository or its wiki; stored as the name of the value.</summary>
    public BackupTarget Target { get; set; }

    /// <summary>Gets or sets the outcome; stored as the name of the value.</summary>
    public BackupItemStatus Status { get; set; }

    /// <summary>Gets or sets the time spent on the item.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>Gets or sets the branch that was backed up, or <see langword="null"/>.</summary>
    public string? Branch { get; set; }

    /// <summary>Gets or sets the HEAD commit that was backed up, or <see langword="null"/>.</summary>
    public string? HeadCommit { get; set; }

    /// <summary>Gets or sets the file name of the archive created in the run, or <see langword="null"/>.</summary>
    public string? ArchiveFileName { get; set; }

    /// <summary>Gets or sets the refs that changed since the previous archive; stored as a JSON array.</summary>
    public List<string> ChangedRefs { get; set; } = [];

    /// <summary>Gets or sets the error, the warning or the reason for skipping, or <see langword="null"/>.</summary>
    public string? Error { get; set; }
}
