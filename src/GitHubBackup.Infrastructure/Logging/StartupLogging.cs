using GitHubBackup.Core.Runs;
using GitHubBackup.Core.Security;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Creates a logger that writes the daily log files when the host that owns the regular logger could not be built.
/// </summary>
public static class StartupLogging
{
    /// <summary>
    /// Creates a logger factory over the two daily files of <see cref="LogFiles"/>, at the Information level, with a
    /// new run identifier in every entry and secrets masked.
    /// </summary>
    /// <remarks>
    /// The factory owns the Serilog logger: disposing it flushes and closes the files.
    /// </remarks>
    /// <param name="contentRootPath">The folder of the executable.</param>
    /// <param name="masker">The masker applied to every rendered entry.</param>
    /// <returns>The logger factory; the caller disposes it.</returns>
    public static ILoggerFactory CreateLoggerFactory(string contentRootPath, ISecretMasker masker)
    {
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.With(new RunIdEnricher(new RunContext()));
        FileLoggingServiceCollectionExtensions.WriteToDailyFiles(configuration, contentRootPath, masker);
        return new SerilogLoggerFactory(configuration.CreateLogger(), dispose: true);
    }
}
