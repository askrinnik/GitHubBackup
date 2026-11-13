namespace GitHubBackup.Core.Discovery;

/// <summary>
/// A configuration entry without an ID whose ID, found on GitHub, already belongs to another entry.
/// </summary>
/// <param name="FullName">The entry as <c>owner/name</c>, as configured.</param>
/// <param name="Reason">The sentence that names the other entry, shown as the error of the item.</param>
public sealed record RepositoryIdConflict(string FullName, string Reason);
