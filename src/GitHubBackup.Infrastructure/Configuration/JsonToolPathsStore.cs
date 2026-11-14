using System.Text.Encodings.Web;
using System.Text.Json;
using GitHubBackup.Core.Tools;
using GitHubBackup.Infrastructure.Files;
using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Reads the tool paths from <see cref="ToolsOptions"/> and writes them to
/// <see cref="GitHubBackupHostBuilder.UserSettingsFileName"/> in the content root, which the host reads at start-up
/// right after <see cref="GitHubBackupHostBuilder.AppSettingsFileName"/>.
/// </summary>
/// <remarks>
/// The file holds only the <c>Tools</c> section and is replaced as a whole; a path that is not set is written as
/// <c>null</c>, which the configuration reads as not set, so it also clears a path set in
/// <see cref="GitHubBackupHostBuilder.AppSettingsFileName"/>.
/// </remarks>
/// <param name="options">The tool paths of the running application.</param>
/// <param name="environment">The host environment whose content root holds the file.</param>
/// <param name="writer">The writer that replaces the file atomically.</param>
internal sealed class JsonToolPathsStore(
    IOptions<ToolsOptions> options,
    IHostEnvironment environment,
    AtomicFileWriter writer) : IToolPathsStore
{
    /// <summary>
    /// The options of the JSON text: indented with the line breaks of the other settings files, and with non-ASCII
    /// characters of a path kept readable.
    /// </summary>
    private static readonly JsonWriterOptions _writerOptions = new()
    {
        Indented = true,
        NewLine = "\r\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The line break that ends the file.</summary>
    private static readonly byte[] _finalNewLine = "\r\n"u8.ToArray();

    /// <inheritdoc />
    public ToolPaths Current => new(options.Value.GitPath, options.Value.SevenZipPath);

    /// <summary>Gets the absolute path of the file.</summary>
    private string FilePath => Path.Combine(environment.ContentRootPath, GitHubBackupHostBuilder.UserSettingsFileName);

    /// <inheritdoc />
    public IReadOnlyList<string> Validate(ToolPaths paths)
    {
        var normalized = Normalize(paths);
        return ToolsOptionsValidator.Validate(normalized.GitPath, normalized.SevenZipPath);
    }

    /// <inheritdoc />
    public async Task SaveAsync(ToolPaths paths, CancellationToken cancellationToken)
    {
        var errors = Validate(paths);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(' ', errors), nameof(paths));
        }

        var normalized = Normalize(paths);

        using var buffer = new MemoryStream();
        await using (var json = new Utf8JsonWriter(buffer, _writerOptions))
        {
            json.WriteStartObject();
            json.WriteStartObject(ToolsOptions.SectionName);
            WritePath(json, nameof(ToolsOptions.GitPath), normalized.GitPath);
            WritePath(json, nameof(ToolsOptions.SevenZipPath), normalized.SevenZipPath);
            json.WriteEndObject();
            json.WriteEndObject();
        }

        buffer.Write(_finalNewLine);
        await writer.WriteAsync(FilePath, buffer.ToArray(), cancellationToken);
    }

    /// <summary>
    /// Returns <paramref name="paths"/> without surrounding white space, a blank path being not set.
    /// </summary>
    /// <param name="paths">The paths as entered.</param>
    /// <returns>The normalized paths.</returns>
    private static ToolPaths Normalize(ToolPaths paths) => new(Trim(paths.GitPath), Trim(paths.SevenZipPath));

    /// <summary>
    /// Returns <paramref name="path"/> without surrounding white space, or <see langword="null"/> when it is blank.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The trimmed path, or <see langword="null"/>.</returns>
    private static string? Trim(string? path) => string.IsNullOrWhiteSpace(path) ? null : path.Trim();

    /// <summary>
    /// Writes one path, or <c>null</c> when it is not set.
    /// </summary>
    /// <param name="json">The writer.</param>
    /// <param name="name">The property name.</param>
    /// <param name="path">The normalized path, or <see langword="null"/> when it is not set.</param>
    private static void WritePath(Utf8JsonWriter json, string name, string? path)
    {
        if (path is null)
        {
            json.WriteNull(name);
        }
        else
        {
            json.WriteString(name, path);
        }
    }
}
