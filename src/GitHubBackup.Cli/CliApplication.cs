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

    /// <summary>The error written when a command other than <c>backup</c> is cancelled.</summary>
    public const string CancelledMessage = "The operation was cancelled.";

    /// <summary>The version of the executable, written to the log when a run starts.</summary>
    private static readonly string? _version = typeof(CliApplication).Assembly.GetName().Version?.ToString();

    /// <summary>
    /// Parses the command line, builds the host, validates the configuration by starting it, runs the command, logs
    /// the start and the end of the run, and stops the host again.
    /// </summary>
    /// <remarks>
    /// <c>--help</c> and <c>--version</c> are answered, and an invalid command line is reported, before the host is
    /// built. Invalid configuration or an invalid command line produces one line per error on the error output and
    /// <see cref="ExitCode.Critical"/>; no stack trace is written, because the console shows results and the details
    /// belong in the log. The start entry is written as soon as the host has started, so both log files of the day
    /// exist from then on.
    /// </remarks>
    /// <param name="args">The command-line arguments of the process.</param>
    /// <param name="settings">The process the host is built for; the command line adds its configuration keys and the path of <c>backup-config.json</c>.</param>
    /// <param name="cancellationToken">Cancels starting and stopping the host and the command.</param>
    /// <returns>The exit code of the process.</returns>
    public async Task<ExitCode> RunAsync(
        IReadOnlyList<string> args, GitHubBackupHostSettings settings, CancellationToken cancellationToken)
    {
        var arguments = CliCommandLine.Parse(args);
        if (arguments.Errors.Count > 0)
        {
            return await ReportAsync(CommandLineErrorPrefix, arguments.Errors);
        }

        if (arguments.Command == CliCommand.Information)
        {
            await arguments.ParseResult.InvokeAsync(
                new InvocationConfiguration { Output = console.Output.Profile.Out.Writer, Error = console.Error },
                cancellationToken);
            return ExitCode.Success;
        }

        IHost host;
        try
        {
            host = BuildHost(
                settings with
                {
                    BackupConfigPath = arguments.BackupConfigPath,
                    ConfigurationArguments = arguments.ConfigurationArguments,
                },
                console);
        }
        catch (Exception exception) when (ConfigurationErrorMessages.TryGet(exception, out var messages))
        {
            return await ReportAsync(ConfigurationErrorPrefix, messages);
        }

        using (host)
        {
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

            var exitCode = await RunCancellableAsync(
                host.Services.GetRequiredService<IHostApplicationLifetime>(),
                commandToken => RunCommandAsync(host.Services, arguments, commandToken),
                cancellationToken);

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
    /// Runs a command with a token that is cancelled when <paramref name="cancellationToken"/> is cancelled or the host
    /// begins to stop, which Ctrl+C triggers through the console lifetime of the host.
    /// </summary>
    /// <remarks>
    /// A command that ends with <see cref="OperationCanceledException"/> after its token was cancelled produces one
    /// line on the error output and <see cref="ExitCode.Critical"/>, without a stack trace. The <c>backup</c> command
    /// reports a cancellation that comes after its run took the run lock in its summary and returns normally.
    /// </remarks>
    /// <param name="lifetime">The lifetime of the host, whose <see cref="IHostApplicationLifetime.ApplicationStopping"/> cancels the command.</param>
    /// <param name="command">Runs the command with the token it receives.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The exit code of the command.</returns>
    internal async Task<ExitCode> RunCancellableAsync(
        IHostApplicationLifetime lifetime,
        Func<CancellationToken, Task<ExitCode>> command,
        CancellationToken cancellationToken)
    {
        using var commandCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.ApplicationStopping);
        try
        {
            return await command(commandCancellation.Token);
        }
        catch (OperationCanceledException) when (commandCancellation.IsCancellationRequested)
        {
            await console.Error.WriteLineAsync(BackupSummaryRenderer.ErrorPrefix + CancelledMessage);
            return ExitCode.Critical;
        }
    }

    /// <summary>
    /// Runs the handler of the parsed command.
    /// </summary>
    /// <param name="services">The services of the started host.</param>
    /// <param name="arguments">The parsed command line.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The exit code of the command.</returns>
    private static async Task<ExitCode> RunCommandAsync(
        IServiceProvider services, CliArguments arguments, CancellationToken cancellationToken) => arguments.Command switch
        {
            CliCommand.Backup => await services.GetRequiredService<BackupCommandHandler>().RunAsync(arguments.Backup, cancellationToken),
            CliCommand.Validate => await services.GetRequiredService<ValidateCommandHandler>().RunAsync(cancellationToken),
            CliCommand.SetToken => await services.GetRequiredService<SetTokenCommandHandler>().RunAsync(cancellationToken),
            _ => ExitCode.Success,
        };

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
