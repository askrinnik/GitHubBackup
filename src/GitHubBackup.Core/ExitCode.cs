namespace GitHubBackup.Core;

/// <summary>
/// Defines the process exit codes of a backup run.
/// </summary>
/// <remarks>
/// When several conditions hold, the code highest in this order wins:
/// <see cref="Critical"/>, <see cref="RepositoriesFailed"/>, <see cref="DiscoveryEvents"/>, <see cref="Success"/>.
/// </remarks>
public enum ExitCode
{
    /// <summary>Everything succeeded.</summary>
    Success = 0,

    /// <summary>At least one repository finished with a failure.</summary>
    RepositoriesFailed = 1,

    /// <summary>The run could not proceed: settings, tools, token, lock or an API failure during discovery.</summary>
    Critical = 2,

    /// <summary>No failures, but new, missing or renamed repositories were discovered.</summary>
    DiscoveryEvents = 3,
}
