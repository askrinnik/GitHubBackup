using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.Runs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Core.Tests;

/// <summary>
/// Tests <see cref="CoreServiceCollectionExtensions"/>.
/// </summary>
public sealed class CoreServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGitHubBackupCore_BackupSection_BindsBackupOptions()
    {
        using var provider = BuildProvider(new() { ["Backup:ShortHashLength"] = "12" });

        provider.GetRequiredService<IOptions<BackupOptions>>().Value.ShortHashLength.ShouldBe(12);
    }

    [Fact]
    public void AddGitHubBackupCore_InvalidValue_FailsStartupValidation()
    {
        using var provider = BuildProvider(new() { ["Backup:ShortHashLength"] = "3" });

        var exception = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        exception.Failures.ShouldHaveSingleItem().ShouldBe("Backup:ShortHashLength must be between 4 and 40.");
    }

    [Fact]
    public void AddGitHubBackupCore_RunContext_IsOneInstancePerProvider()
    {
        using var provider = BuildProvider(new());
        using var otherProvider = BuildProvider(new());

        var runContext = provider.GetRequiredService<IRunContext>();

        provider.GetRequiredService<IRunContext>().ShouldBeSameAs(runContext);
        otherProvider.GetRequiredService<IRunContext>().RunId.ShouldNotBe(runContext.RunId);
    }

    [Fact]
    public void AddGitHubBackupCore_CalledTwice_RegistersValidatorOnce()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        services.AddGitHubBackupCore(configuration).AddGitHubBackupCore(configuration);

        services.Count(descriptor => descriptor.ServiceType == typeof(IValidateOptions<BackupOptions>)).ShouldBe(1);
    }

    /// <summary>
    /// Builds a service provider with the core services registered over <paramref name="values"/>.
    /// </summary>
    /// <param name="values">The configuration keys and values.</param>
    /// <returns>The service provider; the caller disposes it.</returns>
    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection().AddGitHubBackupCore(configuration).BuildServiceProvider();
    }
}
