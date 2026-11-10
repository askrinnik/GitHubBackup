using GitHubBackup.Infrastructure.Tools;

namespace GitHubBackup.Infrastructure.Tests.Tools;

/// <summary>
/// Tests <see cref="ToolVersionParser"/>.
/// </summary>
public sealed class ToolVersionParserTests
{
    [Theory]
    [InlineData("git version 2.46.0.windows.1", "2.46.0.windows.1")]
    [InlineData("git version 2.39.2", "2.39.2")]
    public void TryParseGit_VersionLine_ReturnsVersion(string line, string expected)
    {
        ToolVersionParser.TryParseGit([line], out var version).ShouldBeTrue();

        version.ShouldBe(expected);
    }

    [Theory]
    [InlineData("git-lfs/3.5.1 (GitHub; windows amd64; go 1.21.5)", "3.5.1")]
    [InlineData("git-lfs/3.6.0 (GitHub; windows arm64; go 1.22.2; git 0123abc)", "3.6.0")]
    public void TryParseGitLfs_VersionLine_ReturnsVersion(string line, string expected)
    {
        ToolVersionParser.TryParseGitLfs([line], out var version).ShouldBeTrue();

        version.ShouldBe(expected);
    }

    [Theory]
    [InlineData("7-Zip 24.08 (x64) : Copyright (c) 1999-2024 Igor Pavlov : 2024-08-11", "24.08")]
    [InlineData("7-Zip 23.01 (x86) : Copyright (c) 1999-2023 Igor Pavlov : 2023-06-20", "23.01")]
    [InlineData("7-Zip (a) 24.08 (x64) : Copyright (c) 1999-2024 Igor Pavlov : 2024-08-11", "24.08")]
    [InlineData("7-Zip [64] 16.04 : Copyright (c) 1999-2016 Igor Pavlov : 2016-10-04", "16.04")]
    public void TryParseSevenZip_Banner_ReturnsVersion(string line, string expected)
    {
        ToolVersionParser.TryParseSevenZip([line], out var version).ShouldBeTrue();

        version.ShouldBe(expected);
    }

    [Fact]
    public void TryParseSevenZip_VersionNotOnFirstLine_ReturnsVersion()
    {
        string[] lines = ["", "  ", "7-Zip 24.08 (x64) : Copyright (c) 1999-2024 Igor Pavlov", "Usage: 7z <command>"];

        ToolVersionParser.TryParseSevenZip(lines, out var version).ShouldBeTrue();

        version.ShouldBe("24.08");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Microsoft Windows [Version 10.0.26300]")]
    [InlineData("git version")]
    [InlineData("7-Zip")]
    public void TryParse_UnrelatedLine_ReturnsFalse(string line)
    {
        string[] lines = [line];
        ToolVersionParser.TryParseGit(lines, out var git).ShouldBeFalse();
        ToolVersionParser.TryParseGitLfs(lines, out var lfs).ShouldBeFalse();
        ToolVersionParser.TryParseSevenZip(lines, out var sevenZip).ShouldBeFalse();

        git.ShouldBeNull();
        lfs.ShouldBeNull();
        sevenZip.ShouldBeNull();
    }

    [Fact]
    public void TryParse_NoLines_ReturnsFalse()
    {
        ToolVersionParser.TryParseGit([], out var git).ShouldBeFalse();
        ToolVersionParser.TryParseGitLfs([], out var lfs).ShouldBeFalse();
        ToolVersionParser.TryParseSevenZip([], out var sevenZip).ShouldBeFalse();

        git.ShouldBeNull();
        lfs.ShouldBeNull();
        sevenZip.ShouldBeNull();
    }
}
