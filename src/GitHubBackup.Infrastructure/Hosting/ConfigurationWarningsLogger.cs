using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.Hosting;

/// <summary>
/// Logs the <see cref="ConfigurationWarnings"/> when the host starts.
/// </summary>
/// <param name="warnings">The warnings of the configuration providers.</param>
/// <param name="logger">The logger that receives one warning entry per warning.</param>
internal sealed partial class ConfigurationWarningsLogger(
    ConfigurationWarnings warnings, ILogger<ConfigurationWarningsLogger> logger) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var warning in warnings.Items)
        {
            ConfigurationWarning(logger, warning);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Logs a warning of a configuration provider.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="warning">The text of the warning.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Configuration: {Warning}")]
    private static partial void ConfigurationWarning(ILogger logger, string warning);
}
