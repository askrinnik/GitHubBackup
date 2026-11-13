using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace GitHubBackup.Infrastructure.Tests.Telemetry;

/// <summary>
/// Tests <see cref="TelemetryServiceCollectionExtensions"/>.
/// </summary>
public sealed class TelemetryServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("False")]
    [InlineData("")]
    [InlineData("perhaps")]
    public void AddGitHubBackupTelemetry_NotEnabled_RegistersNothing(string? enabled)
    {
        var services = new ServiceCollection();
        var configuration = Configuration(enabled, "http://collector:4317");

        services.AddGitHubBackupTelemetry(configuration);

        services.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    public void AddGitHubBackupTelemetry_Enabled_RegistersTracerAndMeterProvidersAndHostedService(string enabled)
    {
        using var provider = BuildProvider(Configuration(enabled, "http://collector:4317"));

        provider.GetService<TracerProvider>().ShouldNotBeNull();
        provider.GetService<MeterProvider>().ShouldNotBeNull();
        provider.GetServices<IHostedService>().ShouldNotBeEmpty();
    }

    [Fact]
    public void AddGitHubBackupTelemetry_Enabled_ExportsToOtlpEndpoint()
    {
        using var provider = BuildProvider(Configuration("true", "http://collector.example:4317"));

        provider.GetRequiredService<IOptionsMonitor<OtlpExporterOptions>>().CurrentValue.Endpoint
            .ShouldBe(new Uri("http://collector.example:4317"));
    }

    [Fact]
    public void AddGitHubBackupTelemetry_EnabledWithDefaultEndpoint_ExportsToLocalCollector()
    {
        using var provider = BuildProvider(Configuration("true", otlpEndpoint: null));

        provider.GetRequiredService<IOptionsMonitor<OtlpExporterOptions>>().CurrentValue.Endpoint
            .ShouldBe(new Uri("http://localhost:4317"));
    }

    /// <summary>
    /// Creates a configuration with the <c>OpenTelemetry</c> section.
    /// </summary>
    /// <param name="enabled">The value of <c>OpenTelemetry:Enabled</c>, or <see langword="null"/> to leave it out.</param>
    /// <param name="otlpEndpoint">The value of <c>OpenTelemetry:OtlpEndpoint</c>, or <see langword="null"/> to leave it out.</param>
    /// <returns>The configuration.</returns>
    private static IConfiguration Configuration(string? enabled, string? otlpEndpoint)
    {
        var values = new Dictionary<string, string?>();
        if (enabled is not null)
        {
            values["OpenTelemetry:Enabled"] = enabled;
        }

        if (otlpEndpoint is not null)
        {
            values["OpenTelemetry:OtlpEndpoint"] = otlpEndpoint;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    /// <summary>
    /// Builds a provider with the configuration and the bound <see cref="OpenTelemetryOptions"/>, as the host
    /// registers them, and the telemetry.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The provider; the caller disposes it.</returns>
    private static ServiceProvider BuildProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.Configure<OpenTelemetryOptions>(configuration.GetSection(OpenTelemetryOptions.SectionName));
        services.AddGitHubBackupTelemetry(configuration);
        return services.BuildServiceProvider();
    }
}
