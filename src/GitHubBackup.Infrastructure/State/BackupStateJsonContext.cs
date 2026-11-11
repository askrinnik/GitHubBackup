using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using GitHubBackup.Core.State;

namespace GitHubBackup.Infrastructure.State;

/// <summary>
/// Supplies the source-generated serialization metadata and the format of <see cref="BackupState.FileName"/>.
/// </summary>
/// <remarks>
/// Names are camelCase and the file is indented by two spaces with Windows line breaks, so a written file has the
/// shape of the documented sample. Only the application writes the file, so reading is strict: comments, trailing
/// commas, unknown properties, missing properties and <c>null</c> for a non-nullable property are errors.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    IndentSize = 2,
    NewLine = "\r\n",
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    Converters =
    [
        typeof(BackupStateSectionJsonConverter),
        typeof(RefSnapshotJsonConverter),
        typeof(LocalDateTimeOffsetJsonConverter),
    ])]
[JsonSerializable(typeof(BackupState))]
[JsonSerializable(typeof(IReadOnlyList<ArchiveRecord>))]
internal sealed partial class BackupStateJsonContext : JsonSerializerContext
{
    /// <summary>
    /// Holds <see cref="BackupStateTypeInfo"/>, created on first use because it reads <see cref="Default"/>, whose
    /// generated initializer may not have run while the static fields of this class are initialized.
    /// </summary>
    private static readonly Lazy<JsonTypeInfo<BackupState>> _backupStateTypeInfo = new(CreateBackupStateTypeInfo);

    /// <summary>
    /// Gets the metadata of <see cref="BackupState"/> with the options of this context plus an encoder that keeps
    /// non-ASCII characters of names readable instead of escaping them.
    /// </summary>
    public static JsonTypeInfo<BackupState> BackupStateTypeInfo => _backupStateTypeInfo.Value;

    /// <summary>
    /// Creates <see cref="BackupStateTypeInfo"/>.
    /// </summary>
    /// <returns>The metadata of <see cref="BackupState"/>.</returns>
    private static JsonTypeInfo<BackupState> CreateBackupStateTypeInfo()
    {
        var options = new JsonSerializerOptions(Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        return (JsonTypeInfo<BackupState>)options.GetTypeInfo(typeof(BackupState));
    }
}
