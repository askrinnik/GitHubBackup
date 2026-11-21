using GitHubBackup.Infrastructure.Logging;
using Microsoft.Extensions.Configuration;

namespace GitHubBackup.Infrastructure.Tests.Logging;

/// <summary>
/// Tests <see cref="SerilogEndpointsOptionsValidator"/> over options bound from configuration, as the host binds them.
/// </summary>
public sealed class SerilogEndpointsOptionsValidatorTests
{
    /// <summary>The list of accepted names that every level failure ends with.</summary>
    private const string _acceptedLevels = "must be one of Verbose, Debug, Information, Warning, Error, Fatal, Off.";

    [Theory]
    [InlineData("Verbose")]
    [InlineData("Debug")]
    [InlineData("Information")]
    [InlineData("Warning")]
    [InlineData("Error")]
    [InlineData("Fatal")]
    [InlineData("Off")]
    [InlineData("off")]
    public void Validate_KnownLevelAndOverride_Succeeds(string level)
    {
        var failures = Validate(new()
        {
            ["Serilog:Endpoints:File:Level"] = level,
            ["Serilog:Endpoints:OpenTelemetry:Override:Microsoft"] = level,
        });

        failures.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_LevelNotSet_Succeeds(string? level)
    {
        var failures = Validate(new() { ["Serilog:Endpoints:Console:Level"] = level });

        failures.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_EndpointSectionNull_Succeeds(string? value)
    {
        var failures = Validate(new() { ["Serilog:Endpoints:LiveLog"] = value });

        failures.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_NoSection_Succeeds() => Validate([]).ShouldBeEmpty();

    [Theory]
    [InlineData("Loud")]
    [InlineData("3")]
    public void Validate_UnknownLevel_FailsNamingKey(string level)
    {
        var failures = Validate(new() { ["Serilog:Endpoints:File:Level"] = level });

        failures.ShouldBe([$"Serilog:Endpoints:File:Level {_acceptedLevels}"]);
    }

    [Theory]
    [InlineData("Loud")]
    [InlineData("")]
    public void Validate_UnknownOverrideLevel_FailsNamingKey(string level)
    {
        var failures = Validate(new() { ["Serilog:Endpoints:LiveLog:Override:GitHubBackup"] = level });

        failures.ShouldBe([$"Serilog:Endpoints:LiveLog:Override:GitHubBackup {_acceptedLevels}"]);
    }

    [Fact]
    public void Validate_EndpointsSectionIsValue_FailsNamingKey()
    {
        var failures = Validate(new() { ["Serilog:Endpoints"] = "Warning" });

        failures.ShouldBe(["Serilog:Endpoints must be an object, not a value."]);
    }

    [Fact]
    public void Validate_EndpointSectionIsValue_FailsNamingKey()
    {
        var failures = Validate(new() { ["Serilog:Endpoints:Console"] = "Warning" });

        failures.ShouldBe(["Serilog:Endpoints:Console must be an object, not a value."]);
    }

    [Fact]
    public void Validate_OverrideIsValue_FailsNamingKey()
    {
        var failures = Validate(new() { ["Serilog:Endpoints:File:Override"] = "Warning" });

        failures.ShouldBe(["Serilog:Endpoints:File:Override must be an object, not a value."]);
    }

    [Fact]
    public void Validate_UnknownEndpointName_FailsNamingKey()
    {
        var failures = Validate(new() { ["Serilog:Endpoints:Fiel:Level"] = "Debug" });

        failures.ShouldBe(
            ["Serilog:Endpoints:Fiel is not a log endpoint; the endpoints are File, Console, LiveLog, OpenTelemetry."]);
    }

    [Fact]
    public void Validate_EndpointNameInOtherCase_BindsAndValidates()
    {
        var failures = Validate(new() { ["Serilog:Endpoints:file:Level"] = "Loud" });

        failures.ShouldBe([$"Serilog:Endpoints:File:Level {_acceptedLevels}"]);
    }

    [Fact]
    public void Validate_SeveralErrors_ReportsStructureFirstThenLevelsByEndpoint()
    {
        var failures = Validate(new()
        {
            ["Serilog:Endpoints:OpenTelemetry:Level"] = "Loud",
            ["Serilog:Endpoints:File:Override:System"] = "Loud",
            ["Serilog:Endpoints:File:Override:Microsoft"] = "Loud",
            ["Serilog:Endpoints:File:Level"] = "Loud",
            ["Serilog:Endpoints:Unknown"] = "Debug",
            ["Serilog:Endpoints:Console"] = "Debug",
        });

        failures.ShouldBe(
        [
            "Serilog:Endpoints:Console must be an object, not a value.",
            "Serilog:Endpoints:Unknown is not a log endpoint; the endpoints are File, Console, LiveLog, OpenTelemetry.",
            $"Serilog:Endpoints:File:Level {_acceptedLevels}",
            $"Serilog:Endpoints:File:Override:Microsoft {_acceptedLevels}",
            $"Serilog:Endpoints:File:Override:System {_acceptedLevels}",
            $"Serilog:Endpoints:OpenTelemetry:Level {_acceptedLevels}",
        ]);
    }

    /// <summary>
    /// Binds <see cref="SerilogEndpointsOptions"/> from <paramref name="values"/> and validates them.
    /// </summary>
    /// <param name="values">The configuration keys and values.</param>
    /// <returns>The failures, empty when the options are valid.</returns>
    private static IReadOnlyList<string> Validate(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var options = new SerilogEndpointsOptions();
        configuration.GetSection(SerilogEndpointsOptions.SectionName).Bind(options);

        var result = new SerilogEndpointsOptionsValidator(configuration).Validate(null, options);

        return result.Failed ? [.. result.Failures!] : [];
    }
}
