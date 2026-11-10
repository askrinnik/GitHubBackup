using System.ComponentModel;
using System.Diagnostics;
using System.IO.Abstractions;
using System.Text;
using GitHubBackup.Core.Processes;
using GitHubBackup.Core.Security;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.Processes;

/// <summary>
/// Runs external tools with <see cref="Process"/>, without a shell, and logs every output line as its own entry with
/// secrets masked.
/// </summary>
/// <remarks>
/// The <c>Repository</c> property of the entries comes from the logging scope the caller opens; the runner does not
/// know which repository it works for.
/// </remarks>
/// <param name="secretMasker">Masks the command line and the output lines before they are logged.</param>
/// <param name="fileSystem">Checks that the executable and the working directory exist.</param>
/// <param name="timeProvider">Measures the duration of the run.</param>
/// <param name="logger">Writes the entries of the run.</param>
internal sealed class ProcessRunner(
    ISecretMasker secretMasker, IFileSystem fileSystem, TimeProvider timeProvider, ILogger<ProcessRunner> logger)
    : IProcessRunner
{
    /// <summary>The <see cref="Core.Logging.LogProperties.Stream"/> value of a standard output line.</summary>
    internal const string StandardOutputStream = "stdout";

    /// <summary>The <see cref="Core.Logging.LogProperties.Stream"/> value of a standard error line.</summary>
    internal const string StandardErrorStream = "stderr";

    /// <summary>The encoding of the output streams; git writes UTF-8 regardless of the console code page.</summary>
    private static readonly UTF8Encoding _outputEncoding = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Gets how long a cancelled run waits for the killed process to exit and its output to close before it closes
    /// the output streams itself.
    /// </summary>
    internal TimeSpan PipeDrainTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets the action that kills a process together with every process it started; tests replace it to simulate a
    /// failing kill.
    /// </summary>
    internal Action<Process> KillProcessTree { get; init; } = static process => process.Kill(entireProcessTree: true);

    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(request);

        foreach (var variable in request.EnvironmentVariables)
        {
            if (variable.IsSecret && !string.IsNullOrWhiteSpace(variable.Value))
            {
                secretMasker.Register(variable.Value);
            }
        }

        var tool = Path.GetFileNameWithoutExtension(request.FileName);
        if (logger.IsEnabled(LogLevel.Information))
        {
            var commandLine = secretMasker.Mask(ProcessCommandLine.Format(request.FileName, request.Arguments));
            var variableNames = string.Join(", ", request.EnvironmentVariables.Select(variable => variable.Name));
            ProcessRunnerLog.Starting(logger, tool, commandLine, request.WorkingDirectory, variableNames);
        }

        using var process = new Process();
        process.StartInfo = CreateStartInfo(request);
        var startedAt = timeProvider.GetTimestamp();
        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            throw new ProcessStartException($"Cannot start '{request.FileName}': {exception.Message}", exception);
        }

        using var readCancellation = new CancellationTokenSource();
        Task<IReadOnlyList<string>>[] readers = [];
        try
        {
            // The tools never read input; closing it makes a tool that unexpectedly waits for input fail instead of
            // hanging the run.
            process.StandardInput.Close();

            readers =
            [
                ReadLinesAsync(process.StandardOutput, tool, StandardOutputStream, readCancellation.Token),
                ReadLinesAsync(process.StandardError, tool, StandardErrorStream, readCancellation.Token),
            ];

            await process.WaitForExitAsync(cancellationToken);

            // A process started by the tool may still hold the pipes open; the output is complete only when both
            // close, so the wait lasts as long as that process unless the run is cancelled.
            var lines = await Task.WhenAll(readers).WaitAsync(cancellationToken);
            var duration = timeProvider.GetElapsedTime(startedAt);
            var exitCode = process.ExitCode;

            ProcessRunnerLog.Exited(
                logger, exitCode == 0 ? LogLevel.Information : LogLevel.Warning, tool, exitCode, duration);
            return new(exitCode, duration, lines[0], lines[1]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopAsync(process, tool, readers, readCancellation);
            ProcessRunnerLog.Cancelled(logger, tool, timeProvider.GetElapsedTime(startedAt));
            throw;
        }
        catch
        {
            TryKillProcessTree(process, tool);
            throw;
        }
    }

    /// <summary>
    /// Checks that the paths of <paramref name="request"/> are fully qualified and exist.
    /// </summary>
    /// <param name="request">The request to check.</param>
    /// <exception cref="ArgumentException">A path is not fully qualified.</exception>
    /// <exception cref="ProcessStartException">The executable or the working directory does not exist.</exception>
    private void Validate(ProcessRequest request)
    {
        if (!Path.IsPathFullyQualified(request.FileName))
        {
            throw new ArgumentException(
                $"The executable path '{request.FileName}' must be fully qualified.", nameof(request));
        }

        if (!Path.IsPathFullyQualified(request.WorkingDirectory))
        {
            throw new ArgumentException(
                $"The working directory '{request.WorkingDirectory}' must be fully qualified.", nameof(request));
        }

        if (!fileSystem.File.Exists(request.FileName))
        {
            throw new ProcessStartException($"The executable '{request.FileName}' does not exist.");
        }

        if (!fileSystem.Directory.Exists(request.WorkingDirectory))
        {
            throw new ProcessStartException($"The working directory '{request.WorkingDirectory}' does not exist.");
        }
    }

    /// <summary>
    /// Creates the start information: no shell, no window, every stream redirected, the arguments as a list and the
    /// environment variables of <paramref name="request"/> on top of the inherited environment.
    /// </summary>
    /// <param name="request">The request to start.</param>
    /// <returns>The start information.</returns>
    private static ProcessStartInfo CreateStartInfo(ProcessRequest request)
    {
        var startInfo = new ProcessStartInfo(request.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = _outputEncoding,
            StandardErrorEncoding = _outputEncoding,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var variable in request.EnvironmentVariables)
        {
            startInfo.Environment[variable.Name] = variable.Value;
        }

        return startInfo;
    }

    /// <summary>
    /// Reads <paramref name="reader"/> to its end and logs every non-empty line, masked, as its own entry.
    /// </summary>
    /// <param name="reader">The output stream of the process.</param>
    /// <param name="tool">The name of the tool.</param>
    /// <param name="stream">The name of the stream.</param>
    /// <param name="cancellationToken">Stops reading when the run abandons the output.</param>
    /// <returns>Every line read, unmasked and in order.</returns>
    private async Task<IReadOnlyList<string>> ReadLinesAsync(
        StreamReader reader, string tool, string stream, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lines.Add(line);
            if (line.Length > 0 && logger.IsEnabled(LogLevel.Information))
            {
                var maskedLine = secretMasker.Mask(line);
                ProcessRunnerLog.OutputLine(logger, tool, stream, maskedLine);
            }
        }

        return lines;
    }

    /// <summary>
    /// Kills the tree of <paramref name="process"/> and waits up to <see cref="PipeDrainTimeout"/> for the process to
    /// exit and the output to close; when that does not happen, stops reading and closes the output streams.
    /// </summary>
    /// <remarks>
    /// A process whose parent has already exited is not part of the tree, so the kill cannot reach it, yet it may
    /// hold inherited pipes open for as long as it runs; closing the streams lets the cancelled run end anyway.
    /// </remarks>
    /// <param name="process">The started process.</param>
    /// <param name="tool">The name of the tool.</param>
    /// <param name="readers">The tasks reading the output; empty when reading had not begun.</param>
    /// <param name="readCancellation">Cancels the pending reads.</param>
    /// <returns>A task that completes when the run can end; it never faults.</returns>
    private async Task StopAsync(
        Process process, string tool, Task[] readers, CancellationTokenSource readCancellation)
    {
        TryKillProcessTree(process, tool);

        var drained = Task.WhenAll([process.WaitForExitAsync(CancellationToken.None), .. readers]);
        Observe(drained);
        foreach (var reader in readers)
        {
            Observe(reader);
        }

        using var delayCancellation = new CancellationTokenSource();
        var completed = await Task.WhenAny(drained, Task.Delay(PipeDrainTimeout, timeProvider, delayCancellation.Token));
        await delayCancellation.CancelAsync();
        if (completed == drained)
        {
            return;
        }

        ProcessRunnerLog.OutputAbandoned(logger, tool, PipeDrainTimeout);
        await readCancellation.CancelAsync();
        try
        {
            process.StandardOutput.Dispose();
            process.StandardError.Dispose();
        }
        catch (IOException)
        {
            // The streams are abandoned either way; a failure to close them must not replace the cancellation.
        }
    }

    /// <summary>
    /// Kills <paramref name="process"/> together with every process it started, and logs a failure to do so instead
    /// of throwing, so that the exception the caller is about to throw is not replaced.
    /// </summary>
    /// <param name="process">The started process.</param>
    /// <param name="tool">The name of the tool.</param>
    private void TryKillProcessTree(Process process, string tool)
    {
        try
        {
            KillProcessTree(process);
        }
        catch (InvalidOperationException)
        {
            // The process has already exited; there is nothing left to stop.
        }
        catch (Exception exception) when (exception is Win32Exception or AggregateException or NotSupportedException)
        {
            var message = secretMasker.Mask(exception.Message);
            ProcessRunnerLog.KillFailed(logger, tool, message);
        }
    }

    /// <summary>
    /// Marks a fault of <paramref name="task"/> as observed, for a task the run stops waiting for.
    /// </summary>
    /// <param name="task">The task.</param>
    private static void Observe(Task task) =>
        task.ContinueWith(
            static faulted => _ = faulted.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}
