using GitHubBackup.Infrastructure.Configuration;

namespace GitHubBackup.Infrastructure.Tests.Configuration;

/// <summary>
/// Tests <see cref="GitHubOptionsValidator"/>.
/// </summary>
public sealed class GitHubOptionsValidatorTests
{
    private const string _failure = "GitHub:ApiBaseUrl must be an absolute https URI, or http for a loopback address.";

    private readonly GitHubOptionsValidator _validator = new();

    [Theory]
    [InlineData("https://api.github.com")]
    [InlineData("https://ghe.example.com/api/v3")]
    [InlineData("http://localhost:8080/api/v3")]
    [InlineData("http://127.0.0.1:5000")]
    [InlineData("http://[::1]:5000")]
    public void Validate_HttpsOrLoopbackHttpUri_Succeeds(string url)
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

        result.Failures.ShouldHaveSingleItem().ShouldBe(_failure);
    }

    [Theory]
    [InlineData("http://api.github.com")]
    [InlineData("http://ghe.example.com/api/v3")]
    [InlineData("http://192.168.1.10:8080")]
    public void Validate_HttpUriOnRemoteHost_FailsNamingKey(string url)
    {
        var result = _validator.Validate(null, new() { ApiBaseUrl = url });

        result.Failures.ShouldHaveSingleItem().ShouldBe(_failure);
    }
}
