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
        /// Adds the outputs of the console, the <see cref="BackupSummaryRenderer"/> and the
        /// <see cref="BackupCommandHandler"/>.
        /// </summary>
        /// <param name="console">The outputs of the console application.</param>
        /// <returns>The same service collection, for chaining.</returns>
        public IServiceCollection AddGitHubBackupCli(CliConsole console)
        {
            services.AddSingleton(console);
            services.AddSingleton<BackupSummaryRenderer>();
            services.AddSingleton<BackupCommandHandler>();
            return services;
        }
    }
}
