using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using GitHubBackup.Core.BackupConfiguration;

namespace GitHubBackup.Infrastructure.BackupConfiguration;

/// <summary>
/// Supplies the source-generated serialization metadata and the format of <c>backup-config.json</c>.
/// </summary>
/// <remarks>
/// Names are camelCase, <c>null</c> values are written explicitly, <see cref="RepositoryStatus"/> is a string, and the
/// file is indented by two spaces with Windows line breaks, so a saved file has the shape of the documented sample.
/// Reading tolerates comments and trailing commas a person may leave, but rejects unknown properties, <c>null</c>
/// for a non-nullable property and numeric statuses, so a typo is reported instead of silently ignored. The <c>id</c>
/// of a repository entry is the exception: <c>null</c>, like a missing <c>id</c>, reads as <c>0</c>, and an <c>id</c>
/// of <c>0</c> is not written, so an entry whose ID is still unknown keeps its shape through a save. The optional
/// <c>clonePath</c> of a repository entry is written only when it is set, so entries without it keep their shape.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    IndentSize = 2,
    NewLine = "\r\n",
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    RespectNullableAnnotations = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    Converters = [typeof(RepositoryStatusJsonConverter)])]
[JsonSerializable(typeof(BackupConfig))]
internal sealed partial class BackupConfigJsonContext : JsonSerializerContext
{
    /// <summary>
    /// Holds <see cref="BackupConfigTypeInfo"/>, created on first use because it reads <see cref="Default"/>, whose
    /// generated initializer may not have run while the static fields of this class are initialized.
    /// </summary>
    private static readonly Lazy<JsonTypeInfo<BackupConfig>> _backupConfigTypeInfo = new(CreateBackupConfigTypeInfo);

    /// <summary>
    /// Gets the metadata of <see cref="BackupConfig"/> with the options of this context plus an encoder that keeps
    /// non-ASCII characters of paths readable instead of escaping them, and the handling of the repository <c>id</c>.
    /// </summary>
    public static JsonTypeInfo<BackupConfig> BackupConfigTypeInfo => _backupConfigTypeInfo.Value;

    /// <summary>
    /// Creates <see cref="BackupConfigTypeInfo"/>.
    /// </summary>
    /// <returns>The metadata of <see cref="BackupConfig"/>.</returns>
    private static JsonTypeInfo<BackupConfig> CreateBackupConfigTypeInfo()
    {
        var options = new JsonSerializerOptions(Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = Default.WithAddedModifier(ModifyRepositoryId),
        };

        return (JsonTypeInfo<BackupConfig>)options.GetTypeInfo(typeof(BackupConfig));
    }

    /// <summary>
    /// Makes the <c>id</c> of both kinds of repository entry read <c>null</c> as <c>0</c> and skips it on write when it
    /// is <c>0</c>, and skips their <c>clonePath</c> on write when it is <see langword="null"/>.
    /// </summary>
    /// <param name="typeInfo">The metadata of one type of the configuration.</param>
    private static void ModifyRepositoryId(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(AccountRepositoryConfig) && typeInfo.Type != typeof(RepositoryConfig))
        {
            return;
        }

        foreach (var property in typeInfo.Properties.Where(property => property.Name == "id"))
        {
            property.CustomConverter = new RepositoryIdJsonConverter();
            property.ShouldSerialize = static (_, value) => value is long id && id != 0;
        }

        foreach (var property in typeInfo.Properties.Where(property => property.Name == "clonePath"))
        {
            property.ShouldSerialize = static (_, value) => value is not null;
        }
    }
}
