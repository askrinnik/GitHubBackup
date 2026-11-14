using System.Diagnostics;
using System.Text;
using GitHubBackup.Infrastructure.Hosting;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Runs the console executable as a child process with all three standard streams redirected and waits for it to
/// exit.
/// </summary>
internal static class CliProcess
{
    /// <summary>The longest time one run may take before it is killed, unless the caller sets another limit.</summary>
    private static readonly TimeSpan _defaultTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Starts <paramref name="executablePath"/> in its own folder and collects its output until it exits, killing it
    /// after two minutes.
    /// </summary>
    /// <param name="executablePath">The absolute path of the executable.</param>
    /// <param name="arguments">The command-line arguments, passed one by one.</param>
    /// <param name="environment">The environment variables to set for the child.</param>
    /// <param name="cancellationToken">Cancels the run; the process tree is killed.</param>
    /// <returns>The exit code and the output.</returns>
    /// <exception cref="TimeoutException">The process did not exit within the time limit; it is killed.</exception>
    public static Task<CliProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken) =>
        RunAsync(executablePath, arguments, environment, _defaultTimeout, cancellationToken);

    /// <summary>
    /// Starts <paramref name="executablePath"/> in its own folder and collects its output until it exits.
    /// </summary>
    /// <remarks>
    /// Standard input stays open and unwritten while the process runs: the process sees redirected input, and a
    /// process that waits for input never exits and is killed on timeout. Standard output and standard error are read
    /// concurrently, so a full pipe never blocks the process. The child gets the environment of the test process
    /// without the variables that configure the application or git, plus <paramref name="environment"/>.
    /// </remarks>
    /// <param name="executablePath">
    /// The absolute path of the executable, or a bare file name that is looked up on <c>PATH</c> and runs in the
    /// current folder.
    /// </param>
    /// <param name="arguments">The command-line arguments, passed one by one.</param>
    /// <param name="environment">The environment variables to set for the child.</param>
    /// <param name="timeout">The longest time the run may take before it is killed.</param>
    /// <param name="cancellationToken">Cancels the run; the process tree is killed.</param>
    /// <returns>The exit code and the output.</returns>
    /// <exception cref="TimeoutException">The process did not exit within the time limit; it is killed.</exception>
    public static async Task<CliProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var name in startInfo.Environment.Keys.Where(IsInheritedConfiguration).ToList())
        {
            startInfo.Environment.Remove(name);
        }

        foreach (var (name, value) in environment)
        {
            startInfo.Environment[name] = value;
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        // The readers end when the pipes close, which killing the process tree guarantees.
        var standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
            await Task.WhenAll(standardOutput, standardError).WaitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"{Path.GetFileName(executablePath)} {string.Join(' ', arguments)} did not exit within {timeout}.");
        }
        finally
        {
            process.StandardInput.Close();
        }

        return new(process.ExitCode, await standardOutput, await standardError);
    }

    /// <summary>
    /// Returns whether <paramref name="name"/> configures the application or git, so that a value set on the
    /// developer's machine cannot change the scenario.
    /// </summary>
    /// <param name="name">The name of an environment variable.</param>
    /// <returns><see langword="true"/> for a <c>GitHubBackup__</c> or a <c>GIT_</c> variable.</returns>
    private static bool IsInheritedConfiguration(string name) =>
        name.StartsWith(GitHubBackupHostBuilder.EnvironmentVariablePrefix, StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase);
}
