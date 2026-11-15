using GitHubBackup.Core.Telemetry;

namespace GitHubBackup.Core.Tests.Telemetry;

/// <summary>
/// Tests <see cref="TelemetrySettings"/>.
/// </summary>
public sealed class TelemetrySettingsTests
{
    [Fact]
    public void ToString_HeadersSet_NeverContainsValue()
    {
        var text = new TelemetrySettings(true, "https://otlp.nr-data.net:4317", "api-key=secret-license-key").ToString();

        text.ShouldNotContain("secret-license-key");
        text.ShouldNotContain("api-key");
        text.ShouldBe("TelemetrySettings { Enabled = True, OtlpEndpoint = https://otlp.nr-data.net:4317, Headers = *** }");
    }

    [Fact]
    public void ToString_HeadersNotSet_SaysNotSet()
    {
        var text = new TelemetrySettings(false, null, null).ToString();

        text.ShouldBe("TelemetrySettings { Enabled = False, OtlpEndpoint = , Headers = <not set> }");
    }
}
