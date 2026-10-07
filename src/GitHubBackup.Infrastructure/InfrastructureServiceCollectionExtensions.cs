using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure;

/// <summary>
/// Registers the services and options of <c>GitHubBackup.Infrastructure</c>.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <param name="services">The service collection of the host.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the infrastructure services, including the <see cref="ISecretMasker"/> that knows the configured
        /// token, and binds <see cref="GitHubOptions"/>, <see cref="ToolsOptions"/>,
        /// <see cref="HistoryOptions"/> and <see cref="OpenTelemetryOptions"/>, validated when the host starts.
        /// </summary>
        /// <remarks>
        /// Relative paths in <see cref="BackupOptions"/> and <see cref="HistoryOptions"/> are resolved against
        /// <see cref="Microsoft.Extensions.Hosting.IHostEnvironment.ContentRootPath"/>, so the host must register
        /// <see cref="Microsoft.Extensions.Hosting.IHostEnvironment"/>.
        /// </remarks>
        /// <param name="configuration">The application configuration.</param>
        /// <returns>The same service collection, for chaining.</returns>
        public IServiceCollection AddGitHubBackupInfrastructure(IConfiguration configuration)
        {
            services.TryAddSingleton<ISecretMasker>(_ => CreateSecretMasker(configuration));

            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<GitHubOptions>, GitHubOptionsValidator>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ToolsOptions>, ToolsOptionsValidator>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<HistoryOptions>, HistoryOptionsValidator>());
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<OpenTelemetryOptions>, OpenTelemetryOptionsValidator>());

            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IPostConfigureOptions<BackupOptions>, ContentRootPathPostConfigure>());
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IPostConfigureOptions<HistoryOptions>, ContentRootPathPostConfigure>());

            services.AddOptions<GitHubOptions>().BindSection(configuration, GitHubOptions.SectionName).ValidateOnStart();
            services.AddOptions<ToolsOptions>().BindSection(configuration, ToolsOptions.SectionName).ValidateOnStart();
            services.AddOptions<HistoryOptions>().BindSection(configuration, HistoryOptions.SectionName).ValidateOnStart();
            services.AddOptions<OpenTelemetryOptions>()
                .BindSection(configuration, OpenTelemetryOptions.SectionName)
                .ValidateOnStart();

            return services;
        }
    }

    /// <summary>
    /// Creates the <see cref="ISecretMasker"/> of the host with <see cref="GitHubOptions.TokenKey"/> registered when
    /// the configuration sets it.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The masker.</returns>
    private static SecretMasker CreateSecretMasker(IConfiguration configuration)
    {
        var masker = new SecretMasker();
        var token = configuration[GitHubOptions.TokenKey];
        if (!string.IsNullOrWhiteSpace(token))
        {
            masker.Register(token);
        }

        return masker;
    }
}
