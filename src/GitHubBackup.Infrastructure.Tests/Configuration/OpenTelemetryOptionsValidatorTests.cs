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
        var failures = OpenTelemetryOptionsValidator.Validate(enabled, endpoint, headers: null);

        failures.Count.ShouldBe(count);
        failures.ShouldAllBe(failure => failure.StartsWith("OpenTelemetry:OtlpEndpoint") && !failure.Contains("not a uri"));
    }

    [Theory]
    [InlineData("api-key")]
    [InlineData("=secret-value")]
    [InlineData("api-key=")]
    [InlineData("api-key=secret-value,,x=y")]
    [InlineData("api-key=secret-value,")]
    [InlineData("api key=secret-value")]
    [InlineData("api-key=secret\nvalue")]
    public void Validate_HeadersMalformed_FailsNamingKeyWithoutValue(string headers)
    {
        var result = _validator.Validate(
            null,
            new() { Enabled = true, OtlpEndpoint = "https://otlp.nr-data.net:4317", Headers = headers });

        var failure = result.Failures.ShouldHaveSingleItem();
        failure.ShouldBe(
            "OpenTelemetry:Headers must be a comma-separated list of name=value pairs with a header name and a value in each pair.");
        failure.ShouldNotContain("secret");
    }

    [Fact]
    public void Validate_DisabledWithMalformedHeaders_Succeeds()
    {
        var result = _validator.Validate(
            null,
            new() { Enabled = false, OtlpEndpoint = "http://collector.example:4317", Headers = "secret-value" });

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("http://collector.example:4317")]
    [InlineData("http://192.168.1.10:4317")]
    [InlineData("grpc://collector.example:4317")]
    public void Validate_HeadersWithHttpRemoteEndpoint_FailsNamingKey(string endpoint)
    {
        var result = _validator.Validate(
            null,
            new() { Enabled = true, OtlpEndpoint = endpoint, Headers = "api-key=secret-value" });

        var failure = result.Failures.ShouldHaveSingleItem();
        failure.ShouldBe(
            "OpenTelemetry:OtlpEndpoint must be an absolute https URI, or http for a loopback address, when OpenTelemetry:Headers is set.");
        failure.ShouldNotContain("secret-value");
    }

    [Theory]
    [InlineData("https://otlp.nr-data.net:4317")]
    [InlineData("http://localhost:4317")]
    [InlineData("http://127.0.0.1:1")]
    [InlineData("http://[::1]:4317")]
    public void Validate_HeadersWithHttpsOrLoopbackHttp_Succeeds(string endpoint)
    {
        var result = _validator.Validate(
            null,
            new() { Enabled = true, OtlpEndpoint = endpoint, Headers = "api-key=secret-value, x-other = a=b==" });

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_NoHeadersWithHttpRemoteEndpoint_Succeeds(string? headers)
    {
        var result = _validator.Validate(
            null,
            new() { Enabled = true, OtlpEndpoint = "http://collector.example:4317", Headers = headers });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_HeadersWithoutAbsoluteEndpoint_ReportsOnlyAbsoluteUriForEndpoint()
    {
        var result = _validator.Validate(
            null,
            new() { Enabled = true, OtlpEndpoint = "not a uri", Headers = "api-key=secret-value" });

        result.Failures.ShouldHaveSingleItem()
            .ShouldBe("OpenTelemetry:OtlpEndpoint must be an absolute URI when OpenTelemetry:Enabled is true.");
    }

    [Theory]
    [InlineData("api-key=secret-value")]
    [InlineData("api-key=secret-value, x-other = a=b")]
    public void ValidateHeadersFormat_WellFormed_ReturnsNull(string headers) =>
        OpenTelemetryOptionsValidator.ValidateHeadersFormat(headers).ShouldBeNull();

    [Theory]
    [InlineData("api-key=secret-value,,x=y")]
    [InlineData("api-key secret-value")]
    [InlineData("api-key=secretvalue")]
    public void ValidateHeadersFormat_Malformed_ReturnsFailureNamingKeyWithoutValue(string headers)
    {
        var failure = OpenTelemetryOptionsValidator.ValidateHeadersFormat(headers).ShouldNotBeNull();

        failure.ShouldStartWith("OpenTelemetry:Headers");
        failure.ShouldNotContain("secret");
    }
}
