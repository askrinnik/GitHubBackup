using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Win32.SafeHandles;

namespace GitHubBackup.Infrastructure.Tests.BackupConfiguration;

/// <summary>
/// Provides a <see cref="MockFileSystem"/> that fails chosen operations with an <see cref="IOException"/>.
/// </summary>
internal sealed class FaultInjectingFileSystem : MockFileSystem
{
    /// <summary>The file operations that consult the fault switches.</summary>
    private readonly FaultingFile _file;

    /// <summary>The stream factory that consults the fault switches.</summary>
    private readonly FaultingFileStreamFactory _fileStream;

    /// <summary>
    /// Initializes a new, empty file system with no faults enabled.
    /// </summary>
    public FaultInjectingFileSystem()
    {
        _file = new(this);
        _fileStream = new(this);
    }

    /// <summary>Gets or sets a value indicating whether writing to a new stream fails after half of the first buffer.</summary>
    public bool FailStreamWrite { get; set; }

    /// <summary>Gets or sets a value indicating whether <see cref="IFile.Replace(string, string, string?)"/> fails.</summary>
    public bool FailReplace { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="IFile.Replace(string, string, string?)"/> moves the
    /// destination to the backup name and then fails, leaving nothing under the destination name.
    /// </summary>
    public bool FailReplaceAfterBackup { get; set; }

    /// <summary>Gets or sets a value indicating whether <see cref="IFile.Delete(string)"/> fails.</summary>
    public bool FailDelete { get; set; }

    /// <summary>Gets or sets a value indicating whether <see cref="IFile.Move(string, string)"/> fails.</summary>
    public bool FailMove { get; set; }

    /// <inheritdoc />
    public override IFile File => _file;

    /// <inheritdoc />
    public override IFileStreamFactory FileStream => _fileStream;

    /// <summary>
    /// Fails <see cref="Replace"/>, <see cref="Move(string, string)"/> and <see cref="Delete"/> when the matching
    /// switch of the owner is on.
    /// </summary>
    /// <param name="owner">The file system that holds the switches and the files.</param>
    private sealed class FaultingFile(FaultInjectingFileSystem owner) : MockFile(owner)
    {
        /// <inheritdoc />
        public override void Replace(string sourceFileName, string destinationFileName, string? destinationBackupFileName)
        {
            if (owner.FailReplace)
            {
                throw new IOException("Injected failure of File.Replace.");
            }

            if (owner.FailReplaceAfterBackup && destinationBackupFileName is not null)
            {
                base.Move(destinationFileName, destinationBackupFileName, overwrite: true);
                throw new IOException("Injected failure of File.Replace after the backup was made.");
            }

            base.Replace(sourceFileName, destinationFileName, destinationBackupFileName);
        }

        /// <inheritdoc />
        public override void Move(string sourceFileName, string destFileName)
        {
            if (owner.FailMove)
            {
                throw new IOException("Injected failure of File.Move.");
            }

            base.Move(sourceFileName, destFileName);
        }

        /// <inheritdoc />
        public override void Delete(string path)
        {
            if (owner.FailDelete)
            {
                throw new IOException("Injected failure of File.Delete.");
            }

            base.Delete(path);
        }
    }

    /// <summary>
    /// Creates <see cref="FaultingStream"/> instances for the overload used to write files and delegates every other
    /// overload to a <see cref="MockFileStreamFactory"/>.
    /// </summary>
    /// <param name="owner">The file system that holds the switches and the files.</param>
    private sealed class FaultingFileStreamFactory(FaultInjectingFileSystem owner) : IFileStreamFactory
    {
        /// <summary>The factory that serves the overloads without faults.</summary>
        private readonly MockFileStreamFactory _inner = new(owner);

        /// <inheritdoc />
        public IFileSystem FileSystem => owner;

        /// <inheritdoc />
        public FileSystemStream New(string path, FileMode mode, FileAccess access, FileShare share) =>
            new FaultingStream(owner, path, mode, access, share);

        /// <inheritdoc />
        public FileSystemStream New(SafeFileHandle handle, FileAccess access) => _inner.New(handle, access);

        /// <inheritdoc />
        public FileSystemStream New(SafeFileHandle handle, FileAccess access, int bufferSize) =>
            _inner.New(handle, access, bufferSize);

        /// <inheritdoc />
        public FileSystemStream New(SafeFileHandle handle, FileAccess access, int bufferSize, bool isAsync) =>
            _inner.New(handle, access, bufferSize, isAsync);

        /// <inheritdoc />
        public FileSystemStream New(string path, FileMode mode) => _inner.New(path, mode);

        /// <inheritdoc />
        public FileSystemStream New(string path, FileMode mode, FileAccess access) => _inner.New(path, mode, access);

        /// <inheritdoc />
        public FileSystemStream New(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize) =>
            _inner.New(path, mode, access, share, bufferSize);

        /// <inheritdoc />
        public FileSystemStream New(
            string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, bool useAsync) =>
            _inner.New(path, mode, access, share, bufferSize, useAsync);

        /// <inheritdoc />
        public FileSystemStream New(
            string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options) =>
            _inner.New(path, mode, access, share, bufferSize, options);

        /// <inheritdoc />
        public FileSystemStream New(string path, FileStreamOptions options) => _inner.New(path, options);

        /// <inheritdoc />
        public FileSystemStream Wrap(FileStream fileStream) => _inner.Wrap(fileStream);
    }

    /// <summary>
    /// Writes half of the first buffer and then fails, when the switch of the owner is on.
    /// </summary>
    /// <param name="owner">The file system that holds the switch and the files.</param>
    /// <param name="path">The path of the file.</param>
    /// <param name="mode">The open mode.</param>
    /// <param name="access">The access requested.</param>
    /// <param name="share">The sharing allowed.</param>
    private sealed class FaultingStream(
        FaultInjectingFileSystem owner, string path, FileMode mode, FileAccess access, FileShare share)
        : MockFileStream(owner, path, mode, access, share, FileOptions.None)
    {
        /// <inheritdoc />
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (owner.FailStreamWrite)
            {
                await base.WriteAsync(buffer[..(buffer.Length / 2)], cancellationToken);
                Flush();
                throw new IOException("Injected failure of the write: the disk is full.");
            }

            await base.WriteAsync(buffer, cancellationToken);
        }
    }
}
