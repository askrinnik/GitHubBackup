namespace GitHubBackup.Core.State;

/// <summary>
/// Lists the archives of one backup target in the order they were created.
/// </summary>
public sealed record BackupStateSection
{
    /// <summary>
    /// Creates a section that holds a copy of <paramref name="archives"/>.
    /// </summary>
    /// <param name="archives">The archives, oldest first.</param>
    public BackupStateSection(IReadOnlyList<ArchiveRecord> archives)
    {
        Archives = [.. archives];
    }

    /// <summary>Gets a section without archives.</summary>
    public static BackupStateSection Empty { get; } = new([]);

    /// <summary>Gets the archives, oldest first.</summary>
    public IReadOnlyList<ArchiveRecord> Archives { get; }

    /// <summary>Gets the most recently created archive, or <see langword="null"/> when there is none.</summary>
    public ArchiveRecord? LastArchive => Archives.Count > 0 ? Archives[^1] : null;

    /// <summary>
    /// Returns a section with <paramref name="record"/> after the existing archives.
    /// </summary>
    /// <param name="record">The archive to add.</param>
    /// <returns>The new section; this one is unchanged.</returns>
    public BackupStateSection Append(ArchiveRecord record) => new([.. Archives, record]);
}
