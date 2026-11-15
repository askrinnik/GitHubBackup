using System.IO.Abstractions;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Reads and writes the text of the JSON settings files next to the executable.
/// </summary>
internal static class JsonSettingsText
{
    /// <summary>
    /// The options of the JSON text: indented with the line breaks of the other settings files, and with non-ASCII
    /// characters of a path kept readable.
    /// </summary>
    public static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        NewLine = "\r\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The options of reading, as lenient as the JSON configuration provider that reads the files at start-up.</summary>
    private static readonly JsonDocumentOptions _readerOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The line break that ends a file.</summary>
    private static readonly byte[] _finalNewLine = "\r\n"u8.ToArray();

    /// <summary>The UTF-8 byte order mark.</summary>
    private static readonly byte[] _byteOrderMark = [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Reads a file whose text must be a JSON object.
    /// </summary>
    /// <param name="fileSystem">The file system.</param>
    /// <param name="path">The absolute path of the file.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>
    /// The parsed document, whose root is an object; <see langword="null"/> when there is no file or it holds only
    /// white space, which counts as no file.
    /// </returns>
    /// <exception cref="IOException">The file cannot be read or its text is not a JSON object.</exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be read.</exception>
    public static async Task<JsonDocument?> ReadObjectAsync(
        IFileSystem fileSystem, string path, CancellationToken cancellationToken)
    {
        if (!fileSystem.File.Exists(path))
        {
            return null;
        }

        var content = await fileSystem.File.ReadAllBytesAsync(path, cancellationToken);

        // A file saved by a text editor may start with the UTF-8 byte order mark, which the JSON reader rejects.
        ReadOnlyMemory<byte> json = content.AsSpan().StartsWith(_byteOrderMark) ? content.AsMemory(_byteOrderMark.Length) : content;
        if (json.Span.Trim(" \t\r\n"u8).IsEmpty)
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, _readerOptions);
        }
        catch (JsonException exception)
        {
            throw NotAnObject(path, exception);
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw NotAnObject(path, null);
        }

        return document;
    }

    /// <summary>
    /// Writes a JSON value into a buffer and returns the content of a file that holds it.
    /// </summary>
    /// <param name="writeValue">Writes the value.</param>
    /// <returns>The UTF-8 text without a byte order mark, ending with a line break.</returns>
    public static byte[] Write(Action<Utf8JsonWriter> writeValue)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writeValue(json);
        }

        buffer.Write(_finalNewLine);
        return buffer.ToArray();
    }

    /// <summary>
    /// Returns a property compared without regard to case, as the configuration compares keys; of two properties
    /// with the same name the later one wins.
    /// </summary>
    /// <param name="element">The object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The value of the property, or <see langword="null"/> when there is none.</returns>
    public static JsonElement? TryGetProperty(JsonElement element, string name) =>
        element.EnumerateObject()
            .Where(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(property => (JsonElement?)property.Value)
            .LastOrDefault();

    /// <summary>
    /// Creates the exception for a file whose text is not a JSON object.
    /// </summary>
    /// <param name="path">The absolute path of the file.</param>
    /// <param name="innerException">The error of the JSON reader, or <see langword="null"/>.</param>
    /// <returns>The exception.</returns>
    private static IOException NotAnObject(string path, JsonException? innerException) => new(
        $"The settings file '{path}' is not a valid JSON object. Correct or delete it, then save again.",
        innerException);
}
