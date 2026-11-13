using GitHubBackup.Core.GitHub;

namespace GitHubBackup.Core.Discovery;

/// <summary>
/// A configuration entry without an ID and the repository GitHub returned for its name.
/// </summary>
/// <param name="Owner">The login of the owner, as configured.</param>
/// <param name="Name">The repository name, as configured.</param>
/// <param name="AccountLogin">
/// The login of the tracked account that lists the entry, or <see langword="null"/> for a standalone entry.
/// </param>
/// <param name="Repository">The repository GitHub returned, whose <see cref="GitHubRepository.Id"/> the entry gets.</param>
public sealed record ResolvedRepositoryId(string Owner, string Name, string? AccountLogin, GitHubRepository Repository)
{
    /// <summary>Gets the entry as <c>owner/name</c>, as configured.</summary>
    public string FullName => $"{Owner}/{Name}";
}
