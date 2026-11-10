namespace GitHubBackup.Core.Tools;

/// <summary>
/// Locates the external tools the application drives and verifies that each one starts and reports a version.
/// </summary>
public interface IToolLocator
{
    /// <summary>
    /// Locates git, git-lfs and 7-Zip, runs each to read its version and logs the versions found.
    /// </summary>
    /// <remarks>
    /// The check runs on every call and its result is not cached. It stops at the first tool that is missing.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The paths and versions of the tools.</returns>
    /// <exception cref="ToolUnavailableException">
    /// A tool is missing, cannot be started, exits with a non-zero code or prints no recognizable version.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<ToolSet> LocateAsync(CancellationToken cancellationToken);
}
