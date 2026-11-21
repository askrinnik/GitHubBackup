using GitHubBackup.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace GitHubBackup.Cli;

/// <summary>
/// Registers the services of the console application.
/// </summary>
internal static class CliServiceCollectionExtensions
{
    /// <param name="services">The service collection of the host.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the outputs of the console, the console log endpoint on <see cref="CliConsole.Error"/>, the
        /// <see cref="BackupSummaryRenderer"/> and the handlers of the commands.
        /// </summary>
        /// <remarks>
        /// Log entries go to the error writer so that standard output keeps only the summary and the questions of
        /// the commands.
        /// </remarks>
        /// <param name="console">The outputs of the console application.</param>
        /// <returns>The same service collection, for chaining.</returns>
        public IServiceCollection AddGitHubBackupCli(CliConsole console)
        {
            services.AddSingleton(console);
            services.AddGitHubBackupConsoleLog(console.Error);
            services.AddSingleton<BackupSummaryRenderer>();
            services.AddSingleton<ConsoleDiscoveryDecisionHandler>();
            services.AddSingleton<BackupCommandHandler>();
            services.AddSingleton<ValidateCommandHandler>();
            services.AddSingleton<SetTokenCommandHandler>();
            services.AddSingleton<SetOtlpHeadersCommandHandler>();
            return services;
        }
    }
}
