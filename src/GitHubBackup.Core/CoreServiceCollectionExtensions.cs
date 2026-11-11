using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Changes;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.Runs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Core;

/// <summary>
/// Registers the services and options of <c>GitHubBackup.Core</c>.
/// </summary>
public static class CoreServiceCollectionExtensions
{
    /// <param name="services">The service collection of the host.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the core services, including the <see cref="IRunContext"/> of the host, the
        /// <see cref="IBackupConfigValidator"/>, the <see cref="IChangeDetector"/> and <see cref="TimeProvider.System"/>
        /// as the <see cref="TimeProvider"/>, and registers <see cref="BackupOptions"/> with
        /// <see cref="BackupOptionsValidator"/>, validated when the host starts.
        /// </summary>
        /// <remarks>
        /// The options are not bound to a configuration source here: <c>Core</c> does not read configuration, so the
        /// host or <c>GitHubBackup.Infrastructure</c> binds the <c>Backup</c> section.
        /// </remarks>
        /// <returns>The same service collection, for chaining.</returns>
        public IServiceCollection AddGitHubBackupCore()
        {
            services.TryAddSingleton<IRunContext, RunContext>();
            services.TryAddSingleton<IBackupConfigValidator, BackupConfigValidator>();
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IChangeDetector, ChangeDetector>();

            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<BackupOptions>, BackupOptionsValidator>());
            services.AddOptions<BackupOptions>().ValidateOnStart();

            return services;
        }
    }
}
