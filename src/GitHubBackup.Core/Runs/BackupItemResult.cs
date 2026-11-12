using GitHubBackup.Core.State;

namespace GitHubBackup.Core.Runs;

/// <summary>
/// The outcome of one repository or wiki in a backup run, as shown in the summary.
/// </summary>
/// <param name="Repository">The repository as <c>owner/name</c>, or its wiki as <c>owner/name.wiki</c>.</param>
/// <param name="Status">The outcome.</param>
/// <param name="ArchiveFileName">The file name of the archive created in this run, or <see langword="null"/> when none was.</param>
/// <param name="Duration">The time spent on the repository.</param>
/// <param name="Message">The error, the warning or the reason for skipping, or <see langword="null"/>.</param>
public sealed record BackupItemResult(
    string Repository, BackupItemStatus Status, string? ArchiveFileName, TimeSpan Duration, string? Message)
{
    /// <summary>Gets what the result is about: the repository or its wiki.</summary>
    public BackupTarget Target { get; init; } = BackupTarget.Repository;

    /// <summary>
    /// Gets the branch the clone has checked out, or the branch a dry run reads, or <see langword="null"/> when the
    /// item did not reach a branch.
    /// </summary>
    public string? Branch { get; init; }

    /// <summary>Gets the commit <see cref="Branch"/> points at, or <see langword="null"/> when there is none.</summary>
    public string? HeadCommit { get; init; }

    /// <summary>
    /// Gets the refs that changed since the previous archive when the item has an archive in this run, all the refs
    /// for the first archive; empty otherwise.
    /// </summary>
    public IReadOnlyList<string> ChangedRefs { get; init; } = [];

    /// <summary>
    /// Returns whether <paramref name="other"/> describes the same outcome, comparing <see cref="ChangedRefs"/> by
    /// content.
    /// </summary>
    /// <param name="other">The result to compare with.</param>
    /// <returns><see langword="true"/> when every property is equal.</returns>
    public bool Equals(BackupItemResult? other) =>
        other is not null
        && Repository == other.Repository
        && Status == other.Status
        && ArchiveFileName == other.ArchiveFileName
        && Duration == other.Duration
        && Message == other.Message
        && Target == other.Target
        && Branch == other.Branch
        && HeadCommit == other.HeadCommit
        && ChangedRefs.SequenceEqual(other.ChangedRefs, StringComparer.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(Repository, Status, ArchiveFileName, Duration, Message, Target, Branch, HeadCommit);
}
