using GitHubBackup.Infrastructure.Configuration;

namespace GitHubBackup.Infrastructure.Tests.Configuration;

/// <summary>
/// Tests <see cref="HistoryOptionsValidator"/>.
/// </summary>
public sealed class HistoryOptionsValidatorTests
{
    private readonly HistoryOptionsValidator _validator = new();

    [Fact]
    public void Validate_DefaultOptions_Succeeds()
    {
        var result = _validator.Validate(null, new());

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_DatabasePathEmpty_FailsNamingKey(string path)
    {
        var result = _validator.Validate(null, new() { DatabasePath = path });

        result.Failures.ShouldHaveSingleItem().ShouldBe("History:DatabasePath must not be empty.");
    }
}
