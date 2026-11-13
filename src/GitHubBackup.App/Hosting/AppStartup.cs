using GitHubBackup.App.Dialogs;
using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.App.Hosting;

/// <summary>
/// Builds, starts and stops the host of the WPF application and reports invalid configuration to the user.
/// </summary>
/// <param name="dialogs">Shows configuration errors; it cannot come from the host, which may fail to build.</param>
/// <param name="buildHost">Builds the host for the given settings, not yet started.</param>
internal sealed class AppStartup(IDialogService dialogs, Func<GitHubBackupHostSettings, IHost> buildHost)
{
    /// <summary>The title of the dialogs the application shows.</summary>
    public const string DialogTitle = "GitHub Backup";

    /// <summary>The first line of the dialog that lists configuration errors.</summary>
    public const string ConfigurationErrorHeading = "Configuration error:";

    /// <summary>The version of the executable, written to the log when the application starts.</summary>
    private static readonly string? _version = typeof(AppStartup).Assembly.GetName().Version?.ToString();

    /// <summary>
    /// Builds the host, validates the configuration by starting it and logs the start.
    /// </summary>
    /// <remarks>
    /// Invalid configuration produces one dialog that lists every error, without a stack trace, and the method
    /// returns <see langword="null"/>. An error raised while the host is built cannot be logged, because the logger
    /// is part of the host; an error raised while it starts is logged as well, and the host is disposed. Any other
    /// exception propagates, as in the console application.
    /// </remarks>
    /// <param name="settings">The process the host is built for.</param>
    /// <param name="cancellationToken">Cancels starting the host.</param>
    /// <returns>The started host, or <see langword="null"/> when the configuration is invalid.</returns>
    public async Task<IHost?> StartAsync(GitHubBackupHostSettings settings, CancellationToken cancellationToken)
    {
        IHost host;
        try
        {
            host = buildHost(settings);
        }
        catch (Exception exception) when (ConfigurationErrorMessages.TryGet(exception, out var messages))
        {
            ShowConfigurationErrors(messages);
            return null;
        }

        try
        {
            await host.StartAsync(cancellationToken);
        }
        catch (Exception exception) when (ConfigurationErrorMessages.TryGet(exception, out var messages))
        {
            var errorLogger = GetLogger(host);
            var errors = string.Join(Environment.NewLine, messages);
            AppLog.ConfigurationInvalid(errorLogger, errors);
            host.Dispose();
            ShowConfigurationErrors(messages);
            return null;
        }
        catch
        {
            host.Dispose();
            throw;
        }

        var logger = GetLogger(host);
        var environmentName = host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName;
        AppLog.Started(logger, _version, environmentName);
        return host;
    }

    /// <summary>
    /// Stops the host, logs the stop and disposes the host, which flushes and closes the log files.
    /// </summary>
    /// <param name="host">The host returned by <see cref="StartAsync"/>.</param>
    /// <param name="cancellationToken">Cancels stopping the host.</param>
    /// <returns>A task that completes when the host is disposed.</returns>
    public static async Task StopAsync(IHost host, CancellationToken cancellationToken)
    {
        using (host)
        {
            await host.StopAsync(cancellationToken);
            var logger = GetLogger(host);
            AppLog.Stopped(logger);
        }
    }

    /// <summary>
    /// Returns the logger of the start-up code from the host.
    /// </summary>
    /// <param name="host">The built host.</param>
    /// <returns>The logger.</returns>
    private static ILogger<AppStartup> GetLogger(IHost host) => host.Services.GetRequiredService<ILogger<AppStartup>>();

    /// <summary>
    /// Shows one dialog with <see cref="ConfigurationErrorHeading"/> and then one line per error.
    /// </summary>
    /// <param name="messages">The configuration errors.</param>
    private void ShowConfigurationErrors(IReadOnlyList<string> messages) =>
        dialogs.ShowError(DialogTitle, string.Join(Environment.NewLine, [ConfigurationErrorHeading, .. messages]));
}
