using System.IO.Abstractions;
using GitHubBackup.Core;
using GitHubBackup.Core.Archiving;
using GitHubBackup.Core.Authentication;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Core.History;
using GitHubBackup.Core.Processes;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.Security;
using GitHubBackup.Core.State;
using GitHubBackup.Core.Tools;
using GitHubBackup.Infrastructure.Archiving;
using GitHubBackup.Infrastructure.Authentication;
using GitHubBackup.Infrastructure.BackupConfiguration;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.Git;
using GitHubBackup.Infrastructure.GitHub;
using GitHubBackup.Infrastructure.History;
using GitHubBackup.Infrastructure.Processes;
using GitHubBackup.Infrastructure.Runs;
using GitHubBackup.Infrastructure.State;
using GitHubBackup.Infrastructure.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GitHubBackup.Infrastructure.Tests;

/// <summary>
/// Tests <see cref="InfrastructureServiceCollectionExtensions"/>.
/// </summary>
public sealed class InfrastructureServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGitHubBackupInfrastructure_Sections_BindsEveryOptionsClass()
    {
        using var provider = BuildProvider(new()
        {
            ["Backup:ShortHashLength"] = "12",
            ["GitHub:ApiBaseUrl"] = "https://github.example.com/api/v3",
            ["Tools:GitPath"] = @"C:\Git\cmd\git.exe",
            ["History:DatabasePath"] = @"D:\history.db",
            ["OpenTelemetry:Enabled"] = "true",
        });

        provider.GetRequiredService<IOptions<BackupOptions>>().Value.ShortHashLength.ShouldBe(12);
        provider.GetRequiredService<IOptions<GitHubOptions>>().Value.ApiBaseUrl.ShouldBe("https://github.example.com/api/v3");
        provider.GetRequiredService<IOptions<ToolsOptions>>().Value.GitPath.ShouldBe(@"C:\Git\cmd\git.exe");
        provider.GetRequiredService<IOptions<HistoryOptions>>().Value.DatabasePath.ShouldBe(@"D:\history.db");
        provider.GetRequiredService<IOptions<OpenTelemetryOptions>>().Value.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void AddGitHubBackupInfrastructure_RelativePaths_ResolvesAgainstContentRoot()
    {
        using var provider = BuildProvider(new() { ["Backup:ConfigPath"] = "my-config.json" });

        provider.GetRequiredService<IOptions<BackupOptions>>().Value.ConfigPath.ShouldBe(@"C:\app\my-config.json");
        provider.GetRequiredService<IOptions<HistoryOptions>>().Value.DatabasePath.ShouldBe(@"C:\app\data\history.db");
    }

    [Fact]
    public void AddGitHubBackupInfrastructure_FileServices_AreResolved()
    {
        using var provider = BuildProvider(new());

        provider.GetRequiredService<IFileSystem>().ShouldBeOfType<FileSystem>();
        provider.GetRequiredService<IBackupConfigStore>().ShouldBeOfType<JsonBackupConfigStore>();
    }

    [Fact]
    public void AddGitHubBackupInfrastructure_BackupStateStore_IsOneInstancePerProvider()
    {
        using var provider = BuildProvider(new());

        var store = provider.GetRequiredService<IBackupStateStore>();

        store.ShouldBeOfType<JsonBackupStateStore>();
        provider.GetRequiredService<IBackupStateStore>().ShouldBeSameAs(store);
    }

    [Fact]
    public void AddGitHubBackupInfrastructure_ProcessRunner_IsOneInstancePerProvider()
    {
        using var provider = BuildProvider(new());

        var runner = provider.GetRequiredService<IProcessRunner>();

        runner.ShouldBeOfType<ProcessRunner>();
        provider.GetRequiredService<IProcessRunner>().ShouldBeSameAs(runner);
    }

    [Fact]
    public void AddGitHubBackupInfrastructure_ToolLocator_IsOneInstancePerProvider()
    {
        using var provider = BuildProvider(new());

        var locator = provider.GetRequiredService<IToolLocator>();

        locator.ShouldBeOfType<ToolLocator>();
        provider.GetRequiredService<IToolLocator>().ShouldBeSameAs(locator);
    }

    [Fact]
    public void AddGitHubBackupInfrastructure_GitClient_IsOneInstancePerProvider()
    {
        using var provider = BuildProvider(new());

        var client = provider.GetRequiredService<IGitClient>();

        client.ShouldBeOfType<GitClient>();
        provider.GetRequiredService<IGitClient>().ShouldBeSameAs(client);
    }

    [Fact]
    public void AddGitHubBackupInfrastructure_GitHubApi_IsOneInstancePerProvider()
    {
        using var provider = BuildProvider(new());

        var api = provider.GetRequiredService<IGitHubApi>();

        api.ShouldBeOfType<OctokitGitHubApi>();
        provider.GetRequiredService<IGitHubApi>().ShouldBeSameAs(api);
    }

    [Fact]
    public void AddGitHubBackupInfrastructure_Archiver_IsOneInstancePerProvider()
    {
        using var provider = BuildProvider(new());

        var archiver = provider.GetRequiredService<IArchiver>();

        archiver.ShouldBeOfType<SevenZipArchiver>();
        provider.GetRequiredService<IArchiver>().ShouldBeSameAs(archiver);
    }

    [Fact]
    public void AddGitHubBackupInfrastructure_RunLock_IsOneNamedMutexInstancePerProvider()
    {
        using var provider = BuildProvider(new());

        var runLock = provider.GetRequiredService<IRunLock>();

        runLock.ShouldBeOfType<NamedMutexRunLock>();
        provider.GetRequiredService<IRunLock>().ShouldBeSameAs(runLock);
    }

    [Fact]
    public void AddGitHubBackupInfrastructure_RunHistoryStore_IsOneSqliteInstancePerProvider()
    {
        using var provider = BuildProvider(new());

        var store = provider.GetRequiredService<IRunHistoryStore>();

        store.ShouldBeOfType<SqliteRunHistoryStore>();
        provider.GetRequiredService<IRunHistoryStore>().ShouldBeSameAs(store);
    }

    [Fact]
    public void AddGitHubBackupInfrastructure_HostedServices_IncludeHistoryDatabaseInitializerOnce()
    {
        var services = new ServiceCollection()
            .AddGitHubBackupInfrastructure(new ConfigurationBuilder().Build())
            .AddGitHubBackupInfrastructure(new ConfigurationBuilder().Build());

        services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
            .Select(descriptor => descriptor.ImplementationType)
            .ShouldBe([typeof(HistoryDatabaseInitializer)]);
    }

    [Fact]
    public void AddGitHubBackupInfrastructure_HistoryContextFactory_UsesDatabasePathWithoutPooling()
    {
        using var provider = BuildProvider(new() { ["History:DatabasePath"] = @"D:\history\runs.db" });

        using var context = provider.GetRequiredService<IDbContextFactory<HistoryDbContext>>().CreateDbContext();

        var connectionString = new SqliteConnectionStringBuilder(context.Database.GetConnectionString());
        connectionString.DataSource.ShouldBe(@"D:\history\runs.db");
        connectionString.Pooling.ShouldBeFalse();
    }

    [Fact]
    public void AddGitHubBackupInfrastructure_RepositoryPathResolver_DefaultsSourcesRootToContentRoot()
    {
        using var provider = BuildProvider(new());

        provider.GetRequiredService<IRepositoryPathResolver>().GetSourcesRoot(new BackupConfig()).ShouldBe(@"C:\app\sources");
    }

    [Fact]
    public void AddGitHubBackupInfrastructure_TokenConfigured_SecretMaskerMasksToken()
    {
        using var provider = BuildProvider(new() { [GitHubOptions.TokenKey] = "configured-token-value" });

        provider.GetRequiredService<ISecretMasker>().Mask("token configured-token-value").ShouldBe("token ***");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void AddGitHubBackupInfrastructure_NoToken_SecretMaskerLeavesTextUnchanged(string? token)
    {
        using var provider = BuildProvider(new() { [GitHubOptions.TokenKey] = token });

        provider.GetRequiredService<ISecretMasker>().Mask("plain text").ShouldBe("plain text");
    }

    [Fact]
    public async Task AddGitHubBackupInfrastructure_TokenConfigured_TokenProviderFindsToken()
    {
        using var provider = BuildProvider(new() { [GitHubOptions.TokenKey] = "configured-token-value" });

        var token = await provider.GetRequiredService<ITokenProvider>().GetTokenAsync(TestContext.Current.CancellationToken);

        token.Value.ShouldBe("configured-token-value");
        token.SourceName.ShouldBe("configuration (GitHub:Token)");
    }

    [Fact]
    public void AddGitHubBackupInfrastructure_TokenSources_RegisteredInChainOrder()
    {
        using var provider = BuildProvider(new());

        var sources = provider.GetServices<ITokenSource>().ToList();

        sources.Select(source => source.GetType()).ShouldBe(
            [typeof(ConfigurationTokenSource), typeof(CredentialManagerTokenSource), typeof(GitHubCliTokenSource)]);
        sources.Select(source => source.Name).ShouldBe(
        [
            "configuration (GitHub:Token)",
            "Windows Credential Manager (GitHubBackup:https://github.com)",
            "GitHub CLI (gh auth token)",
        ]);
    }

    [Fact]
    public void AddGitHubBackupInfrastructure_TokenStore_IsCredentialManagerOverDefaultTarget()
    {
        using var provider = BuildProvider(new());

        var store = provider.GetRequiredService<ITokenStore>().ShouldBeOfType<CredentialManagerTokenStore>();

        store.Target.ShouldBe(CredentialManagerTokenStore.DefaultTarget);
        provider.GetRequiredService<ITokenStore>().ShouldBeSameAs(store);
    }

    [Fact]
    public void AddGitHubBackupInfrastructure_TokenProvider_IsOneInstancePerProvider()
    {
        using var provider = BuildProvider(new());

        var tokenProvider = provider.GetRequiredService<ITokenProvider>();

        tokenProvider.ShouldBeOfType<TokenProvider>();
        provider.GetRequiredService<ITokenProvider>().ShouldBeSameAs(tokenProvider);
    }

    [Fact]
    public void AddGitHubBackupInfrastructure_InvalidSections_ReportsEveryFailureAtStartup()
    {
        using var provider = BuildProvider(new()
        {
            ["GitHub:ApiBaseUrl"] = "not a uri",
            ["Tools:SevenZipPath"] = "7z.exe",
            ["History:DatabasePath"] = "",
            ["OpenTelemetry:Enabled"] = "true",
            ["OpenTelemetry:OtlpEndpoint"] = "",
        });

        var exception = Should.Throw<AggregateException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        exception.InnerExceptions.Count.ShouldBe(4);
    }

    /// <summary>
    /// Builds a service provider with the core and infrastructure services registered over <paramref name="values"/>
    /// together with logging and a host environment whose content root is <c>C:\app\</c>.
    /// </summary>
    /// <param name="values">The configuration keys and values.</param>
    /// <returns>The service provider; the caller disposes it.</returns>
    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(@"C:\app\");

        return new ServiceCollection()
            .AddSingleton(environment)
            .AddLogging()
            .AddGitHubBackupCore()
            .AddGitHubBackupInfrastructure(configuration)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
