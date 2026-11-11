using GitHubBackup.Core.State;

namespace GitHubBackup.Core.Archiving;

/// <summary>
/// Builds the file name of a new archive: <c>&lt;owner&gt;_&lt;repo&gt;[.wiki]_&lt;yyyyMMdd-HHmmss&gt;_&lt;shortHash&gt;.zip</c>.
/// </summary>
public interface IArchiveNameBuilder
{
    /// <summary>
    /// Builds the name of an archive created now.
    /// </summary>
    /// <param name="owner">The login of the repository owner.</param>
    /// <param name="repository">The name of the repository.</param>
    /// <param name="target">Whether the archive holds the repository or its wiki.</param>
    /// <param name="headCommit">The id of the archived commit; its first <c>Backup:ShortHashLength</c> characters enter the name.</param>
    /// <returns>The file name and the local time written into it.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="owner"/> or <paramref name="repository"/> is not valid on GitHub, or
    /// <paramref name="headCommit"/> is not a hexadecimal id with at least <c>Backup:ShortHashLength</c> characters.
    /// </exception>
    ArchiveName Build(string owner, string repository, BackupTarget target, string headCommit);
}
