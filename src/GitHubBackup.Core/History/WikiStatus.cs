namespace GitHubBackup.Core.History;

/// <summary>
/// The state of the wiki of a repository, as its latest run shows it.
/// </summary>
public enum WikiStatus
{
    /// <summary>The wiki was archived, was up to date, finished with a warning or would be archived or cloned.</summary>
    Ok,

    /// <summary>The wiki is enabled but has no pages.</summary>
    NoPages,

    /// <summary>The backup of the wiki failed.</summary>
    Failed,

    /// <summary>The run was cancelled while the wiki was processed.</summary>
    Cancelled,

    /// <summary>The repository was processed and its wiki is turned off.</summary>
    Off,

    /// <summary>The history says nothing about the wiki.</summary>
    Unknown,
}
