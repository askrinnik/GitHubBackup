using System.IO.Abstractions;

namespace GitHubBackup.Infrastructure.Files;

/// <summary>
/// Replaces the content of a file the application owns so that a crash at any point leaves either the old or the new
/// file under the final name, never a partial one, and keeps the previous version next to it.
/// </summary>
/// <param name="fileSystem">The file system.</param>
internal sealed class AtomicFileWriter(IFileSystem fileSystem)
{
    /// <summary>The extension appended to the path of a file for the copy of its previous version.</summary>
    public const string BackupExtension = ".bak";

    /// <summary>The extension of the temporary file written before it replaces the file.</summary>
    public const string TemporaryExtension = ".tmp";

    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="path"/>, keeping the previous file as
    /// <paramref name="path"/> plus <see cref="BackupExtension"/>.
    /// </summary>
    /// <remarks>
    /// The content goes to a uniquely named temporary file in the folder of <paramref name="path"/> and is flushed to
    /// disk; only then does it replace the file, or take its name when there is no file yet. On failure or
    /// cancellation the temporary file is removed, unless a failed replace left it as the only copy of the new content.
    /// </remarks>
    /// <param name="path">The path of the file.</param>
    /// <param name="content">The complete new content.</param>
    /// <param name="cancellationToken">Cancels the write before the file is replaced.</param>
    /// <returns>A task that completes when the file holds the new content.</returns>
    public async Task WriteAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}{TemporaryExtension}";
        var backupPath = path + BackupExtension;
        var replacing = false;
        try
        {
            await using (var stream = fileSystem.FileStream.New(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(content, cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (fileSystem.File.Exists(path))
            {
                replacing = true;
                fileSystem.File.Replace(temporaryPath, path, backupPath);
            }
            else
            {
                fileSystem.File.Move(temporaryPath, path);
            }
        }
        catch
        {
            if (replacing)
            {
                RestoreFinalFile(path, temporaryPath, backupPath);
            }

            // After a failed replace the complete temporary file may be the only copy of the new content, so it is
            // kept unless the final name holds a file again.
            if (!replacing || fileSystem.File.Exists(path))
            {
                TryDelete(temporaryPath);
            }

            throw;
        }
    }

    /// <summary>
    /// Puts a file back under the final name when a failed replace has already moved the original to the backup name.
    /// </summary>
    /// <remarks>
    /// <see cref="IFile.Replace(string, string, string?)"/> can fail after renaming the original to the backup name
    /// and before moving the replacement in, which leaves no file under the final name. The complete temporary file
    /// is moved in first; if that fails too, the original is copied back from the backup. Failures of these attempts
    /// are ignored: the exception of the replace is the one the caller needs.
    /// </remarks>
    /// <param name="path">The path of the file.</param>
    /// <param name="temporaryPath">The path of the fully written temporary file.</param>
    /// <param name="backupPath">The path of the copy of the previous file.</param>
    private void RestoreFinalFile(string path, string temporaryPath, string backupPath)
    {
        if (fileSystem.File.Exists(path) || !fileSystem.File.Exists(backupPath))
        {
            return;
        }

        try
        {
            if (fileSystem.File.Exists(temporaryPath))
            {
                fileSystem.File.Move(temporaryPath, path);
                return;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Falls through to restoring the previous file.
        }

        try
        {
            fileSystem.File.Copy(backupPath, path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done; the previous content stays in the backup file.
        }
    }

    /// <summary>
    /// Deletes a temporary file left by a failed write, ignoring a failure to delete it.
    /// </summary>
    /// <remarks>
    /// The write has already failed and its exception is the one the caller needs; a leftover temporary file is
    /// harmless because the next write uses a new name.
    /// </remarks>
    /// <param name="temporaryPath">The path of the temporary file.</param>
    private void TryDelete(string temporaryPath)
    {
        try
        {
            fileSystem.File.Delete(temporaryPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort only: the exception of the failed write is rethrown by the caller.
        }
    }
}
