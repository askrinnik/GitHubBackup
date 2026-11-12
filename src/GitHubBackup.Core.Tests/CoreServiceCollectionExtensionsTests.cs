using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Core.Archiving;
using GitHubBackup.Core.Authentication;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Changes;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.Git;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Core.History;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.State;
using GitHubBackup.Core.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GitHubBackup.Core.Tests;

/// <summary>
/// Tests <see cref="CoreServiceCollectionExtensions"/>.
/// </summary>
public sealed class CoreServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGitHubBackupCore_NoConfiguration_ResolvesDefaultBackupOptions()
    {
        using var provider = new ServiceCollection().AddGitHubBackupCore().BuildServiceProvider();

        provider.GetRequiredService<IOptions<BackupOptions>>().Value.ShortHashLength.ShouldBe(new BackupOptions().ShortHashLength);
    }

    [Fact]
    public void AddGitHubBackupCore_InvalidValue_FailsStartupValidation()
    {
        using var provider = new ServiceCollection()
            .AddGitHubBackupCore()
            .Configure<BackupOptions>(options => options.ShortHashLength = 3)
            .BuildServiceProvider();

        var exception = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        exception.Failures.ShouldHaveSingleItem().ShouldBe("Backup:ShortHashLength must be between 4 and 40.");
    }

    [Fact]
    public void AddGitHubBackupCore_RunContext_IsOneInstancePerProvider()
    {
        using var provider = new ServiceCollection().AddGitHubBackupCore().BuildServiceProvider();
        using var otherProvider = new ServiceCollection().AddGitHubBackupCore().BuildServiceProvider();

        var runContext = provider.GetRequiredService<IRunContext>();

        provider.GetRequiredService<IRunContext>().ShouldBeSameAs(runContext);
        otherProvider.GetRequiredService<IRunContext>().RunId.ShouldNotBe(runContext.RunId);
    }

    [Fact]
    public void AddGitHubBackupCore_BackupConfigValidator_IsResolved()
    {
        using var provider = new ServiceCollection().AddGitHubBackupCore().BuildServiceProvider();

        provider.GetRequiredService<IBackupConfigValidator>().ShouldBeOfType<BackupConfigValidator>();
    }

    [Fact]
    public void AddGitHubBackupCore_ExclusionMatcher_IsResolved()
    {
        using var provider = new ServiceCollection().AddGitHubBackupCore().BuildServiceProvider();

        provider.GetRequiredService<IExclusionMatcher>().ShouldBeOfType<ExclusionMatcher>();
    }

    [Fact]
    public void AddGitHubBackupCore_ChangeDetector_IsResolved()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<ILogger<ChangeDetector>>(NullLogger<ChangeDetector>.Instance)
            .AddSingleton(Substitute.For<IGitClient>())
            .AddSingleton<IFileSystem>(new MockFileSystem())
            .AddGitHubBackupCore()
            .BuildServiceProvider();

        provider.GetRequiredService<IChangeDetector>().ShouldBeOfType<ChangeDetector>();
    }

    [Fact]
    public void AddGitHubBackupCore_ArchiveNameBuilder_IsResolved()
    {
        using var provider = new ServiceCollection().AddGitHubBackupCore().BuildServiceProvider();

        provider.GetRequiredService<IArchiveNameBuilder>().ShouldBeOfType<ArchiveNameBuilder>();
    }

    [Fact]
    public void AddGitHubBackupCore_RemoteUrlProvider_IsGitHub()
    {
        using var provider = new ServiceCollection().AddGitHubBackupCore().BuildServiceProvider();

        provider.GetRequiredService<IRemoteUrlProvider>().ShouldBeOfType<GitHubRemoteUrlProvider>();
    }

    [Fact]
    public void AddGitHubBackupCore_BackupOrchestrator_IsRecordingDecoratorAsSingleton()
    {
        using var provider = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton(Substitute.For<IGitClient>())
            .AddSingleton<IFileSystem>(new MockFileSystem())
            .AddSingleton(Substitute.For<IBackupConfigStore>())
            .AddSingleton(Substitute.For<IToolLocator>())
            .AddSingleton(Substitute.For<ITokenProvider>())
            .AddSingleton(Substitute.For<IRunLock>())
            .AddSingleton(Substitute.For<IRepositoryPathResolver>())
            .AddSingleton(Substitute.For<IArchiver>())
            .AddSingleton(Substitute.For<IBackupStateStore>())
            .AddSingleton(Substitute.For<IGitHubApi>())
            .AddSingleton(Substitute.For<IRunHistoryStore>())
            .AddGitHubBackupCore()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();

        orchestrator.ShouldBeOfType<RecordingBackupOrchestrator>();
        provider.GetRequiredService<IBackupOrchestrator>().ShouldBeSameAs(orchestrator);
        provider.GetRequiredService<BackupOrchestrator>().ShouldNotBeNull();
    }

    [Fact]
    public void AddGitHubBackupCore_RepositoryDiscoveryService_IsResolvedAsSingleton()
    {
        using var provider = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton(Substitute.For<IGitHubApi>())
            .AddGitHubBackupCore()
            .BuildServiceProvider();

        var service = provider.GetRequiredService<IRepositoryDiscoveryService>();

        service.ShouldBeOfType<RepositoryDiscoveryService>();
        provider.GetRequiredService<IRepositoryDiscoveryService>().ShouldBeSameAs(service);
    }

    [Fact]
    public void AddGitHubBackupCore_DiscoveryDecisionApplier_IsResolvedAsSingleton()
    {
        using var provider = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton(Substitute.For<IGitClient>())
            .AddSingleton<IFileSystem>(new MockFileSystem())
            .AddSingleton(Substitute.For<IBackupConfigStore>())
            .AddSingleton(Substitute.For<IRepositoryPathResolver>())
            .AddGitHubBackupCore()
            .BuildServiceProvider();

        var applier = provider.GetRequiredService<IDiscoveryDecisionApplier>();

        applier.ShouldBeOfType<DiscoveryDecisionApplier>();
        provider.GetRequiredService<IDiscoveryDecisionApplier>().ShouldBeSameAs(applier);
        provider.GetRequiredService<ICloneRelocator>().ShouldBeOfType<CloneRelocator>();
    }

    [Fact]
    public void AddGitHubBackupCore_AutomaticDiscoveryDecisionHandler_IsResolvedAsSingleton()
    {
        using var provider = new ServiceCollection().AddGitHubBackupCore().BuildServiceProvider();

        var handler = provider.GetRequiredService<AutomaticDiscoveryDecisionHandler>();

        provider.GetRequiredService<AutomaticDiscoveryDecisionHandler>().ShouldBeSameAs(handler);
    }

    [Fact]
    public void AddGitHubBackupCore_TimeProvider_IsSystemClock()
    {
        using var provider = new ServiceCollection().AddGitHubBackupCore().BuildServiceProvider();

        provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(TimeProvider.System);
    }

    [Fact]
    public void AddGitHubBackupCore_CalledTwice_RegistersValidatorOnce()
    {
        var services = new ServiceCollection();

        services.AddGitHubBackupCore().AddGitHubBackupCore();

        services.Count(descriptor => descriptor.ServiceType == typeof(IValidateOptions<BackupOptions>)).ShouldBe(1);
    }
}
