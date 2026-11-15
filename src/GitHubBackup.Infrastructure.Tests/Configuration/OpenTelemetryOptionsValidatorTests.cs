using GitHubBackup.Infrastructure.Configuration;

namespace GitHubBackup.Infrastructure.Tests.Configuration;

/// <summary>
/// Tests <see cref="OpenTelemetryOptionsValidator"/>.
/// </summary>
public sealed class OpenTelemetryOptionsValidatorTests
{
    private readonly OpenTelemetryOptionsValidator _validator = new();

    [Theory]
    [InlineData(false, "not a uri")]
    [InlineData(false, null)]
    [InlineData(true, "http://localhost:4317")]
    public void Validate_DisabledOrAbsoluteEndpoint_Succeeds(bool enabled, string? endpoint)
    {
        var result = _validator.Validate(null, new() { Enabled = enabled, OtlpEndpoint = endpoint });

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/v1/traces")]
    [InlineData("not a uri")]
    public void Validate_EnabledWithoutAbsoluteEndpoint_FailsNamingKey(string? endpoint)
    {
        var result = _validator.Validate(null, new() { Enabled = true, OtlpEndpoint = endpoint });

        result.Failures.ShouldHaveSingleItem()
            .ShouldBe("OpenTelemetry:OtlpEndpoint must be an absolute URI when OpenTelemetry:Enabled is true.");
    }

    [Theory]
    [InlineData(false, "not a uri", 0)]
    [InlineData(true, "http://localhost:4317", 0)]
    [InlineData(true, "not a uri", 1)]
    [InlineData(true, null, 1)]
    public void ValidateValues_EnabledAndEndpoint_ReturnsFailuresWithKeyAndWithoutValue(bool enabled, string? endpoint, int count)
    {
        var failures = OpenTelemetryOptionsValidator.Validate(enabled, endpoint);

        failures.Count.ShouldBe(count);
        failures.ShouldAllBe(failure => failure.StartsWith("OpenTelemetry:OtlpEndpoint") && !failure.Contains("not a uri"));
    }
}
