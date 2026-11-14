using System.IO.Abstractions;
using System.Text.Json;
using GitHubBackup.Core.Clones;
using GitHubBackup.Infrastructure.Files;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.Clones;

/// <summary>
/// Reads and writes <see cref="CloneLocations.FileName"/> with <see cref="CloneLocationsJsonContext"/>.
/// </summary>
/// <param name="fileSystem">The file system.</param>
/// <param name="writer">The writer that replaces the file atomically.</param>
/// <param name="logger">The logger.</param>
internal sealed class JsonCloneLocationStore(
    IFileSystem fileSystem, AtomicFileWriter writer, ILogger<JsonCloneLocationStore> logger) : ICloneLocationStore
{
    /// <summary>The line break that ends the file, matching the line breaks inside it.</summary>
    private static readonly byte[] _finalNewLine = "\r\n"u8.ToArray();

    /// <inheritdoc />
    public async Task<CloneLocations> LoadAsync(string sourcesRoot, CancellationToken cancellationToken)
    {
        var path = fileSystem.Path.Combine(sourcesRoot, CloneLocations.FileName);
        CloneLocationsDocument? document;
        try
        {
            await using var stream = fileSystem.File.OpenRead(path);
            document = await JsonSerializer.DeserializeAsync(
                stream, CloneLocationsJsonContext.Default.CloneLocationsDocument, cancellationToken);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            CloneLocationLog.NotFound(logger, path);
            return new();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            CloneLocationLog.NotRead(logger, path, exception.Message);
            return new();
        }
        catch (JsonException exception)
        {
            // The message of a JsonException can quote part of the file, so only its position is logged.
            CloneLocationLog.NotRead(
                logger, path, $"invalid JSON at line {exception.LineNumber + 1 ?? 1}, position {exception.BytePositionInLine + 1 ?? 1}");
            return new();
        }

        if (document is null || document.SchemaVersion != CloneLocationsDocument.CurrentSchemaVersion)
        {
            CloneLocationLog.NotRead(logger, path, "the file is null or has an unsupported schema version");
            return new();
        }

        var entries = document.Clones.Where(entry => entry.Key > 0 && entry.Value is not null).ToList();
        var valid = entries.Where(entry => IsValid(entry.Value)).ToList();
        if (valid.Count < entries.Count)
        {
            // A relative folder would be resolved against the current folder of the process, so a move could start
            // from or go to an unintended place; such entries count as unknown.
            CloneLocationLog.NotRead(logger, path, $"{entries.Count - valid.Count} entries hold a folder that is not fully qualified and are ignored");
        }

        var locations = new CloneLocations(valid);
        CloneLocationLog.Loaded(logger, path, locations.Entries.Count);
        return locations;
    }

    /// <summary>
    /// Returns whether every folder an entry records is fully qualified.
    /// </summary>
    /// <param name="location">The entry.</param>
    /// <returns><see langword="true"/> when the entry can be used.</returns>
    private bool IsValid(CloneLocation location) =>
        (location.Repository is null || fileSystem.Path.IsPathFullyQualified(location.Repository))
        && (location.Wiki is null || fileSystem.Path.IsPathFullyQualified(location.Wiki));

    /// <inheritdoc />
    public async Task SaveAsync(string sourcesRoot, CloneLocations locations, CancellationToken cancellationToken)
    {
        var path = fileSystem.Path.Combine(sourcesRoot, CloneLocations.FileName);
        var document = new CloneLocationsDocument
        {
            Clones = locations.Entries.OrderBy(entry => entry.Key).ToDictionary(),
        };
        var content = JsonSerializer.SerializeToUtf8Bytes(document, CloneLocationsJsonContext.Default.CloneLocationsDocument);
        fileSystem.Directory.CreateDirectory(sourcesRoot);
        await writer.WriteAsync(path, content.Concat(_finalNewLine).ToArray(), cancellationToken);
        CloneLocationLog.Saved(logger, path, locations.Entries.Count);
    }
}
