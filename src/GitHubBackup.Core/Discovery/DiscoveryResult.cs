using GitHubBackup.Core.GitHub;

namespace GitHubBackup.Core.Discovery;

/// <summary>
/// The classes into which discovery sorts the repositories it compared with the configuration.
/// </summary>
/// <param name="NewInAccounts">Repositories of a tracked account that the configuration does not list, ordered by <c>owner/name</c>.</param>
/// <param name="NewStandalone">Repositories reachable with the token but outside every tracked account and not listed, ordered by <c>owner/name</c>.</param>
/// <param name="Missing">Active configuration entries that GitHub does not report, in file order.</param>
/// <param name="Returned">Unavailable configuration entries that GitHub reports again, in file order.</param>
/// <param name="Renamed">Configuration entries that GitHub reports under another <c>owner/name</c>, in file order.</param>
public sealed record DiscoveryResult(
    IReadOnlyList<NewAccountRepository> NewInAccounts,
    IReadOnlyList<GitHubRepository> NewStandalone,
    IReadOnlyList<TrackedRepository> Missing,
    IReadOnlyList<ReturnedRepository> Returned,
    IReadOnlyList<RenamedRepository> Renamed)
{
    /// <summary>Gets a result without any finding.</summary>
    public static DiscoveryResult Empty { get; } = new([], [], [], [], []);

    /// <summary>
    /// Gets the repositories GitHub reported during discovery, by id: those of the collected lists and those found by
    /// a lookup of a configured entry.
    /// </summary>
    /// <remarks>
    /// The run reads repository metadata, such as whether the wiki feature is enabled, from here instead of asking the
    /// API again. The repositories are not classes of the result and do not count for <see cref="HasChanges"/>.
    /// </remarks>
    public IReadOnlyDictionary<long, GitHubRepository> Repositories { get; init; } =
        new Dictionary<long, GitHubRepository>();

    /// <summary>Gets whether at least one class holds a repository.</summary>
    public bool HasChanges =>
        NewInAccounts.Count > 0
        || NewStandalone.Count > 0
        || Missing.Count > 0
        || Returned.Count > 0
        || Renamed.Count > 0;
}
