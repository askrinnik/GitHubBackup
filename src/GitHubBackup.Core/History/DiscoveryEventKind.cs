namespace GitHubBackup.Core.History;

/// <summary>
/// Defines what discovery found about a repository.
/// </summary>
public enum DiscoveryEventKind
{
    /// <summary>A repository of a tracked account that <c>backup-config.json</c> does not list.</summary>
    New,

    /// <summary>A listed repository that GitHub does not return.</summary>
    Missing,

    /// <summary>A listed repository that GitHub returns under a different name.</summary>
    Renamed,

    /// <summary>A repository marked unavailable that GitHub returns again.</summary>
    Returned,
}
