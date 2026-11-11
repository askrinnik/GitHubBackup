namespace GitHubBackup.Core.State;

/// <summary>
/// Holds the content of the state file in the archive folder of one repository: the archives created for the
/// repository and, in a separate section, for its wiki.
/// </summary>
/// <param name="SchemaVersion">The version of the file format.</param>
/// <param name="RepositoryId">The GitHub id of the repository, which survives a rename.</param>
/// <param name="FullName">The <c>owner/name</c> of the repository when the last archive was recorded.</param>
/// <param name="Repository">The archives of the repository.</param>
/// <param name="Wiki">The archives of the wiki.</param>
public sealed record BackupState(
    int SchemaVersion, long RepositoryId, string FullName, BackupStateSection Repository, BackupStateSection Wiki)
{
    /// <summary>The only version of the file format this application reads and writes.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>The name of the state file in the archive folder of a repository.</summary>
    public const string FileName = ".backup-state.json";

    /// <summary>
    /// Creates the state of a repository that has no archives yet.
    /// </summary>
    /// <param name="repositoryId">The GitHub id of the repository.</param>
    /// <param name="fullName">The <c>owner/name</c> of the repository.</param>
    /// <returns>The state with the current schema version and two empty sections.</returns>
    public static BackupState CreateEmpty(long repositoryId, string fullName) =>
        new(CurrentSchemaVersion, repositoryId, fullName, BackupStateSection.Empty, BackupStateSection.Empty);

    /// <summary>
    /// Returns the section of <paramref name="target"/>.
    /// </summary>
    /// <param name="target">The backup target.</param>
    /// <returns>The section.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="target"/> is not a defined value.</exception>
    public BackupStateSection GetSection(BackupTarget target) => target switch
    {
        BackupTarget.Repository => Repository,
        BackupTarget.Wiki => Wiki,
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "The backup target is not defined."),
    };

    /// <summary>
    /// Returns the most recently created archive of <paramref name="target"/>.
    /// </summary>
    /// <param name="target">The backup target.</param>
    /// <returns>The archive, or <see langword="null"/> when the target has none.</returns>
    public ArchiveRecord? GetLastArchive(BackupTarget target) => GetSection(target).LastArchive;

    /// <summary>
    /// Returns a state with <paramref name="record"/> added after the archives of <paramref name="target"/>.
    /// </summary>
    /// <remarks>
    /// The full name is replaced because a renamed repository keeps its id and archive folder; the other section is
    /// left as it is.
    /// </remarks>
    /// <param name="target">The backup target the archive belongs to.</param>
    /// <param name="record">The archive to add.</param>
    /// <param name="fullName">The current <c>owner/name</c> of the repository.</param>
    /// <returns>The new state; this one is unchanged.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="target"/> is not a defined value.</exception>
    public BackupState WithArchive(BackupTarget target, ArchiveRecord record, string fullName) => target switch
    {
        BackupTarget.Repository => this with { FullName = fullName, Repository = Repository.Append(record) },
        BackupTarget.Wiki => this with { FullName = fullName, Wiki = Wiki.Append(record) },
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "The backup target is not defined."),
    };
}
