using System.IO.Abstractions;

namespace GitHubBackup.TestUtilities;

/// <summary>
/// Wraps the real file system and makes <see cref="IDirectory.Move"/> throw <see cref="IOException"/>, so a test can
/// exercise the code that handles a folder that cannot be renamed; every other operation reaches the disk.
/// </summary>
public sealed class MoveFailingFileSystem : IFileSystem
{
    private readonly FileSystem _inner = new();

    /// <summary>
    /// Creates the wrapper.
    /// </summary>
    public MoveFailingFileSystem() => Directory = new MoveFailingDirectory(this);

    /// <inheritdoc />
    public IDirectory Directory { get; }

    /// <inheritdoc />
    public IDirectoryInfoFactory DirectoryInfo => _inner.DirectoryInfo;

    /// <inheritdoc />
    public IDriveInfoFactory DriveInfo => _inner.DriveInfo;

    /// <inheritdoc />
    public IFile File => _inner.File;

    /// <inheritdoc />
    public IFileInfoFactory FileInfo => _inner.FileInfo;

    /// <inheritdoc />
    public IFileStreamFactory FileStream => _inner.FileStream;

    /// <inheritdoc />
    public IFileSystemWatcherFactory FileSystemWatcher => _inner.FileSystemWatcher;

    /// <inheritdoc />
    public IFileVersionInfoFactory FileVersionInfo => _inner.FileVersionInfo;

    /// <inheritdoc />
    public IPath Path => _inner.Path;

    /// <inheritdoc />
    public IRandomAccess RandomAccess => _inner.RandomAccess;

    /// <summary>
    /// The real directory API except for <see cref="Move"/>.
    /// </summary>
    /// <param name="fileSystem">The owning file system.</param>
    private sealed class MoveFailingDirectory(IFileSystem fileSystem) : DirectoryWrapper(fileSystem)
    {
        /// <inheritdoc />
        public override void Move(string sourceDirName, string destDirName) =>
            throw new IOException("The folder is in use.");
    }
}
