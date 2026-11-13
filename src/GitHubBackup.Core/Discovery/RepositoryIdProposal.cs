namespace GitHubBackup.Core.Discovery;

/// <summary>
/// What an <see cref="IDiscoveryDecisionHandler"/> shows the user about an ID found for an entry without one.
/// </summary>
/// <param name="FullName">The entry as <c>owner/name</c>, as configured.</param>
/// <param name="Id">The GitHub ID found by the name.</param>
public sealed record RepositoryIdProposal(string FullName, long Id);
