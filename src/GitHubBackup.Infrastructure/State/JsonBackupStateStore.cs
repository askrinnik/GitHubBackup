using System.IO.Abstractions;
using System.Text.Json;
using GitHubBackup.Core.State;
using GitHubBackup.Infrastructure.Files;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.State;

/// <summary>
/// Reads and writes <see cref="BackupState.FileName"/> with <see cref="BackupStateJsonContext"/>.
/// </summary>
/// <param name="fileSystem">The file system.</param>
/// <param name="writer">The writer that replaces the state file atomically.</param>
/// <param name="logger">The logger.</param>
internal sealed class JsonBackupStateStore(
    IFileSystem fileSystem,
    AtomicFileWriter writer,
    ILogger<JsonBackupStateStore> logger) : IBackupStateStore
{
    /// <summary>The line break that ends the file, matching the line breaks inside it.</summary>
    private static readonly byte[] _finalNewLine = "\r\n"u8.ToArray();

    /// <inheritdoc />
    public async Task<BackupState> LoadAsync(
        string archiveFolder, long repositoryId, string fullName, CancellationToken cancellationToken)
    {
        var path = Path.Combine(archiveFolder, BackupState.FileName);
        var state = await ReadAsync(path, cancellationToken);
        if (state is null)
        {
            BackupStateLog.NotFound(logger, path);
            return BackupState.CreateEmpty(repositoryId, fullName);
        }

        if (state.RepositoryId != repositoryId)
        {
            throw new BackupStateException(
                path,
                [$"The backup state file '{path}' belongs to another repository (id {state.RepositoryId}, expected {repositoryId}); it is neither read nor changed."]);
        }

        BackupStateLog.Loaded(logger, path, state.Repository.Archives.Count, state.Wiki.Archives.Count);
        return state;
    }

    /// <inheritdoc />
    /// <remarks>
    /// An archive that exists only as a cloud placeholder counts as present; its content is never read.
    /// </remarks>
    public async Task<BackupState> AppendAsync(
        string archiveFolder,
        long repositoryId,
        string fullName,
        BackupTarget target,
        ArchiveRecord record,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(archiveFolder, BackupState.FileName);
        if (!fileSystem.File.Exists(Path.Combine(archiveFolder, record.File)))
        {
            throw new BackupStateException(
                path, [$"The archive '{record.File}' is not in the archive folder '{archiveFolder}', so it is not recorded."]);
        }

        var state = (await LoadAsync(archiveFolder, repositoryId, fullName, cancellationToken))
            .WithArchive(target, record, fullName);

        var content = JsonSerializer.SerializeToUtf8Bytes(state, BackupStateJsonContext.BackupStateTypeInfo);
        await writer.WriteAsync(path, content.Concat(_finalNewLine).ToArray(), cancellationToken);

        BackupStateLog.ArchiveAppended(logger, path, target, record.File);
        return state;
    }

    /// <summary>
    /// Builds the error that describes a JSON problem by its position only.
    /// </summary>
    /// <remarks>
    /// The message of a <see cref="JsonException"/> can quote part of the file, so only the line, the position and
    /// the JSON path are reported.
    /// </remarks>
    /// <param name="path">The path of the state file.</param>
    /// <param name="exception">The parse or conversion failure.</param>
    /// <returns>The error message.</returns>
    private static string DescribeJsonError(string path, JsonException exception) =>
        $"The backup state file '{path}' is not valid at line {exception.LineNumber + 1 ?? 1}, "
        + $"position {exception.BytePositionInLine + 1 ?? 1} (JSON path {exception.Path ?? "$"}): "
        + "check the syntax, the property names, the value types and that required values are present and not null.";

    /// <summary>
    /// Reads and deserializes the state file.
    /// </summary>
    /// <param name="path">The path of the state file.</param>
    /// <param name="cancellationToken">Cancels reading the file.</param>
    /// <returns>The state, or <see langword="null"/> when the file or its folder does not exist.</returns>
    /// <exception cref="BackupStateException">
    /// The file is unreadable, not a valid state document or of an unsupported schema version.
    /// </exception>
    private async Task<BackupState?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        BackupState? state;
        try
        {
            await using var stream = fileSystem.File.OpenRead(path);
            state = await JsonSerializer.DeserializeAsync(stream, BackupStateJsonContext.BackupStateTypeInfo, cancellationToken);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new BackupStateException(path, [$"The backup state file '{path}' cannot be read: {exception.Message}"], exception);
        }
        catch (JsonException exception)
        {
            // The JsonException is not attached as the inner exception: its message can quote part of the file.
            throw new BackupStateException(path, [DescribeJsonError(path, exception)]);
        }
        catch (ArgumentException)
        {
            // An archive record rejects its values in its constructor, and that message quotes them.
            throw new BackupStateException(
                path, [$"The backup state file '{path}' holds an archive with an invalid file name, branch, head commit or size."]);
        }

        if (state is null)
        {
            throw new BackupStateException(path, [$"The backup state file '{path}' must contain a JSON object, not null."]);
        }

        return state.SchemaVersion == BackupState.CurrentSchemaVersion
            ? state
            : throw new BackupStateException(
                path,
                [$"The backup state file '{path}' has schema version {state.SchemaVersion}; only version {BackupState.CurrentSchemaVersion} is supported."]);
    }
}
