using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
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
    }

    [Fact]
    public void BuildHost_SettingAbsent_RegistersNoProvider()
    {
        using var contentRoot = new TemporaryContentRoot("{}");

        using var host = contentRoot.BuildHost();

        host.Services.GetService<TracerProvider>().ShouldBeNull();
        host.Services.GetService<MeterProvider>().ShouldBeNull();
    }
}
