namespace GitHubBackup.Core.Clones;

/// <summary>
/// Records where the clones of one repository were when a backup run last left them, and the URLs they were cloned
/// from.
/// </summary>
/// <param name="Repository">The clone folder of the repository, or <see langword="null"/> when it is not known.</param>
/// <param name="Wiki">The clone folder of the wiki, or <see langword="null"/> when it is not known.</param>
/// <param name="RepositoryUrl">
/// The URL the clone of the repository points at, or <see langword="null"/>; after a rename it is the old URL, which a
/// later move of the clone accepts as its origin.
/// </param>
/// <param name="WikiUrl">The URL the clone of the wiki points at, or <see langword="null"/>.</param>
public sealed record CloneLocation(string? Repository, string? Wiki, string? RepositoryUrl = null, string? WikiUrl = null);
