using GitHubBackup.Core.Clones;

namespace GitHubBackup.Infrastructure.Clones;

/// <summary>
/// The content of <see cref="CloneLocations.FileName"/>.
/// </summary>
internal sealed class CloneLocationsDocument
{
    /// <summary>The version of the format this application reads and writes.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Gets or sets the version of the format.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Gets or sets the clone folders of each repository and its wiki, by GitHub ID.</summary>
    public Dictionary<long, CloneLocation> Clones { get; set; } = [];
}
