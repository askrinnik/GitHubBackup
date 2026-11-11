using System.CommandLine;
using GitHubBackup.Core;
using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Cli;

/// <summary>
/// Runs the console application: parses the command line, builds and starts the host, runs the command and maps
/// failures to an exit code.
/// </summary>
/// <param name="console">The outputs of the console application.</param>
internal sealed class CliApplication(CliConsole console)
{
    /// <summary>The prefix of every line that reports a configuration error.</summary>
    public const string ConfigurationErrorPrefix = "Configuration error: ";

    /// <summary>The prefix of every line that reports an error in the command line.</summary>
    public const string CommandLineErrorPrefix = "Command-line error: ";

    /// <summary>The version of the executable, written to the log when a run starts.</summary>
    private static readonly string? _version = typeof(CliApplication).Assembly.GetName().Version?.ToString();

    /// <summary>
    /// Parses the command line, builds the host, validates the configuration by starting it, runs the command, logs
    /// the start and the end of the run, and stops the host again.
    /// </summary>
    /// <remarks>
    /// <c>--help</c> and <c>--version</c> are answered before the host is built. The host is built before command-line
    /// errors are reported, so an invalid <c>--config</c> value is reported as a configuration error. Invalid
    /// configuration or an invalid command line produces one line per error on the error output and
    /// <see cref="ExitCode.Critical"/>; no stack trace is written, because the console shows results and the details
    /// belong in the log. The start entry is written as soon as the host has started, so both log files of the day
    /// exist from then on.
    /// </remarks>
    /// <param name="settings">The process the host is built for.</param>
    /// <param name="cancellationToken">Cancels starting and stopping the host and the command.</param>
    /// <returns>The exit code of the process.</returns>
    public async Task<ExitCode> RunAsync(GitHubBackupHostSettings settings, CancellationToken cancellationToken)
    {
        var arguments = CliCommandLine.Parse(settings.Args);
        if (arguments.Command == CliCommand.Information && arguments.Errors.Count == 0)
        {
            await arguments.ParseResult.InvokeAsync(
                new InvocationConfiguration { Output = console.Output.Profile.Out.Writer, Error = console.Error },
                cancellationToken);
            return ExitCode.Success;
        }

        IHost host;
        try
        {
            host = BuildHost(settings, console);
        }
        catch (Exception exception) when (ConfigurationErrorMessages.TryGet(exception, out var messages))
        {
            return await ReportAsync(ConfigurationErrorPrefix, messages);
        }

        using (host)
        {
            if (arguments.Errors.Count > 0)
            {
                return await ReportAsync(CommandLineErrorPrefix, arguments.Errors);
            }

            try
            {
                await host.StartAsync(cancellationToken);
            }
            catch (Exception exception) when (ConfigurationErrorMessages.TryGet(exception, out var messages))
            {
                return await ReportAsync(ConfigurationErrorPrefix, messages);
            }

            var logger = host.Services.GetRequiredService<ILogger<CliApplication>>();
            var environment = host.Services.GetRequiredService<IHostEnvironment>();
            CliLog.RunStarted(logger, _version, environment.EnvironmentName);

            var exitCode = arguments.Command == CliCommand.Backup
                ? await host.Services.GetRequiredService<BackupCommandHandler>().RunAsync(cancellationToken)
                : ExitCode.Success;

            await host.StopAsync(cancellationToken);

            CliLog.RunFinished(logger, exitCode);
            return exitCode;
        }
    }

    /// <summary>
    /// Builds the host of the console application.
    /// </summary>
    /// <param name="settings">The process the host is built for.</param>
    /// <param name="console">The outputs of the console application.</param>
    /// <returns>The host, not yet started.</returns>
    internal static IHost BuildHost(GitHubBackupHostSettings settings, CliConsole console)
    {
        var builder = GitHubBackupHostBuilder.Create(settings);
        builder.Services.AddGitHubBackupCli(console);
        return builder.Build();
    }

    /// <summary>
    /// Writes one line per error to the error output.
    /// </summary>
    /// <param name="prefix">The prefix of every line.</param>
    /// <param name="messages">The errors.</param>
    /// <returns><see cref="ExitCode.Critical"/>.</returns>
    private async Task<ExitCode> ReportAsync(string prefix, IReadOnlyList<string> messages)
    {
        foreach (var message in messages)
        {
            await console.Error.WriteLineAsync(prefix + message);
        }

        return ExitCode.Critical;
    }
}
