using System.Text.Json.Serialization;
using GitHubBackup.Core.BackupConfiguration;

namespace GitHubBackup.Infrastructure.BackupConfiguration;

/// <summary>
/// Reads and writes <see cref="RepositoryStatus"/> by name, accepting any case on read and rejecting numbers.
/// </summary>
internal sealed class RepositoryStatusJsonConverter()
    : JsonStringEnumConverter<RepositoryStatus>(namingPolicy: null, allowIntegerValues: false);
