using GitHubBackup.Core.Configuration;

namespace GitHubBackup.Core.Tests.Configuration;

/// <summary>
/// Tests <see cref="BackupOptionsValidator"/>.
/// </summary>
public sealed class BackupOptionsValidatorTests
{
    private readonly BackupOptionsValidator _validator = new();

    [Fact]
    public void Validate_DefaultOptions_Succeeds()
    {
        var result = _validator.Validate(null, new());

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(4)]
    [InlineData(10)]
    [InlineData(40)]
    public void Validate_ShortHashLengthWithinRange_Succeeds(int length)
    {
        var result = _validator.Validate(null, new() { ShortHashLength = length });

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(41)]
    public void Validate_ShortHashLengthOutOfRange_FailsNamingKey(int length)
    {
        var result = _validator.Validate(null, new() { ShortHashLength = length });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldHaveSingleItem().ShouldBe("Backup:ShortHashLength must be between 4 and 40.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ConfigPathEmpty_FailsNamingKey(string path)
    {
        var result = _validator.Validate(null, new() { ConfigPath = path });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldHaveSingleItem().ShouldBe("Backup:ConfigPath must not be empty.");
    }

    [Fact]
    public void Validate_SeveralRulesBroken_ReportsEachFailure()
    {
        var result = _validator.Validate(null, new() { ConfigPath = "", ShortHashLength = 1 });

        result.Failures.ShouldNotBeNull().Count().ShouldBe(2);
    }
}
