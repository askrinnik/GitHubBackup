using System.Text.Json;
using System.Text.Json.Serialization;
using GitHubBackup.Core.Git;

namespace GitHubBackup.Infrastructure.State;

/// <summary>
/// Reads and writes a <see cref="RefSnapshot"/> as a JSON object that maps each ref name to its object id.
/// </summary>
/// <remarks>
/// Names are written in ordinal order, so the same snapshot always produces the same text.
/// </remarks>
internal sealed class RefSnapshotJsonConverter : JsonConverter<RefSnapshot>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">
    /// The value is not an object of string values, or a name is not a valid ref name or occurs twice.
    /// </exception>
    public override RefSnapshot Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("The refs must be a JSON object.");
        }

        var refs = new List<KeyValuePair<string, string>>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString()!;
            if (!reader.Read() || reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("The object id of a ref must be a string.");
            }

            refs.Add(new(name, reader.GetString()!));
        }

        try
        {
            return new(refs);
        }
        catch (ArgumentException)
        {
            // The message of the ArgumentException quotes the ref name from the file, so it is not passed on.
            throw new JsonException("A ref name is not of the form heads/<name> or tags/<name>, has no object id, or occurs twice.");
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, RefSnapshot value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (name, objectId) in value.Refs.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            writer.WriteString(name, objectId);
        }

        writer.WriteEndObject();
    }
}
