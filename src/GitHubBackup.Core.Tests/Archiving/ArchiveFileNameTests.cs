using GitHubBackup.Core.Archiving;

namespace GitHubBackup.Core.Tests.Archiving;

/// <summary>
/// Tests <see cref="ArchiveFileName"/>.
/// </summary>
public sealed class ArchiveFileNameTests
{
    [Theory]
    [InlineData("owner_repo_20261111-132030_0123456789.zip")]
    [InlineData("a.b.zip")]
    [InlineData("CONSOLE.zip")]
    [InlineData("name")]
    public void IsValid_PlainFileName_ReturnsTrue(string name) => ArchiveFileName.IsValid(name).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("name.")]
    [InlineData("name ")]
    [InlineData(@"sub\a.zip")]
    [InlineData("sub/a.zip")]
    [InlineData(@"C:\a.zip")]
    [InlineData("a:b.zip")]
    [InlineData("a*.zip")]
    [InlineData("a?.zip")]
    [InlineData("a\tb.zip")]
    [InlineData("CON.zip")]
    [InlineData("nul")]
    [InlineData("com1.zip")]
    public void IsValid_UnsafeName_ReturnsFalse(string? name) => ArchiveFileName.IsValid(name).ShouldBeFalse();
}
