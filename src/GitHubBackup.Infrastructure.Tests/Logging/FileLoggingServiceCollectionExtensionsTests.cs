using GitHubBackup.Infrastructure.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.Tests.Logging;

/// <summary>
/// Tests the options registration of <see cref="FileLoggingServiceCollectionExtensions"/>; the logger itself writes files
/// and is covered by the integration tests.
/// </summary>
public sealed class FileLoggingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGitHubBackupLogging_MinimumLevelSection_BindsDefaultAndOverrides()
    {
        using var provider = BuildProvider(new()
        {
            ["Serilog:MinimumLevel:Default"] = "Debug",
            ["Serilog:MinimumLevel:Override:Microsoft.Hosting"] = "Warning",
        });

        var options = provider.GetRequiredService<IOptions<SerilogOptions>>().Value;

        options.Default.ShouldBe("Debug");
        options.Override.ShouldBe(new Dictionary<string, string?> { ["Microsoft.Hosting"] = "Warning" });
    }

    [Fact]
    public void AddGitHubBackupLogging_NoSection_DefaultsToInformation()
    {
        using var provider = BuildProvider(new());

        provider.GetRequiredService<IOptions<SerilogOptions>>().Value.Default.ShouldBe("Information");
    }

    [Fact]
    public void AddGitHubBackupLogging_InvalidLevel_FailsStartupValidationNamingKey()
    {
        using var provider = BuildProvider(new() { ["Serilog:MinimumLevel:Default"] = "Loud" });

        var exception = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        exception.Failures.ShouldHaveSingleItem().ShouldStartWith("Serilog:MinimumLevel:Default must be one of ");
    }

    /// <summary>
    /// Builds a service provider with the logging services registered over <paramref name="values"/>.
    /// </summary>
    /// <param name="values">The configuration keys and values.</param>
    /// <returns>The service provider; the caller disposes it.</returns>
    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection().AddGitHubBackupLogging(configuration).BuildServiceProvider();
    }
}
