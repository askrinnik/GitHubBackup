using System.Buffers;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.GitHub;

namespace GitHubBackup.Core.State;

/// <summary>
/// Describes one archive created for a repository or its wiki.
/// </summary>
public sealed record ArchiveRecord
{
    /// <summary>The printable characters Windows forbids in a file name, on every operating system.</summary>
    private static readonly SearchValues<char> _invalidFileNameCharacters = SearchValues.Create(['\\', '/', ':', '<', '>', '"', '|', '?', '*']);

    /// <summary>
    /// Creates a record of an archive.
    /// </summary>
    /// <param name="file">The file name of the archive in the archive folder, without a folder.</param>
    /// <param name="createdAt">The local time the archive was created, with its offset from UTC.</param>
    /// <param name="branch">The branch checked out in the archived clone.</param>
    /// <param name="headCommit">The full id of the commit checked out in the archived clone.</param>
    /// <param name="sizeBytes">The size of the archive file in bytes.</param>
    /// <param name="refs">The branches and tags of the clone when it was archived.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="file"/> is empty, contains a path separator or another character Windows forbids in a file
    /// name, is a relative folder name, a reserved device name, or ends with a dot or a space;
    /// <paramref name="branch"/> or <paramref name="headCommit"/> is empty; or <paramref name="sizeBytes"/> is negative.
    /// </exception>
    public ArchiveRecord(
        string file, DateTimeOffset createdAt, string branch, string headCommit, long sizeBytes, RefSnapshot refs)
    {
        // The file name is combined with the archive folder to check that the archive exists, so a separator, a
        // relative folder name, a device name or a name Windows would shorten (trailing dot or space) must not pass.
        if (string.IsNullOrWhiteSpace(file)
            || file.AsSpan().ContainsAny(_invalidFileNameCharacters)
            || file.Any(char.IsControl)
            || file is "." or ".."
            || file[^1] is '.' or ' '
            || GitHubUrl.IsWindowsDeviceName(file))
        {
            throw new ArgumentException("The archive file must be a file name without a folder.", nameof(file));
        }

        if (string.IsNullOrWhiteSpace(branch))
        {
            throw new ArgumentException("The branch of an archive must not be empty.", nameof(branch));
        }

        if (string.IsNullOrWhiteSpace(headCommit))
        {
            throw new ArgumentException("The head commit of an archive must not be empty.", nameof(headCommit));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);

        File = file;
        CreatedAt = createdAt;
        Branch = branch;
        HeadCommit = headCommit;
        SizeBytes = sizeBytes;
        Refs = refs;
    }

    /// <summary>Gets the file name of the archive in the archive folder.</summary>
    public string File { get; }

    /// <summary>Gets the local time the archive was created, with its offset from UTC.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Gets the branch checked out in the archived clone.</summary>
    public string Branch { get; }

    /// <summary>Gets the full id of the commit checked out in the archived clone.</summary>
    public string HeadCommit { get; }

    /// <summary>Gets the size of the archive file in bytes.</summary>
    public long SizeBytes { get; }

    /// <summary>Gets the branches and tags of the clone when it was archived.</summary>
    public RefSnapshot Refs { get; }
}
