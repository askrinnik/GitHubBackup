using GitHubBackup.Core.BackupConfiguration;

namespace GitHubBackup.Core.Discovery;

/// <summary>
/// A repository listed in <c>backup-config.json</c>, either under a tracked account or as a standalone entry.
/// </summary>
/// <param name="Owner">The login of the owner, taken from the account URL or the repository URL.</param>
/// <param name="Name">The repository name as configured.</param>
/// <param name="Id">The GitHub id stored in the configuration.</param>
/// <param name="Status">Whether the repository was last found on GitHub.</param>
/// <param name="AccountLogin">The login of the tracked account that lists the repository, or <see langword="null"/> for a standalone repository.</param>
public sealed record TrackedRepository(string Owner, string Name, long Id, RepositoryStatus Status, string? AccountLogin)
{
    /// <summary>Gets the repository as <c>owner/name</c>.</summary>
    public string FullName => $"{Owner}/{Name}";
}
