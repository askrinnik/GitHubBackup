namespace GitHubBackup.Core.Discovery;

/// <summary>
/// Everything discovery found that a handler can show the user at once, before the questions about single
/// repositories.
/// </summary>
/// <param name="New">The proposals for new repositories: those of tracked accounts first, then the standalone ones.</param>
/// <param name="Renamed">The proposals for renamed repositories, in file order.</param>
/// <param name="Missing">The active entries that GitHub does not report; they become unavailable without a question.</param>
/// <param name="Returned">The unavailable entries that GitHub reports again; they become active without a question.</param>
public sealed record DiscoveryReview(
    IReadOnlyList<NewRepositoryProposal> New,
    IReadOnlyList<RenameProposal> Renamed,
    IReadOnlyList<TrackedRepository> Missing,
    IReadOnlyList<ReturnedRepository> Returned)
{
    /// <summary>Gets whether the review holds at least one repository.</summary>
    public bool HasItems => New.Count > 0 || Renamed.Count > 0 || Missing.Count > 0 || Returned.Count > 0;
}
