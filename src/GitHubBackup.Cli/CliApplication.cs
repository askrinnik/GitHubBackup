using GitHubBackup.Core;
using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Cli;

/// <summary>
/// Runs the console application: builds and starts the host and maps start-up failures to an exit code.
/// </summary>
/// <param name="standardError">The writer that receives error messages, normally <see cref="Console.Error"/>.</param>
internal sealed class CliApplication(TextWriter standardError)
{
    /// <summary>The prefix of every line that reports a configuration error.</summary>
    public const string ConfigurationErrorPrefix = "Configuration error: ";

    /// <summary>The version of the executable, written to the log when a run starts.</summary>
    private static readonly string? _version = typeof(CliApplication).Assembly.GetName().Version?.ToString();

    /// <summary>
    /// Builds the host, validates the configuration by starting it, logs the start and the end of the run, and stops
    /// the host again.
    /// </summary>
    /// <remarks>
    /// The start entry is written as soon as the host has started, so both log files of the day exist from then on.
    /// Invalid configuration produces one line per error on <c>standardError</c> and <see cref="ExitCode.Critical"/>;
    /// no stack trace is written, because the console shows results and the details belong in the log.
    /// </remarks>
    /// <param name="settings">The process the host is built for.</param>
    /// <param name="cancellationToken">Cancels starting and stopping the host.</param>
    /// <returns>The exit code of the process.</returns>
    public async Task<ExitCode> RunAsync(GitHubBackupHostSettings settings, CancellationToken cancellationToken)
    {
        IHost host;
        try
        {
            host = BuildHost(settings);
        }
        catch (Exception exception) when (ConfigurationErrorMessages.TryGet(exception, out var messages))
        {
            return await ReportAsync(messages);
        }

        using (host)
        {
            try
            {
                await host.StartAsync(cancellationToken);
            }
            catch (Exception exception) when (ConfigurationErrorMessages.TryGet(exception, out var messages))
            {
                return await ReportAsync(messages);
            }

            var logger = host.Services.GetRequiredService<ILogger<CliApplication>>();
            var environment = host.Services.GetRequiredService<IHostEnvironment>();
            CliLog.RunStarted(logger, _version, environment.EnvironmentName);

            await host.StopAsync(cancellationToken);

            CliLog.RunFinished(logger, ExitCode.Success);
        }

        return ExitCode.Success;
    }

    /// <summary>
    /// Builds the host of the console application.
    /// </summary>
    /// <param name="settings">The process the host is built for.</param>
    /// <returns>The host, not yet started.</returns>
    internal static IHost BuildHost(GitHubBackupHostSettings settings) => GitHubBackupHostBuilder.Create(settings).Build();

    /// <summary>
    /// Writes one line per configuration error to the error writer.
    /// </summary>
    /// <param name="messages">The configuration errors.</param>
    /// <returns><see cref="ExitCode.Critical"/>.</returns>
    private async Task<ExitCode> ReportAsync(IReadOnlyList<string> messages)
    {
        foreach (var message in messages)
        {
            await standardError.WriteLineAsync(ConfigurationErrorPrefix + message);
        }

        return ExitCode.Critical;
    }
}
