namespace GitHubBackup.Core.History;

/// <summary>
/// A repository that discovery found new, missing, renamed or returned in a run, with the decision taken on it.
/// </summary>
/// <param name="Repository">The repository as <c>owner/name</c>; for a rename, the new name.</param>
/// <param name="Kind">What discovery found.</param>
public sealed record DiscoveryHistoryEvent(string Repository, DiscoveryEventKind Kind)
{
    /// <summary>Gets the name before a rename as <c>owner/name</c>, or <see langword="null"/> for other events.</summary>
    public string? PreviousFullName { get; init; }

    /// <summary>
    /// Gets the decision taken on the event, or <see langword="null"/> for <see cref="DiscoveryEventKind.Missing"/> and
    /// <see cref="DiscoveryEventKind.Returned"/>, which change the status without asking.
    /// </summary>
    public DiscoveryEventDecision? Decision { get; init; }

    /// <summary>Gets who took <see cref="Decision"/>, or <see langword="null"/> when there is none.</summary>
    public DecisionMaker? DecidedBy { get; init; }
}
