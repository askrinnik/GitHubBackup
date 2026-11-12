using System.IO.Abstractions;
using GitHubBackup.Core.Archiving;
using GitHubBackup.Core.Authentication;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Core.Processes;
using GitHubBackup.Core.Security;
using GitHubBackup.Core.State;
using GitHubBackup.Core.Tools;
using GitHubBackup.Infrastructure.Archiving;
using GitHubBackup.Infrastructure.Authentication;
using GitHubBackup.Infrastructure.BackupConfiguration;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.Files;
using GitHubBackup.Infrastructure.Git;
using GitHubBackup.Infrastructure.GitHub;
using GitHubBackup.Infrastructure.Processes;
using GitHubBackup.Infrastructure.State;
using GitHubBackup.Infrastructure.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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
        /// token, the <see cref="ITokenStore"/> in Windows Credential Manager, the <see cref="ITokenProvider"/> with the
        /// token chain (configuration, Credential Manager, GitHub CLI), the <see cref="IFileSystem"/>, the <see cref="IProcessRunner"/>, the <see cref="IToolLocator"/>, the <see cref="IGitClient"/>, the <see cref="IGitHubApi"/>, the <see cref="IArchiver"/>, the <see cref="IBackupConfigStore"/>, the <see cref="IBackupStateStore"/> and the
        /// <see cref="IRepositoryPathResolver"/> with its default clone root in the content root, and binds <see cref="BackupOptions"/>,
        /// <see cref="GitHubOptions"/>, <see cref="ToolsOptions"/>, <see cref="HistoryOptions"/> and
        /// <see cref="OpenTelemetryOptions"/>, validated when the host starts.
        /// </summary>
        /// <remarks>
        /// Relative paths in <see cref="BackupOptions"/> and <see cref="HistoryOptions"/> are resolved against
        /// <see cref="Microsoft.Extensions.Hosting.IHostEnvironment.ContentRootPath"/>, so the host must register
        /// <see cref="Microsoft.Extensions.Hosting.IHostEnvironment"/>.
        /// The validator of <see cref="BackupOptions"/> and the <see cref="TimeProvider"/> that
        /// <see cref="IProcessRunner"/> and <see cref="IGitHubApi"/> need are registered by <c>AddGitHubBackupCore</c>.
        /// </remarks>
        /// <param name="configuration">The application configuration.</param>
        /// <returns>The same service collection, for chaining.</returns>
        public IServiceCollection AddGitHubBackupInfrastructure(IConfiguration configuration)
        {
            services.TryAddSingleton<ISecretMasker>(_ => CreateSecretMasker(configuration));
            services.TryAddSingleton<ITokenStore>(
                _ => new CredentialManagerTokenStore(CredentialManagerTokenStore.DefaultTarget));

            // The order of registration is the order of the token chain.
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<ITokenSource, ConfigurationTokenSource>(_ => new ConfigurationTokenSource(configuration)));
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ITokenSource, CredentialManagerTokenSource>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ITokenSource, GitHubCliTokenSource>());
            services.TryAddSingleton<ITokenProvider, TokenProvider>();
            services.TryAddSingleton<IFileSystem, FileSystem>();
            services.TryAddSingleton<IProcessRunner, ProcessRunner>();
            services.TryAddSingleton<IToolLocator, ToolLocator>();
            services.TryAddSingleton<IGitClient, GitClient>();
            services.TryAddSingleton<IGitHubApi, OctokitGitHubApi>();
            services.TryAddSingleton<IArchiver, SevenZipArchiver>();
            services.TryAddSingleton<AtomicFileWriter>();
            services.TryAddSingleton<IBackupConfigStore, JsonBackupConfigStore>();
            services.TryAddSingleton<IBackupStateStore, JsonBackupStateStore>();
            services.TryAddSingleton<IRepositoryPathResolver>(provider => new RepositoryPathResolver(
                Path.Combine(
                    provider.GetRequiredService<IHostEnvironment>().ContentRootPath,
                    BackupConfig.DefaultSourcesFolderName)));

            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<GitHubOptions>, GitHubOptionsValidator>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ToolsOptions>, ToolsOptionsValidator>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<HistoryOptions>, HistoryOptionsValidator>());
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<OpenTelemetryOptions>, OpenTelemetryOptionsValidator>());

            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IPostConfigureOptions<BackupOptions>, ContentRootPathPostConfigure>());
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IPostConfigureOptions<HistoryOptions>, ContentRootPathPostConfigure>());

            services.AddOptions<BackupOptions>().BindSection(configuration, BackupOptions.SectionName);
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
