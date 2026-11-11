namespace GitHubBackup.Core.Git;

/// <summary>
/// Reports that a git command exited with a non-zero code.
/// </summary>
/// <param name="message">The message, naming the command, the clone folder, the exit code and the end of standard
/// error, with secrets masked.</param>
/// <param name="command">The command line that failed, with secrets masked.</param>
/// <param name="exitCode">The exit code of the command.</param>
public class GitCommandException(string message, string command, int exitCode) : Exception(message)
{
    /// <summary>Gets the command line that failed, with secrets masked.</summary>
    public string Command { get; } = command;

    /// <summary>Gets the exit code of the command.</summary>
    public int ExitCode { get; } = exitCode;
}
