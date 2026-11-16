namespace GitHubBackup.Core.Cleanup;

/// <summary>
/// What the cleanup of the old archives of one repository changes: the archive files it moves to the Recycle Bin and
/// the records without a file it removes from <c>.backup-state.json</c>.
/// </summary>
/// <remarks>
/// Two plans are equal when they list the same files with the same sizes and the same records in the same order, so a
/// plan built again under the run lock can be compared with the one the user confirmed.
/// </remarks>
/// <param name="FilesToRecycle">The archive files to move to the Recycle Bin, the repository first, then the wiki.</param>
/// <param name="RecordsWithoutFiles">The records whose file is not on the disk, to remove from the state file.</param>
public sealed record ArchiveCleanupPlan(
    IReadOnlyList<ArchiveCleanupFile> FilesToRecycle, IReadOnlyList<ArchiveCleanupRecord> RecordsWithoutFiles)
{
    /// <summary>Gets a plan that changes nothing.</summary>
    public static ArchiveCleanupPlan Empty { get; } = new([], []);

    /// <summary>Gets whether the plan changes nothing.</summary>
    public bool IsEmpty => FilesToRecycle.Count == 0 && RecordsWithoutFiles.Count == 0;

    /// <summary>Gets the number of files to move to the Recycle Bin.</summary>
    public int FileCount => FilesToRecycle.Count;

    /// <summary>Gets the total size in bytes of the files to move to the Recycle Bin.</summary>
    public long TotalSizeBytes => FilesToRecycle.Sum(file => file.SizeBytes);

    /// <summary>Gets the number of records without a file to remove.</summary>
    public int RecordCount => RecordsWithoutFiles.Count;

    /// <summary>
    /// Returns whether <paramref name="other"/> lists the same files and records in the same order.
    /// </summary>
    /// <param name="other">The plan to compare with.</param>
    /// <returns><see langword="true"/> when both plans change the same things.</returns>
    public bool Equals(ArchiveCleanupPlan? other) =>
        other is not null
        && FilesToRecycle.SequenceEqual(other.FilesToRecycle)
        && RecordsWithoutFiles.SequenceEqual(other.RecordsWithoutFiles);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var file in FilesToRecycle)
        {
            hash.Add(file);
        }

        foreach (var record in RecordsWithoutFiles)
        {
            hash.Add(record);
        }

        return hash.ToHashCode();
    }
}
