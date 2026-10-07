using GitHubBackup.Infrastructure.Configuration;

namespace GitHubBackup.Infrastructure.Tests.Configuration;

/// <summary>
/// Tests <see cref="ToolsOptionsValidator"/>.
/// </summary>
public sealed class ToolsOptionsValidatorTests
{
    private readonly ToolsOptionsValidator _validator = new();

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData(@"C:\Program Files\Git\cmd\git.exe", @"C:\Program Files\7-Zip\7z.exe")]
    public void Validate_PathsUnsetOrAbsolute_Succeeds(string? gitPath, string? sevenZipPath)
    {
        var result = _validator.Validate(null, new() { GitPath = gitPath, SevenZipPath = sevenZipPath });

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("git.exe")]
    [InlineData(@"tools\git.exe")]
    [InlineData(@"C:git.exe")]
    public void Validate_GitPathRelative_FailsNamingKey(string gitPath)
    {
        var result = _validator.Validate(null, new() { GitPath = gitPath });

        result.Failures.ShouldHaveSingleItem().ShouldBe("Tools:GitPath must be an absolute path when set.");
    }

    [Fact]
    public void Validate_SevenZipPathRelative_FailsNamingKey()
    {
        var result = _validator.Validate(null, new() { SevenZipPath = "7z.exe" });

        result.Failures.ShouldHaveSingleItem().ShouldBe("Tools:SevenZipPath must be an absolute path when set.");
    }

    [Fact]
    public void Validate_BothPathsRelative_ReportsEachFailure()
    {
        var result = _validator.Validate(null, new() { GitPath = "git.exe", SevenZipPath = "7z.exe" });

        result.Failures.ShouldNotBeNull().Count().ShouldBe(2);
    }
}
