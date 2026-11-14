using System.Text.Json.Serialization;
using GitHubBackup.Core.Clones;

namespace GitHubBackup.Infrastructure.Clones;

/// <summary>
/// Supplies the source-generated serialization metadata and the format of <see cref="CloneLocations.FileName"/>.
/// </summary>
/// <remarks>
/// Names are camelCase and the file is indented by two spaces with Windows line breaks, like the other files the
/// application writes.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    IndentSize = 2,
    NewLine = "\r\n",
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(CloneLocationsDocument))]
internal sealed partial class CloneLocationsJsonContext : JsonSerializerContext;
