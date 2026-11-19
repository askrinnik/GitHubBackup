using System.CommandLine;
using GitHubBackup.Core;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.Infrastructure.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Cli;

/// <summary>
/// Runs the console application: parses the command line, builds and starts the host, runs the command and maps
/// failures to an exit code.
/// </summary>
/// <param name="console">The outputs of the console application.</param>
/// <param name="timeProvider">Supplies the local date that names the log file in a start-up error.</param>
/// <param name="createHostBuilder">Creates the host builder for the settings of the run.</param>
internal sealed class CliApplication(
    CliConsole console,
    TimeProvider timeProvider,
    Func<GitHubBackupHostSettings, HostApplicationBuilder> createHostBuilder)
{
    /// <summary>The prefix of every line that reports a configuration error.</summary>
    public const string ConfigurationErrorPrefix = "Configuration error: ";

    /// <summary>The prefix of every line that reports an error in the command line.</summary>
    public const string CommandLineErrorPrefix = "Command-line error: ";

    /// <summary>The prefix of the line that reports an unexpected failure to build or start the host.</summary>
    public const string StartupErrorPrefix = "Startup error: ";

    /// <summary>The error written when a command other than <c>backup</c> is cancelled.</summary>
    public const string CancelledMessage = "The operation was cancelled.";

    /// <summary>The version of the executable, written to the log when a run starts.</summary>
    private static readonly string? _version = typeof(CliApplication).Assembly.GetName().Version?.ToString();

    /// <summary>
    /// Creates the application over the system clock and the shared host builder.
    /// </summary>
    /// <param name="console">The outputs of the console application.</param>
    public CliApplication(CliConsole console)
        : this(console, TimeProvider.System, GitHubBackupHostBuilder.Create)
    {
    }

    /// <summary>
    /// Parses the command line, builds the host, validates the configuration by starting it, runs the command, logs
    /// the start and the end of the run, and stops the host again.
    /// </summary>
    /// <remarks>
    /// <c>--help</c> and <c>--version</c> are answered, and an invalid command line is reported, before the host is
    /// built. Invalid configuration or an invalid command line produces one line per error on the error output and
    /// <see cref="ExitCode.Critical"/>; no stack trace is written, because the console shows results and the details
    /// belong in the log. Any other failure to create the builder, build the host or start it, except a cancellation
    /// through <paramref name="cancellationToken"/>, is reported by <see cref="ReportStartupErrorAsync"/>. The start
    /// entry is written as soon as the host has started, so both log files of the day exist from then on.
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

        var hostSettings = settings with
        {
            BackupConfigPath = arguments.BackupConfigPath,
            ConfigurationArguments = arguments.ConfigurationArguments,
        };

        HostApplicationBuilder builder;
        try
        {
            builder = createHostBuilder(hostSettings);
        }
        catch (Exception exception) when (ConfigurationErrorMessages.TryGet(exception, out var messages))
        {
            return await ReportAsync(ConfigurationErrorPrefix, messages);
        }
        catch (Exception exception) when (!IsCancellation(exception, cancellationToken))
        {
            return await ReportStartupErrorAsync(
                exception, hostSettings.ContentRootPath, CreateMaskerWithoutFiles(hostSettings), hostServices: null);
        }

        IHost host;
        try
        {
            builder.Services.AddGitHubBackupCli(console);
            host = builder.Build();
        }
        catch (Exception exception) when (ConfigurationErrorMessages.TryGet(exception, out var messages))
        {
            return await ReportAsync(ConfigurationErrorPrefix, messages);
        }
        catch (Exception exception) when (!IsCancellation(exception, cancellationToken))
        {
            return await ReportStartupErrorAsync(
                exception, hostSettings.ContentRootPath, CreateMasker(() => builder.Configuration), hostServices: null);
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
            catch (Exception exception) when (!IsCancellation(exception, cancellationToken))
            {
                return await ReportStartupErrorAsync(
                    exception, hostSettings.ContentRootPath, host.Services.GetRequiredService<ISecretMasker>(), host.Services);
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
    /// Returns the message of an unexpected start-up failure as one line.
    /// </summary>
    /// <remarks>
    /// An <see cref="AggregateException"/> is represented by its first inner exception that is not a configuration
    /// error, because that is the one that makes the failure unexpected. Line breaks become spaces, and an empty
    /// message is replaced by the name of the exception type.
    /// </remarks>
    /// <param name="exception">The exception that stopped the start.</param>
    /// <returns>The message, not yet masked.</returns>
    internal static string GetStartupErrorMessage(Exception exception)
    {
        var reported = exception is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions.FirstOrDefault(inner => !ConfigurationErrorMessages.TryGet(inner, out _))
                ?? exception
            : exception;
        var message = string.Join(
            ' ',
            reported.Message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return message.Length == 0 ? reported.GetType().Name : message;
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
            CliCommand.SetOtlpHeaders =>
                await services.GetRequiredService<SetOtlpHeadersCommandHandler>().RunAsync(cancellationToken),
            _ => ExitCode.Success,
        };

    /// <summary>
    /// Creates the masker for a failure to create the host builder, when the configuration of the host does not exist.
    /// </summary>
    /// <remarks>
    /// Only the environment variables with <see cref="GitHubBackupHostBuilder.EnvironmentVariablePrefix"/> and the
    /// configuration keys of the command line are read: the settings files may be the cause of the failure. A token
    /// set only in a file is still masked when it has a recognisable format.
    /// </remarks>
    /// <param name="settings">The process the host was to be built for.</param>
    /// <returns>The masker.</returns>
    private static ISecretMasker CreateMaskerWithoutFiles(GitHubBackupHostSettings settings) =>
        CreateMasker(() => new ConfigurationBuilder()
            .AddEnvironmentVariables(GitHubBackupHostBuilder.EnvironmentVariablePrefix)
            .AddCommandLine([.. settings.ConfigurationArguments])
            .Build());

    /// <summary>
    /// Creates the masker of a start-up error over the configuration that <paramref name="configuration"/> returns.
    /// </summary>
    /// <remarks>
    /// A failure to read the configuration gives a masker without registered secrets, so that the report of the
    /// original failure is never replaced by an exception from the masker.
    /// </remarks>
    /// <param name="configuration">Returns the configuration whose token and OTLP headers are registered.</param>
    /// <returns>The masker.</returns>
    private static ISecretMasker CreateMasker(Func<IConfiguration> configuration)
    {
        try
        {
            return HostSecretMasker.Create(configuration());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new SecretMasker();
        }
    }

    /// <summary>
    /// Returns whether <paramref name="exception"/> is the cancellation requested through
    /// <paramref name="cancellationToken"/>, which propagates instead of being reported as a start-up error.
    /// </summary>
    /// <param name="exception">The exception thrown while the host is built or started.</param>
    /// <param name="cancellationToken">The token of the run.</param>
    /// <returns><see langword="true"/> for an <see cref="OperationCanceledException"/> after the token was cancelled.</returns>
    private static bool IsCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

    /// <summary>
    /// Writes the one line of an unexpected start-up failure to the error output and the full exception to the log.
    /// </summary>
    /// <remarks>
    /// The line carries the message of the exception, never its stack trace, and the path of the day's text log once
    /// the entry is written. A failure to write the log leaves the path out rather than hiding the original error, so
    /// no exception escapes. The whole line passes through <paramref name="masker"/>, because an exception message can
    /// quote a configuration value such as the token.
    /// </remarks>
    /// <param name="exception">The exception that stopped the start.</param>
    /// <param name="contentRootPath">The folder of the executable, which holds the log folder.</param>
    /// <param name="masker">Masks the token of the configuration and recognisable secrets.</param>
    /// <param name="hostServices">
    /// The services of the built host, whose logger writes the entry, or <see langword="null"/> when there is no host
    /// and a logger of <see cref="StartupLogging"/> writes it.
    /// </param>
    /// <returns><see cref="ExitCode.Critical"/>.</returns>
    private async Task<ExitCode> ReportStartupErrorAsync(
        Exception exception, string contentRootPath, ISecretMasker masker, IServiceProvider? hostServices)
    {
        var line = StartupErrorPrefix + GetStartupErrorMessage(exception);
        try
        {
            if (hostServices is null)
            {
                using var loggerFactory = StartupLogging.CreateLoggerFactory(contentRootPath, masker);
                var startupLogger = loggerFactory.CreateLogger<CliApplication>();
                CliLog.StartupFailed(startupLogger, exception);
            }
            else
            {
                var hostLogger = hostServices.GetRequiredService<ILogger<CliApplication>>();
                CliLog.StartupFailed(hostLogger, exception);
            }

            line += $" (log: {LogFiles.GetTextLogPath(contentRootPath, timeProvider)})";
        }
        catch (Exception logException) when (logException is not OutOfMemoryException)
        {
            // The console line is the only report left, so the failure to log is not reported on top of it.
        }

        await console.Error.WriteLineAsync(masker.Mask(line));
        return ExitCode.Critical;
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
