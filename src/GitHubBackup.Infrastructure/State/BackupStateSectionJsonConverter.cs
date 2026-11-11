using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using GitHubBackup.Core.State;

namespace GitHubBackup.Infrastructure.State;

/// <summary>
/// Reads and writes a <see cref="BackupStateSection"/> as an object with the single property <c>archives</c>.
/// </summary>
/// <remarks>
/// A section without archives is written on one line as <c>{ "archives": [] }</c>, the shape of the documented sample.
/// Only <see cref="BackupStateSection.Archives"/> is stored; <see cref="BackupStateSection.LastArchive"/> is derived.
/// </remarks>
internal sealed class BackupStateSectionJsonConverter : JsonConverter<BackupStateSection>
{
    /// <summary>The name of the property that holds the archives.</summary>
    private const string _archivesPropertyName = "archives";

    /// <summary>The text of a section without archives.</summary>
    private const string _emptySection = """{ "archives": [] }""";

    /// <inheritdoc />
    /// <exception cref="JsonException">
    /// The value is not an object, lacks <c>archives</c>, repeats it or has another property.
    /// </exception>
    public override BackupStateSection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A section must be a JSON object.");
        }

        IReadOnlyList<ArchiveRecord>? archives = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (!reader.ValueTextEquals(_archivesPropertyName) || archives is not null)
            {
                throw new JsonException($"A section must have exactly one property '{_archivesPropertyName}'.");
            }

            reader.Read();
            archives = JsonSerializer.Deserialize(ref reader, GetArchivesTypeInfo(options))
                ?? throw new JsonException($"The '{_archivesPropertyName}' of a section must be an array.");
        }

        return archives is null
            ? throw new JsonException($"A section must have the property '{_archivesPropertyName}'.")
            : new(archives);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, BackupStateSection value, JsonSerializerOptions options)
    {
        if (value.Archives.Count == 0)
        {
            writer.WriteRawValue(_emptySection, skipInputValidation: true);
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName(_archivesPropertyName);
        JsonSerializer.Serialize(writer, value.Archives, GetArchivesTypeInfo(options));
        writer.WriteEndObject();
    }

    /// <summary>
    /// Returns the metadata of the archive list from the options in use, so the list is written with the same
    /// converters and encoder as the rest of the file.
    /// </summary>
    /// <param name="options">The options of the current call.</param>
    /// <returns>The metadata of the archive list.</returns>
    private static JsonTypeInfo<IReadOnlyList<ArchiveRecord>> GetArchivesTypeInfo(JsonSerializerOptions options) =>
        (JsonTypeInfo<IReadOnlyList<ArchiveRecord>>)options.GetTypeInfo(typeof(IReadOnlyList<ArchiveRecord>));
}
