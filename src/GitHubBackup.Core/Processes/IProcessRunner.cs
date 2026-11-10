namespace GitHubBackup.Core.Processes;

/// <summary>
/// Runs an external tool such as git or 7-Zip as a child process and logs its command line, every output line and
/// its exit.
/// </summary>
/// <remarks>
/// The exit code alone decides success: output on standard error is normal progress for git and is never treated as
/// a failure. A non-zero exit code does not throw; the caller inspects <see cref="ProcessResult.ExitCode"/>.
/// </remarks>
public interface IProcessRunner
{
    /// <summary>
    /// Starts the process described by <paramref name="request"/>, waits for it to exit and returns its result.
    /// </summary>
    /// <param name="request">The executable, arguments, working directory and environment variables.</param>
    /// <param name="cancellationToken">
    /// Cancels the run: the process and every process it started are killed before the method throws.
    /// </param>
    /// <returns>The exit code, the duration and the output of the process.</returns>
    /// <exception cref="ArgumentException">
    /// <see cref="ProcessRequest.FileName"/> or <see cref="ProcessRequest.WorkingDirectory"/> is not a fully
    /// qualified path.
    /// </exception>
    /// <exception cref="ProcessStartException">The process cannot be started.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken);
}
