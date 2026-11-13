namespace GitHubBackup.Core.Discovery;

/// <summary>
/// The outcome of looking up the configuration entries without an ID by name.
/// </summary>
/// <param name="Resolved">The entries whose ID was found and belongs to no other entry.</param>
/// <param name="NotFound">The <c>owner/name</c> of each entry GitHub does not know or does not show.</param>
/// <param name="Conflicts">The entries whose ID already belongs to another entry.</param>
public sealed record RepositoryIdResolution(
    IReadOnlyList<ResolvedRepositoryId> Resolved,
    IReadOnlyList<string> NotFound,
    IReadOnlyList<RepositoryIdConflict> Conflicts)
{
    /// <summary>Gets the outcome of a run without entries to look up.</summary>
    public static RepositoryIdResolution Empty { get; } = new([], [], []);
}
