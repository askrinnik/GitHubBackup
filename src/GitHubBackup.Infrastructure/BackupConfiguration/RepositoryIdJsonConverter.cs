using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitHubBackup.Infrastructure.BackupConfiguration;

/// <summary>
/// Reads the <c>id</c> of a repository entry, where <c>null</c> means the same as a missing <c>id</c>: an entry
/// whose ID is resolved by name at the start of a run.
/// </summary>
internal sealed class RepositoryIdJsonConverter : JsonConverter<long>
{
    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? 0 : reader.GetInt64();

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);
}
