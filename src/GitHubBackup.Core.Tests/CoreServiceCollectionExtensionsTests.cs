using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.Runs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
