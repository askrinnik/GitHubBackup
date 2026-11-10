namespace GitHubBackup.Core.Processes;

/// <summary>
/// The outcome of a process run by <see cref="IProcessRunner"/>.
/// </summary>
/// <remarks>
/// The output lines are not masked, so that callers can parse them; text built from them for a summary or the
/// history must be masked by the caller.
/// </remarks>
/// <param name="ExitCode">The exit code of the process.</param>
/// <param name="Duration">The time from the start of the process to its exit.</param>
/// <param name="StandardOutput">The lines written to standard output, in order.</param>
/// <param name="StandardError">The lines written to standard error, in order.</param>
public sealed record ProcessResult(
    int ExitCode, TimeSpan Duration, IReadOnlyList<string> StandardOutput, IReadOnlyList<string> StandardError)
{
    /// <summary>Gets a value indicating whether the process exited with code 0.</summary>
    public bool Succeeded => ExitCode == 0;
}
