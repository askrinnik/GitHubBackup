using GitHubBackup.Core.State;

namespace GitHubBackup.Core.Clones;

/// <summary>
/// Holds the folder each repository and wiki was cloned into, by GitHub ID, so a run can tell that the configured
/// clone folder changed and move the clone instead of cloning again.
/// </summary>
/// <remarks>
/// The GitHub ID survives a rename, so the record of a renamed repository still leads to its clone under the old name.
/// </remarks>
/// <remarks>
/// Initializes a new instance with the locations read from the file.
/// </remarks>
/// <param name="entries">The locations by GitHub ID.</param>
public sealed class CloneLocations(IEnumerable<KeyValuePair<long, CloneLocation>> entries)
{
    /// <summary>The name of the file under the clone root that holds the locations.</summary>
    public const string FileName = ".clone-locations.json";

    /// <summary>The locations by GitHub ID.</summary>
    private readonly Dictionary<long, CloneLocation> _entries = new(entries);

    /// <summary>
    /// Initializes a new instance without any location.
    /// </summary>
    public CloneLocations()
        : this([])
    {
    }

    /// <summary>Gets the locations by GitHub ID.</summary>
    public IReadOnlyDictionary<long, CloneLocation> Entries => _entries;

    /// <summary>Gets a value indicating whether <see cref="Set"/> changed a location since the instance was created.</summary>
    public bool IsChanged { get; private set; }

    /// <summary>
    /// Returns the recorded clone folder of a repository or its wiki.
    /// </summary>
    /// <param name="id">The GitHub ID of the repository.</param>
    /// <param name="target">Whether the repository or its wiki is meant.</param>
    /// <returns>The folder, or <see langword="null"/> when none is recorded.</returns>
    public string? Get(long id, BackupTarget target) =>
        _entries.TryGetValue(id, out var location)
            ? target == BackupTarget.Wiki ? location.Wiki : location.Repository
            : null;

    /// <summary>
    /// Returns the recorded URL the clone of a repository or its wiki points at.
    /// </summary>
    /// <param name="id">The GitHub ID of the repository.</param>
    /// <param name="target">Whether the repository or its wiki is meant.</param>
    /// <returns>The URL, or <see langword="null"/> when none is recorded.</returns>
    public string? GetRemoteUrl(long id, BackupTarget target) =>
        _entries.TryGetValue(id, out var location)
            ? target == BackupTarget.Wiki ? location.WikiUrl : location.RepositoryUrl
            : null;

    /// <summary>
    /// Records the clone folder of a repository or its wiki and the URL the clone points at.
    /// </summary>
    /// <param name="id">The GitHub ID of the repository, greater than zero.</param>
    /// <param name="target">Whether the repository or its wiki is meant.</param>
    /// <param name="folder">The absolute clone folder.</param>
    /// <param name="remoteUrl">The URL of the repository or wiki, without credentials.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is not greater than zero.</exception>
    public void Set(long id, BackupTarget target, string folder, string remoteUrl)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        var current = _entries.GetValueOrDefault(id) ?? new CloneLocation(null, null);
        var updated = target == BackupTarget.Wiki
            ? current with { Wiki = folder, WikiUrl = remoteUrl }
            : current with { Repository = folder, RepositoryUrl = remoteUrl };
        if (updated != current)
        {
            _entries[id] = updated;
            IsChanged = true;
        }
    }
}
