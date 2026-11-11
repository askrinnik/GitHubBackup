namespace GitHubBackup.Core.Discovery;

/// <summary>
/// What an <see cref="IDiscoveryDecisionHandler"/> shows the user about one renamed repository.
/// </summary>
/// <remarks>
/// The two folders are equal when the entry sets its own <c>path</c>; the decision then has no effect on the archives.
/// </remarks>
/// <param name="Renamed">The configuration entry and the repository under its current name.</param>
/// <param name="CurrentArchiveFolder">The absolute archive folder the entry resolves to now.</param>
/// <param name="RenamedArchiveFolder">The absolute archive folder after <see cref="RenameDecision.RenameArchiveFolder"/>.</param>
public sealed record RenameProposal(RenamedRepository Renamed, string CurrentArchiveFolder, string RenamedArchiveFolder);
