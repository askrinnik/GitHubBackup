namespace GitHubBackup.Core.Logging;

/// <summary>
/// Names the context properties that log entries carry.
/// </summary>
public static class LogProperties
{
    /// <summary>The identifier of the run, in every entry.</summary>
    public const string RunId = "RunId";

    /// <summary>The repository being processed, as <c>owner/repo</c>.</summary>
    public const string Repository = "Repository";

    /// <summary>The operation being performed, for example <c>clone</c>, <c>fetch</c> or <c>archive</c>.</summary>
    public const string Operation = "Operation";
}
