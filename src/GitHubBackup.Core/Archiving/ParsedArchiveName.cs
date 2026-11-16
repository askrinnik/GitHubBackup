using GitHubBackup.Core.State;

namespace GitHubBackup.Core.Archiving;

/// <summary>
/// The values an archive file name carries, read back by <see cref="IArchiveNameBuilder.TryParse"/>.
/// </summary>
/// <param name="Target">Whether the name belongs to an archive of the repository or of its wiki.</param>
/// <param name="CreatedAt">
/// The local time written into the name, with the offset of the local time zone at that time.
/// </param>
/// <param name="ShortHash">The hash written into the name, in lower case.</param>
public sealed record ParsedArchiveName(BackupTarget Target, DateTimeOffset CreatedAt, string ShortHash);
