namespace GitHubBackup.Core.Overview;

/// <summary>
/// Describes one repository of <c>backup-config.json</c> with its status and the date of its last archive.
/// </summary>
/// <param name="Owner">The login of the repository owner.</param>
/// <param name="Name">The repository name.</param>
/// <param name="AccountLogin">
/// The login of the tracked account that lists the repository, or <see langword="null"/> for a standalone repository.
/// </param>
/// <param name="Status">The status of the repository.</param>
/// <param name="LastArchiveAt">
/// The local time the last archive of the repository was created, or <see langword="null"/> when it has none or its
/// state file cannot be read.
/// </param>
/// <param name="StateError">
/// Why the state file of the repository cannot be read, or <see langword="null"/> when it was read or does not exist.
/// </param>
public sealed record RepositoryOverviewEntry(
    string Owner,
    string Name,
    string? AccountLogin,
    RepositoryOverviewStatus Status,
    DateTimeOffset? LastArchiveAt,
    string? StateError)
{
    /// <summary>Gets the repository as <c>owner/name</c>.</summary>
    public string FullName => $"{Owner}/{Name}";
}
