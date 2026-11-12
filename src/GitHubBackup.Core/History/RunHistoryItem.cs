using GitHubBackup.Core.Runs;
using GitHubBackup.Core.State;

namespace GitHubBackup.Core.History;

/// <summary>
/// The outcome of one repository or wiki in a run, as the run history keeps it.
/// </summary>
/// <param name="Repository">The repository as <c>owner/name</c>.</param>
/// <param name="Target">Whether the item is the repository or its wiki.</param>
/// <param name="Status">The outcome.</param>
/// <param name="Duration">The time spent on the item.</param>
public sealed record RunHistoryItem(string Repository, BackupTarget Target, BackupItemStatus Status, TimeSpan Duration)
{
    /// <summary>Gets the branch that was backed up, or <see langword="null"/> when the item did not reach it.</summary>
    public string? Branch { get; init; }

    /// <summary>Gets the HEAD commit that was backed up, or <see langword="null"/> when the item did not reach it.</summary>
    public string? HeadCommit { get; init; }

    /// <summary>Gets the file name of the archive created in the run, or <see langword="null"/> when none was.</summary>
    public string? ArchiveFileName { get; init; }

    /// <summary>Gets the refs that changed since the previous archive, empty when none did.</summary>
    public IReadOnlyList<string> ChangedRefs { get; init; } = [];

    /// <summary>Gets the error, the warning or the reason for skipping, or <see langword="null"/>.</summary>
    public string? Error { get; init; }
}
