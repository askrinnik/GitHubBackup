using GitHubBackup.Core.GitHub;

namespace GitHubBackup.Core.Discovery;

/// <summary>
/// A configured repository that GitHub now reports under a different <c>owner/name</c>.
/// </summary>
/// <param name="Tracked">The configuration entry.</param>
/// <param name="Current">The repository under its current owner and name.</param>
public sealed record RenamedRepository(TrackedRepository Tracked, GitHubRepository Current);
