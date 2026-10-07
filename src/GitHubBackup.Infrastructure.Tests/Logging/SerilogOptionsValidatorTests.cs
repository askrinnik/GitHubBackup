using GitHubBackup.Infrastructure.Logging;
using Serilog.Events;

namespace GitHubBackup.Infrastructure.Tests.Logging;

/// <summary>
/// Tests <see cref="SerilogOptionsValidator"/>.
/// </summary>
public sealed class SerilogOptionsValidatorTests
{
    /// <summary>The list of accepted names that every failure message ends with.</summary>
    private const string _acceptedLevels = "must be one of Verbose, Debug, Information, Warning, Error, Fatal.";

    private readonly SerilogOptionsValidator _validator = new();

    [Theory]
    [InlineData("Verbose")]
    [InlineData("Debug")]
    [InlineData("Information")]
    [InlineData("Warning")]
    [InlineData("Error")]
    [InlineData("Fatal")]
    [InlineData("warning")]
    public void Validate_KnownLevel_Succeeds(string level)
    {
        var options = new SerilogOptions { Default = level };
        options.Override["Microsoft"] = level;

        _validator.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Loud")]
    [InlineData("3")]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_UnknownDefault_FailsNamingKey(string? level)
    {
        var result = _validator.Validate(null, new SerilogOptions { Default = level });

        result.Failures.ShouldNotBeNull().ShouldBe([$"Serilog:MinimumLevel:Default {_acceptedLevels}"]);
    }

    [Fact]
    public void Validate_UnknownOverrides_FailsOncePerKey()
    {
        var options = new SerilogOptions();
        options.Override["System"] = "Loud";
        options.Override["Microsoft"] = "Warning";
        options.Override["GitHubBackup"] = null;

        var result = _validator.Validate(null, options);

        result.Failures.ShouldNotBeNull().ShouldBe(
        [
            $"Serilog:MinimumLevel:Override:GitHubBackup {_acceptedLevels}",
            $"Serilog:MinimumLevel:Override:System {_acceptedLevels}",
        ]);
    }

    [Theory]
    [InlineData("Debug", LogEventLevel.Debug)]
    [InlineData("fatal", LogEventLevel.Fatal)]
    public void ParseLevel_LevelName_ReturnsLevel(string value, LogEventLevel expected) =>
        SerilogOptionsValidator.ParseLevel(value).ShouldBe(expected);
}
