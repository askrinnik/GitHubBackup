using GitHubBackup.Core.GitHub;

namespace GitHubBackup.Core.Discovery;

/// <summary>
/// A configured repository marked unavailable that GitHub reports again.
/// </summary>
/// <param name="Tracked">The configuration entry.</param>
/// <param name="Current">The repository as GitHub reports it.</param>
public sealed record ReturnedRepository(TrackedRepository Tracked, GitHubRepository Current);
