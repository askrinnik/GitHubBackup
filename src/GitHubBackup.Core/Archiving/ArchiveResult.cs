namespace GitHubBackup.Core.Archiving;

/// <summary>
/// The outcome of <see cref="IArchiver.CreateAsync"/>.
/// </summary>
/// <param name="Path">The fully qualified path of the finished archive.</param>
/// <param name="SizeBytes">The size of the archive in bytes.</param>
public sealed record ArchiveResult(string Path, long SizeBytes);
