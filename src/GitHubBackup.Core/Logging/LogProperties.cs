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

    /// <summary>The external tool that wrote an entry about its run, for example <c>git</c> or <c>7z</c>.</summary>
    public const string Tool = "Tool";

    /// <summary>The output stream a logged line of an external tool came from: <c>stdout</c> or <c>stderr</c>.</summary>
    public const string Stream = "Stream";
}
