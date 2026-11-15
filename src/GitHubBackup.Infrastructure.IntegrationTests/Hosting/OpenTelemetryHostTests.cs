using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace GitHubBackup.Infrastructure.IntegrationTests.Hosting;

/// <summary>
/// Tests that the host of <see cref="GitHubBackupHostBuilder"/> registers the OpenTelemetry export only when it is
/// enabled.
/// </summary>
public sealed class OpenTelemetryHostTests
{
    [Theory]
    [InlineData("--OpenTelemetry:Enabled=true", true)]
    [InlineData("--OpenTelemetry:Enabled=false", false)]
    public void BuildHost_EnabledSetting_RegistersProvidersOnlyWhenEnabled(string argument, bool registered)
    {
        using var contentRoot = new TemporaryContentRoot("{}");

        using var host = contentRoot.BuildHost(argument);

        (host.Services.GetService<TracerProvider>() is not null).ShouldBe(registered);
        (host.Services.GetService<MeterProvider>() is not null).ShouldBe(registered);
        (host.Services.GetService<LoggerProvider>() is not null).ShouldBe(registered);
    }

    [Fact]
    public void BuildHost_EnabledInAppSettings_RegistersProviders()
    {
        using var contentRoot = new TemporaryContentRoot(
            """{ "OpenTelemetry": { "Enabled": true, "OtlpEndpoint": "http://localhost:4317" } }""");

        using var host = contentRoot.BuildHost();

        host.Services.GetService<TracerProvider>().ShouldNotBeNull();
        host.Services.GetService<MeterProvider>().ShouldNotBeNull();
        host.Services.GetService<LoggerProvider>().ShouldNotBeNull();
    }

    [Fact]
    public void BuildHost_SettingAbsent_RegistersNoProvider()
    {
        using var contentRoot = new TemporaryContentRoot("{}");

        using var host = contentRoot.BuildHost();

        host.Services.GetService<TracerProvider>().ShouldBeNull();
        host.Services.GetService<MeterProvider>().ShouldBeNull();
        host.Services.GetService<LoggerProvider>().ShouldBeNull();
    }

    [Theory]
    [InlineData("http://collector.example:4317", "api-key=header-value-without-format", "OpenTelemetry:OtlpEndpoint must be an absolute https URI")]
    [InlineData("https://otlp.nr-data.net:4317", "header-value-without-format", "OpenTelemetry:Headers must be a comma-separated list")]
    public void BuildHost_HeadersWithRemoteHttpEndpointOrMalformed_FailsValidationWithoutValue(string endpoint, string headers, string expected)
    {
        using var contentRoot = new TemporaryContentRoot("{}");
        using var host = contentRoot.BuildHost(
            "--OpenTelemetry:Enabled=true", $"--OpenTelemetry:OtlpEndpoint={endpoint}", $"--OpenTelemetry:Headers={headers}");

        var exception = Should.Throw<OptionsValidationException>(
            () => host.Services.GetRequiredService<IStartupValidator>().Validate());

        exception.Failures.ShouldHaveSingleItem().ShouldStartWith(expected);
        exception.Message.ShouldNotContain("header-value-without-format");
    }
}
