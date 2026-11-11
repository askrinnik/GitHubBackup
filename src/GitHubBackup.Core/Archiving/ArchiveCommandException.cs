namespace GitHubBackup.Core.Archiving;

/// <summary>
/// Reports that 7-Zip exited with a non-zero code.
/// </summary>
/// <param name="message">The message, naming the command, the exit code and the end of standard error.</param>
/// <param name="command">The command line that failed.</param>
/// <param name="exitCode">The exit code of the command.</param>
public sealed class ArchiveCommandException(string message, string command, int exitCode) : Exception(message)
{
    /// <summary>Gets the command line that failed.</summary>
    public string Command { get; } = command;

    /// <summary>Gets the exit code of the command.</summary>
    public int ExitCode { get; } = exitCode;
}
