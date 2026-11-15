using System.IO.Abstractions;
using System.Text.Encodings.Web;
using System.Text.Json;
using GitHubBackup.Core.Ui;
using GitHubBackup.Infrastructure.Files;
using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.Hosting;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Reads and rewrites the sections of <see cref="GitHubBackupHostBuilder.UserSettingsFileName"/> in the content root,
/// each writer replacing only its own section.
/// </summary>
/// <remarks>
/// <para>
/// The file keeps only the sections in <see cref="KnownSections"/> and, in a section that is not rewritten, only the
/// keys the application reads, copied as they are: a missing key does not turn into <c>null</c>, which the
/// configuration would read as a value that clears the setting of <see cref="GitHubBackupHostBuilder.AppSettingsFileName"/>,
/// and a key added by hand does not survive a write.
/// </para>
/// <para>
/// One read-change-write runs at a time, so writers of different sections do not lose each other's change. A file
/// that exists but cannot be read, or holds text that is not a JSON object, reads as empty but is never written over:
/// the write fails, so neither the other section nor the backup of <see cref="AtomicFileWriter"/> is lost. An empty
/// file counts as no file.
/// </para>
/// </remarks>
/// <param name="fileSystem">The file system.</param>
/// <param name="environment">The host environment whose content root holds the file.</param>
/// <param name="writer">The writer that replaces the file atomically.</param>
internal sealed class UserSettingsFile(IFileSystem fileSystem, IHostEnvironment environment, AtomicFileWriter writer)
    : IDisposable
{
    /// <summary>The name of the section of the user interface preferences.</summary>
    public const string UiSectionName = "Ui";

    /// <summary>The key of the theme in the <c>Ui</c> section.</summary>
    public const string UiThemeKey = "Theme";

    /// <summary>The key of the object in the <c>Ui</c> section that holds the main window placement and panel heights.</summary>
    public const string UiMainWindowKey = "MainWindow";

    /// <summary>The sections the file keeps with the keys the application reads, in the order they are written.</summary>
    private static readonly IReadOnlyList<KnownKey> _knownSections =
    [
        new(ToolsOptions.SectionName, [new(nameof(ToolsOptions.GitPath)), new(nameof(ToolsOptions.SevenZipPath))]),
        new(UiSectionName,
        [
            new(UiThemeKey),
            new(UiMainWindowKey,
            [
                new(nameof(WindowPlacement.Left)),
                new(nameof(WindowPlacement.Top)),
                new(nameof(WindowPlacement.Width)),
                new(nameof(WindowPlacement.Height)),
                new(nameof(WindowPlacement.IsMaximized)),
                new(nameof(UiSettings.LogPanelHeight)),
                new(nameof(UiSettings.SummaryPanelHeight)),
            ]),
        ]),
    ];

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

    /// <summary>The options of reading, as lenient as the JSON configuration provider that reads the file at start-up.</summary>
    private static readonly JsonDocumentOptions _readerOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The line break that ends the file.</summary>
    private static readonly byte[] _finalNewLine = "\r\n"u8.ToArray();

    /// <summary>The UTF-8 byte order mark.</summary>
    private static readonly byte[] _byteOrderMark = [0xEF, 0xBB, 0xBF];

    /// <summary>Lets one read or read-change-write of the file run at a time.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Gets the names of the sections the file keeps, in the order they are written.</summary>
    public static IReadOnlyList<string> KnownSections { get; } = [.. _knownSections.Select(section => section.Name)];

    /// <summary>Gets the absolute path of the file.</summary>
    public string FilePath => Path.Combine(environment.ContentRootPath, GitHubBackupHostBuilder.UserSettingsFileName);

    /// <summary>
    /// Reads one section of the file.
    /// </summary>
    /// <param name="sectionName">The name of the section, compared without regard to case.</param>
    /// <param name="cancellationToken">Cancels waiting for another read or write.</param>
    /// <returns>
    /// A copy of the value of the section, or <see langword="null"/> when the file does not exist, cannot be read, is
    /// not a JSON object or has no such section.
    /// </returns>
    public async Task<JsonElement?> ReadSectionAsync(string sectionName, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var sections = await ReadSectionsAsync(cancellationToken);
            return sections.TryGetValue(sectionName, out var section) ? section : null;
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
    /// Replaces one section of the file and keeps the known keys of the other known sections as they are.
    /// </summary>
    /// <param name="sectionName">The name of the section; one of <see cref="KnownSections"/>.</param>
    /// <param name="writeSection">Writes the value of the section.</param>
    /// <param name="cancellationToken">Cancels the write before the file is replaced.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="ArgumentException"><paramref name="sectionName"/> is not a known section.</exception>
    /// <exception cref="IOException">
    /// The file cannot be read, is not a JSON object or cannot be written; it is left as it is.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be read or written; it is left as it is.</exception>
    public async Task WriteSectionAsync(
        string sectionName, Action<Utf8JsonWriter> writeSection, CancellationToken cancellationToken)
    {
        if (!KnownSections.Contains(sectionName, StringComparer.Ordinal))
        {
            throw new ArgumentException($"The section '{sectionName}' is not kept in the user settings file.", nameof(sectionName));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var sections = await ReadSectionsAsync(cancellationToken);

            using var buffer = new MemoryStream();
            await using (var json = new Utf8JsonWriter(buffer, _writerOptions))
            {
                json.WriteStartObject();
                foreach (var section in _knownSections)
                {
                    if (section.Name == sectionName)
                    {
                        json.WritePropertyName(section.Name);
                        writeSection(json);
                    }
                    else if (sections.TryGetValue(section.Name, out var value))
                    {
                        WriteKnown(json, section, value);
                    }
                }

                json.WriteEndObject();
            }

            buffer.Write(_finalNewLine);
            await writer.WriteAsync(FilePath, buffer.ToArray(), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    /// <summary>
    /// Writes <paramref name="value"/> under the name of <paramref name="key"/> with only the known keys inside it.
    /// </summary>
    /// <remarks>
    /// A key without known children is a single value and is copied only when it is a string, a number, a boolean or
    /// <c>null</c>; a key with known children is copied only when it is an object. Anything else is left out.
    /// </remarks>
    /// <param name="json">The writer.</param>
    /// <param name="key">The known key with its known children.</param>
    /// <param name="value">The value found in the file.</param>
    private static void WriteKnown(Utf8JsonWriter json, KnownKey key, JsonElement value)
    {
        if (key.Children.Count == 0)
        {
            if (value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True
                or JsonValueKind.False or JsonValueKind.Null)
            {
                json.WritePropertyName(key.Name);
                value.WriteTo(json);
            }

            return;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        json.WriteStartObject(key.Name);
        foreach (var child in key.Children)
        {
            if (TryGetProperty(value, child.Name) is { } childValue)
            {
                WriteKnown(json, child, childValue);
            }
        }

        json.WriteEndObject();
    }

    /// <summary>
    /// Returns a property compared without regard to case, as the configuration compares keys; of two properties
    /// with the same name the later one wins.
    /// </summary>
    /// <param name="element">The object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The value of the property, or <see langword="null"/> when there is none.</returns>
    private static JsonElement? TryGetProperty(JsonElement element, string name) =>
        element.EnumerateObject()
            .Where(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(property => (JsonElement?)property.Value)
            .LastOrDefault();

    /// <summary>
    /// Reads the known sections of the file; the caller holds <see cref="_gate"/>.
    /// </summary>
    /// <remarks>
    /// Section names are compared without regard to case, as the configuration does, and of two sections with the
    /// same name the later one wins.
    /// </remarks>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>
    /// Copies of the known sections found, by their name in <see cref="KnownSections"/>; empty when there is no file
    /// or it holds only white space.
    /// </returns>
    /// <exception cref="IOException">The file cannot be read or is not a JSON object.</exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be read.</exception>
    private async Task<Dictionary<string, JsonElement>> ReadSectionsAsync(CancellationToken cancellationToken)
    {
        var sections = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (!fileSystem.File.Exists(FilePath))
        {
            return sections;
        }

        var content = await fileSystem.File.ReadAllBytesAsync(FilePath, cancellationToken);

        // A file saved by a text editor may start with the UTF-8 byte order mark, which the JSON reader rejects.
        ReadOnlyMemory<byte> json = content.AsSpan().StartsWith(_byteOrderMark) ? content.AsMemory(_byteOrderMark.Length) : content;
        if (json.Span.Trim(" \t\r\n"u8).IsEmpty)
        {
            return sections;
        }

        try
        {
            using var document = JsonDocument.Parse(json, _readerOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw NotAnObject(null);
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                var name = KnownSections.FirstOrDefault(
                    known => string.Equals(known, property.Name, StringComparison.OrdinalIgnoreCase));
                if (name is not null)
                {
                    sections[name] = property.Value.Clone();
                }
            }
        }
        catch (JsonException exception)
        {
            throw NotAnObject(exception);
        }

        return sections;
    }

    /// <summary>
    /// Creates the exception for a file whose text is not a JSON object.
    /// </summary>
    /// <param name="innerException">The error of the JSON reader, or <see langword="null"/>.</param>
    /// <returns>The exception.</returns>
    private IOException NotAnObject(JsonException? innerException) => new(
        $"The user settings file '{FilePath}' is not a valid JSON object. Correct or delete it, then save again.",
        innerException);

    /// <summary>
    /// A key the application reads, with the keys it reads inside it when its value is an object.
    /// </summary>
    /// <param name="Name">The key.</param>
    /// <param name="Children">The known keys inside it; empty for a single value.</param>
    private sealed record KnownKey(string Name, IReadOnlyList<KnownKey> Children)
    {
        /// <summary>
        /// Initializes a key with a single value.
        /// </summary>
        /// <param name="name">The key.</param>
        public KnownKey(string name)
            : this(name, [])
        {
        }
    }
}
