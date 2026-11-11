using System.IO.Abstractions;
using GitHubBackup.Core.Archiving;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.Processes;
using GitHubBackup.Infrastructure.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.Archiving;

/// <summary>
/// Implements <see cref="IArchiver"/> by running 7-Zip through <see cref="IProcessRunner"/>.
/// </summary>
/// <remarks>
/// The archive is built in the temporary folder under a unique name. Within one volume it is renamed into the
/// archive folder; across volumes it is copied to a unique name next to its final name and renamed there, so the
/// final name never refers to a partial file.
/// </remarks>
/// <param name="processRunner">Runs 7-Zip.</param>
/// <param name="fileSystem">Creates the folders, moves and deletes files and reads the size.</param>
/// <param name="options">Tells whether the archive is tested.</param>
/// <param name="logger">The logger.</param>
internal sealed class SevenZipArchiver(
    IProcessRunner processRunner, IFileSystem fileSystem, IOptions<BackupOptions> options, ILogger<SevenZipArchiver> logger)
    : IArchiver
{
    /// <summary>The number of standard error lines quoted in the message of an <see cref="ArchiveCommandException"/>.</summary>
    private const int _quotedErrorLines = 5;

    /// <summary>The extension of the file that holds a copy before it takes its final name.</summary>
    private const string _copyExtension = ".tmp";

    /// <inheritdoc />
    public async Task<ArchiveResult> CreateAsync(ArchiveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);
        cancellationToken.ThrowIfCancellationRequested();

        var finalPath = fileSystem.Path.Combine(request.DestinationFolder, request.FileName);
        if (fileSystem.File.Exists(finalPath))
        {
            throw new IOException($"The archive '{finalPath}' already exists and is not overwritten.");
        }

        var source = fileSystem.Path.TrimEndingDirectorySeparator(request.SourceFolder);
        var sourceName = fileSystem.Path.GetFileName(source);
        var parent = fileSystem.Path.GetDirectoryName(source)!;
        var stem = fileSystem.Path.GetFileNameWithoutExtension(request.FileName);
        var temporaryPath = fileSystem.Path.Combine(request.TemporaryFolder, $"{stem}.{Guid.NewGuid():N}.zip");
        var copyPath = $"{finalPath}.{Guid.NewGuid():N}{_copyExtension}";

        fileSystem.Directory.CreateDirectory(request.TemporaryFolder);
        fileSystem.Directory.CreateDirectory(request.DestinationFolder);
        try
        {
            SevenZipArchiverLog.Creating(logger, request.SourceFolder, temporaryPath);
            await RunAsync(
                request, parent, ["a", "-tzip", "-mcu=on", "-bsp0", "-sccUTF-8", "-y", "--", temporaryPath, sourceName],
                cancellationToken);

            if (options.Value.VerifyArchive)
            {
                SevenZipArchiverLog.Verifying(logger, temporaryPath);
                await RunAsync(request, parent, ["t", "-bsp0", "-sccUTF-8", "--", temporaryPath], cancellationToken);
            }
            else
            {
                SevenZipArchiverLog.VerificationSkipped(logger, temporaryPath);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Move(temporaryPath, copyPath, finalPath);
            return new ArchiveResult(finalPath, fileSystem.FileInfo.New(finalPath).Length);
        }
        catch
        {
            TryDelete(temporaryPath);
            TryDelete(copyPath);
            throw;
        }
    }

    /// <summary>
    /// Rejects a request whose paths are not fully qualified, whose file name carries a folder, or whose source
    /// folder has no parent or does not exist.
    /// </summary>
    /// <param name="request">The request to check.</param>
    /// <exception cref="ArgumentException">The request is invalid.</exception>
    private void Validate(ArchiveRequest request)
    {
        foreach (var (name, path) in new[]
        {
            (nameof(request.SevenZipPath), request.SevenZipPath),
            (nameof(request.SourceFolder), request.SourceFolder),
            (nameof(request.TemporaryFolder), request.TemporaryFolder),
            (nameof(request.DestinationFolder), request.DestinationFolder),
        })
        {
            if (!fileSystem.Path.IsPathFullyQualified(path))
            {
                throw new ArgumentException($"{name} '{path}' must be fully qualified.", nameof(request));
            }
        }

        if (!ArchiveFileName.IsValid(request.FileName))
        {
            throw new ArgumentException($"FileName '{request.FileName}' must be a file name without a folder.", nameof(request));
        }

        if (fileSystem.Path.GetDirectoryName(fileSystem.Path.TrimEndingDirectorySeparator(request.SourceFolder)) is null)
        {
            throw new ArgumentException($"SourceFolder '{request.SourceFolder}' has no parent folder.", nameof(request));
        }

        if (!fileSystem.Directory.Exists(request.SourceFolder))
        {
            throw new ArgumentException($"SourceFolder '{request.SourceFolder}' does not exist.", nameof(request));
        }
    }

    /// <summary>
    /// Runs 7-Zip and throws when it exits with a non-zero code.
    /// </summary>
    /// <param name="request">The archive request that names the executable.</param>
    /// <param name="workingDirectory">The folder 7-Zip runs in.</param>
    /// <param name="arguments">The 7-Zip arguments.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>A task that completes when 7-Zip exits with code 0.</returns>
    /// <exception cref="ArchiveCommandException">7-Zip exits with a non-zero code.</exception>
    private async Task RunAsync(
        ArchiveRequest request, string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            new ProcessRequest { FileName = request.SevenZipPath, Arguments = arguments, WorkingDirectory = workingDirectory },
            cancellationToken);
        if (result.Succeeded)
        {
            return;
        }

        var command = ProcessCommandLine.Format("7z", arguments);
        var error = string.Join(
            Environment.NewLine, result.StandardError.Where(line => line.Length > 0).TakeLast(_quotedErrorLines));
        throw new ArchiveCommandException(
            $"7-Zip failed with exit code {result.ExitCode}: {command}{(error.Length > 0 ? Environment.NewLine + error : string.Empty)}",
            command,
            result.ExitCode);
    }

    /// <summary>
    /// Moves the archive to its final name without ever overwriting an existing file.
    /// </summary>
    /// <param name="temporaryPath">The finished archive in the temporary folder.</param>
    /// <param name="copyPath">The unique name next to the final name used when the volumes differ.</param>
    /// <param name="finalPath">The final path of the archive.</param>
    private void Move(string temporaryPath, string copyPath, string finalPath)
    {
        var sameVolume = string.Equals(
            fileSystem.Path.GetPathRoot(temporaryPath), fileSystem.Path.GetPathRoot(finalPath), StringComparison.OrdinalIgnoreCase);
        SevenZipArchiverLog.Moving(logger, temporaryPath, finalPath, sameVolume);

        if (sameVolume)
        {
            fileSystem.File.Move(temporaryPath, finalPath);
            return;
        }

        fileSystem.File.Copy(temporaryPath, copyPath);
        fileSystem.File.Move(copyPath, finalPath);
        try
        {
            fileSystem.File.Delete(temporaryPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SevenZipArchiverLog.SourceNotDeleted(logger, temporaryPath, exception.Message);
        }
    }

    /// <summary>
    /// Deletes a temporary file left by a failed or cancelled run, ignoring a failure to delete it.
    /// </summary>
    /// <remarks>The exception of the failed step is the one the caller needs.</remarks>
    /// <param name="path">The path of the file; a missing file is not an error.</param>
    private void TryDelete(string path)
    {
        try
        {
            fileSystem.File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SevenZipArchiverLog.CleanupFailed(logger, path, exception.Message);
        }
    }
}
