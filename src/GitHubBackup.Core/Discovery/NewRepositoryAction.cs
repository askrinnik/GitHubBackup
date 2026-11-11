namespace GitHubBackup.Core.Discovery;

/// <summary>
/// Names what happens to a repository that discovery found and the configuration does not list.
/// </summary>
public enum NewRepositoryAction
{
    /// <summary>The repository is added to the configuration and backed up.</summary>
    Add,

    /// <summary>Nothing is written; discovery offers the repository again on the next run.</summary>
    Skip,

    /// <summary>An <c>exclude</c> mask is added, so discovery never offers the repository again.</summary>
    Exclude,
}
