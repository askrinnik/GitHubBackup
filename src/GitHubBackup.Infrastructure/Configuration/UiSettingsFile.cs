using System.IO.Abstractions;
using System.Text.Json;
using GitHubBackup.Infrastructure.Files;
using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.Hosting;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Reads and rewrites <see cref="GitHubBackupHostBuilder.UiSettingsFileName"/> in the content root as a whole.
/// </summary>
/// <remarks>
/// The file stays out of the host configuration: only the WPF application reads it, and its content never fails the
/// start of either host. A file that exists but cannot be read, or whose text is not a JSON object, reads as no file
/// but is never written over, so its previous version in the backup of <see cref="AtomicFileWriter"/> is not lost.
/// </remarks>
/// <param name="fileSystem">The file system.</param>
/// <param name="environment">The host environment whose content root holds the file.</param>
/// <param name="writer">The writer that replaces the file atomically and keeps its previous version.</param>
internal sealed class UiSettingsFile(IFileSystem fileSystem, IHostEnvironment environment, AtomicFileWriter writer)
    : IDisposable
{
    /// <summary>The key of the theme in the root object.</summary>
    public const string ThemeKey = "Theme";

    /// <summary>The key of the object that holds the main window placement and panel heights.</summary>
    public const string MainWindowKey = "MainWindow";

    /// <summary>Lets one read or write of the file run at a time.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Gets the absolute path of the file.</summary>
    public string FilePath => Path.Combine(environment.ContentRootPath, GitHubBackupHostBuilder.UiSettingsFileName);

    /// <summary>
    /// Reads the root object of the file.
    /// </summary>
    /// <param name="cancellationToken">Cancels waiting for another read or write.</param>
    /// <returns>
    /// A copy of the root object, or <see langword="null"/> when the file does not exist, cannot be read or is not a
    /// JSON object.
    /// </returns>
    public async Task<JsonElement?> ReadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var document = await JsonSettingsText.ReadObjectAsync(fileSystem, FilePath, cancellationToken);
            return document?.RootElement.Clone();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Replaces the content of the file.
    /// </summary>
    /// <param name="writeRoot">Writes the root object.</param>
    /// <param name="cancellationToken">Cancels the write before the file is replaced.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="IOException">
    /// The file cannot be read, is not a JSON object or cannot be written; it is left as it is.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be read or written; it is left as it is.</exception>
    public async Task WriteAsync(Action<Utf8JsonWriter> writeRoot, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Reading first refuses to replace a file the user may still want to repair.
            var existing = await JsonSettingsText.ReadObjectAsync(fileSystem, FilePath, cancellationToken);
            existing?.Dispose();

            await writer.WriteAsync(FilePath, JsonSettingsText.Write(writeRoot), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();
}
