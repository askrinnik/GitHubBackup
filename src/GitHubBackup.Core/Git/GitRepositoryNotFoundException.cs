namespace GitHubBackup.Core.Git;

/// <summary>
/// Reports that a git command failed because no repository exists at the remote URL.
/// </summary>
/// <remarks>
/// A wiki that is enabled but has never been edited has no git repository, so callers that back up wikis treat this
/// failure as a skip rather than an error.
/// </remarks>
/// <param name="message">The message, naming the command, the clone folder, the exit code and the end of standard
/// error, with secrets masked.</param>
/// <param name="command">The command line that failed, with secrets masked.</param>
/// <param name="exitCode">The exit code of the command.</param>
public sealed class GitRepositoryNotFoundException(string message, string command, int exitCode)
    : GitCommandException(message, command, exitCode);
