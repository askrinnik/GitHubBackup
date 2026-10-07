using GitHubBackup.Infrastructure.Configuration;

namespace GitHubBackup.Infrastructure.Tests.Configuration;

/// <summary>
/// Tests <see cref="GitHubOptionsValidator"/>.
/// </summary>
public sealed class GitHubOptionsValidatorTests
{
    private readonly GitHubOptionsValidator _validator = new();

    [Theory]
    [InlineData("https://api.github.com")]
    [InlineData("http://localhost:8080/api/v3")]
    public void Validate_AbsoluteHttpUri_Succeeds(string url)
    {
        var result = _validator.Validate(null, new() { ApiBaseUrl = url });

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("api.github.com")]
    [InlineData("/api/v3")]
    [InlineData("ftp://api.github.com")]
    [InlineData(@"C:\api")]
    public void Validate_NotAbsoluteHttpUri_FailsNamingKey(string url)
    {
        var result = _validator.Validate(null, new() { ApiBaseUrl = url });

        result.Failures.ShouldHaveSingleItem().ShouldBe("GitHub:ApiBaseUrl must be an absolute http or https URI.");
    }
}
