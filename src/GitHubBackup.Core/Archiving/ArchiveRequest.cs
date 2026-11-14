namespace GitHubBackup.Core.Archiving;

/// <summary>
/// Describes one archive that <see cref="IArchiver"/> creates.
/// </summary>
public sealed record ArchiveRequest
{
    /// <summary>Gets the fully qualified path of <c>7z.exe</c>.</summary>
    public required string SevenZipPath { get; init; }

    /// <summary>
    /// Gets the fully qualified path of the clone folder; the archive holds this folder, with its <c>.git</c> folder,
    /// as its only root entry.
    /// </summary>
    public required string SourceFolder { get; init; }

    /// <summary>Gets the fully qualified path of the folder in which the archive is built.</summary>
    public required string TemporaryFolder { get; init; }

    /// <summary>Gets the fully qualified path of the folder that receives the finished archive.</summary>
    public required string DestinationFolder { get; init; }

    /// <summary>Gets the name of the archive file in <see cref="DestinationFolder"/>, without a folder.</summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Gets the files and folders left out of the archive, relative to <see cref="SourceFolder"/> with <c>\</c> as the
    /// separator; a folder is left out with its whole content. Empty archives the whole folder.
    /// </summary>
    public IReadOnlyList<string> ExcludedPaths { get; init; } = [];
}
