using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitHubBackup.Infrastructure.State;

/// <summary>
/// Writes a <see cref="DateTimeOffset"/> as local time with its offset and whole seconds, such as
/// <c>2026-10-02T14:30:15+03:00</c>, and reads any ISO 8601 time that carries an offset or <c>Z</c>.
/// </summary>
internal sealed class LocalDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    /// <summary>The format of a written value.</summary>
    private const string _writeFormat = "yyyy-MM-dd'T'HH:mm:sszzz";

    /// <summary>The formats accepted on read; each requires an offset or <c>Z</c>.</summary>
    private static readonly string[] _readFormats =
    [
        _writeFormat,
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
    ];

    /// <inheritdoc />
    /// <exception cref="JsonException">The value is not a string in one of the accepted formats.</exception>
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && DateTimeOffset.TryParseExact(
                reader.GetString(),
                _readFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var value))
        {
            return value;
        }

        throw new JsonException("The time must be an ISO 8601 string with an offset, such as 2026-10-02T14:30:15+03:00.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(_writeFormat, CultureInfo.InvariantCulture));
}
