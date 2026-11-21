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
        options.Override.ShouldBe(new Dictionary<string, string?>
        {
            [SerilogOptions.HostingLifetimeSource] = "Warning",
            ["Microsoft.Hosting"] = "Warning",
        });
    }

    [Fact]
    public void AddGitHubBackupLogging_NoSection_RaisesHostingLifetimeToWarning()
    {
        using var provider = BuildProvider(new());

        provider.GetRequiredService<IOptions<SerilogOptions>>().Value.Override.ShouldBe(
            new Dictionary<string, string?> { [SerilogOptions.HostingLifetimeSource] = "Warning" });
    }

    [Fact]
    public void AddGitHubBackupLogging_HostingLifetimeOverrideInOtherCase_ReplacesDefault()
    {
        using var provider = BuildProvider(new() { ["Serilog:MinimumLevel:Override:microsoft.hosting.lifetime"] = "Information" });

        var options = provider.GetRequiredService<IOptions<SerilogOptions>>().Value;

        options.Override.ShouldHaveSingleItem().Value.ShouldBe("Information");
        options.Override[SerilogOptions.HostingLifetimeSource].ShouldBe("Information");
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

    [Fact]
    public void AddGitHubBackupLogging_NoBufferCall_KeepsNoLiveLogEntries()
    {
        using var provider = BuildProvider(new());

        provider.GetRequiredService<IOptions<LiveLogOptions>>().Value.BufferCapacity.ShouldBe(0);
    }

    [Fact]
    public void AddGitHubBackupLiveLogBuffer_Capacity_SetsBufferCapacity()
    {
        using var provider = BuildProvider(new(), services => services.AddGitHubBackupLiveLogBuffer(5000));

        provider.GetRequiredService<IOptions<LiveLogOptions>>().Value.BufferCapacity.ShouldBe(5000);
        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(LiveLogOptions.MaxBufferCapacity + 1)]
    public void AddGitHubBackupLiveLogBuffer_CapacityOutOfRange_FailsStartupValidation(int capacity)
    {
        using var provider = BuildProvider(new(), services => services.AddGitHubBackupLiveLogBuffer(capacity));

        var exception = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        exception.Failures.ShouldHaveSingleItem().ShouldStartWith("LiveLogOptions:BufferCapacity must be from 0 to ");
    }

    [Fact]
    public void AddGitHubBackupLogging_EndpointsSection_BindsLevelsAndOverrides()
    {
        using var provider = BuildProvider(new()
        {
            ["Serilog:Endpoints:File:Level"] = "Debug",
            ["Serilog:Endpoints:Console:Level"] = "Off",
            ["Serilog:Endpoints:OpenTelemetry:Override:GitHubBackup"] = "Error",
        });

        var options = provider.GetRequiredService<IOptions<SerilogEndpointsOptions>>().Value;

        options.File.Level.ShouldBe("Debug");
        options.Console.Level.ShouldBe("Off");
        options.LiveLog.Level.ShouldBeNull();
        options.OpenTelemetry.Override.ShouldBe(new Dictionary<string, string?> { ["GitHubBackup"] = "Error" });
        options.OpenTelemetry.Override.ContainsKey("githubbackup").ShouldBeTrue();
    }

    [Fact]
    public void AddGitHubBackupLogging_InvalidEndpointLevel_FailsStartupValidationNamingKey()
    {
        using var provider = BuildProvider(new() { ["Serilog:Endpoints:LiveLog:Level"] = "Loud" });

        var exception = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        exception.Failures.ShouldHaveSingleItem().ShouldStartWith("Serilog:Endpoints:LiveLog:Level must be one of ");
    }

    [Fact]
    public void AddGitHubBackupLogging_NoBufferCall_LeavesLiveLogDisabled()
    {
        using var provider = BuildProvider(new());

        provider.GetRequiredService<IOptions<LiveLogOptions>>().Value.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void AddGitHubBackupLiveLogBuffer_Called_EnablesLiveLog()
    {
        using var provider = BuildProvider(new(), services => services.AddGitHubBackupLiveLogBuffer(0));

        provider.GetRequiredService<IOptions<LiveLogOptions>>().Value.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void AddGitHubBackupConsoleLog_Writer_RegistersConsoleTarget()
    {
        using var writer = new StringWriter();

        using var provider = BuildProvider(new(), services => services.AddGitHubBackupConsoleLog(writer));

        provider.GetRequiredService<ConsoleLogTarget>().Writer.ShouldBeSameAs(writer);
    }

    /// <summary>
    /// Builds a service provider with the logging services registered over <paramref name="values"/>.
    /// </summary>
    /// <param name="values">The configuration keys and values.</param>
    /// <param name="configure">Adds further registrations after the logging services, if given.</param>
    /// <returns>The service provider; the caller disposes it.</returns>
    private static ServiceProvider BuildProvider(
        Dictionary<string, string?> values, Action<IServiceCollection>? configure = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection().AddGitHubBackupLogging(configuration);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
