using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.Runs;
using Microsoft.Extensions.Configuration;
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
        /// Adds the core services, including the <see cref="IRunContext"/> of the host, and binds
        /// <see cref="BackupOptions"/>, validated when the host starts.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <returns>The same service collection, for chaining.</returns>
        public IServiceCollection AddGitHubBackupCore(IConfiguration configuration)
        {
            services.TryAddSingleton<IRunContext, RunContext>();

            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<BackupOptions>, BackupOptionsValidator>());
            services.AddOptions<BackupOptions>()
                .BindSection(configuration, BackupOptions.SectionName)
                .ValidateOnStart();

            return services;
        }
    }
}
