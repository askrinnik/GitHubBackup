using System.Globalization;
using GitHubBackup.Core.Archiving;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.State;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GitHubBackup.Core.Tests.Archiving;

/// <summary>
/// Tests <see cref="ArchiveNameBuilder"/>.
/// </summary>
public sealed class ArchiveNameBuilderTests
{
    private const string _commit = "0123456789ABCDEF0123456789abcdef01234567";

    private static readonly TimeZoneInfo _zone = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(3), "Test", "Test");

    [Fact]
    public void Build_Repository_FormatsOwnerRepoLocalTimeAndHash()
    {
        var name = CreateBuilder(new DateTimeOffset(2026, 11, 11, 10, 20, 30, TimeSpan.Zero)).Build("owner", "repo", BackupTarget.Repository, _commit);

        name.FileName.ShouldBe("owner_repo_20261111-132030_0123456789.zip");
        name.CreatedAt.ShouldBe(new DateTimeOffset(2026, 11, 11, 13, 20, 30, TimeSpan.FromHours(3)));
        name.CreatedAt.Offset.ShouldBe(TimeSpan.FromHours(3));
    }

    [Fact]
    public void Build_Wiki_AddsWikiSuffixToRepository()
    {
        var name = CreateBuilder(new DateTimeOffset(2026, 11, 11, 10, 20, 30, TimeSpan.Zero)).Build("owner", "repo", BackupTarget.Wiki, _commit);

        name.FileName.ShouldBe("owner_repo.wiki_20261111-132030_0123456789.zip");
    }

    [Fact]
    public void Build_SingleDigitFields_PadsWithZeros()
    {
        var name = CreateBuilder(new DateTimeOffset(2026, 1, 2, 0, 4, 5, TimeSpan.Zero)).Build("o", "r", BackupTarget.Repository, _commit);

        name.FileName.ShouldBe("o_r_20260102-030405_0123456789.zip");
    }

    [Fact]
    public void Build_NonGregorianCulture_FormatsTimeInvariantly()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("th-TH");
        try
        {
            var name = CreateBuilder(new DateTimeOffset(2026, 11, 11, 10, 20, 30, TimeSpan.Zero)).Build("o", "r", BackupTarget.Repository, _commit);

            name.FileName.ShouldBe("o_r_20261111-132030_0123456789.zip");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(10)]
    [InlineData(40)]
    public void Build_HashLength_FollowsOption(int length)
    {
        var name = CreateBuilder(DateTimeOffset.UnixEpoch, length).Build("o", "r", BackupTarget.Repository, _commit);

        name.FileName.ShouldEndWith("_" + _commit[..length].ToLowerInvariant() + ".zip");
    }

    [Fact]
    public void Build_UpperCaseHash_WritesLowerCase()
    {
        var name = CreateBuilder(DateTimeOffset.UnixEpoch, 6).Build("o", "r", BackupTarget.Repository, "ABCDEF123456");

        name.FileName.ShouldEndWith("_abcdef.zip");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0123456789g")]
    [InlineData("../../evil1")]
    public void Build_InvalidHash_Throws(string hash) => Should.Throw<ArgumentException>(() => CreateBuilder(DateTimeOffset.UnixEpoch).Build("o", "r", BackupTarget.Repository, hash))
            .ParamName.ShouldBe("headCommit");

    [Theory]
    [InlineData("")]
    [InlineData("-owner")]
    [InlineData("own er")]
    [InlineData("..")]
    [InlineData("CON")]
    public void Build_InvalidOwner_Throws(string owner) => Should.Throw<ArgumentException>(() => CreateBuilder(DateTimeOffset.UnixEpoch).Build(owner, "r", BackupTarget.Repository, _commit))
            .ParamName.ShouldBe("owner");

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData("a b")]
    public void Build_InvalidRepository_Throws(string repository) => Should.Throw<ArgumentException>(() => CreateBuilder(DateTimeOffset.UnixEpoch).Build("o", repository, BackupTarget.Repository, _commit))
            .ParamName.ShouldBe("repository");

    [Fact]
    public void TryParse_RepositoryName_ReturnsTargetLocalTimeAndHash()
    {
        var parsed = TryParse("octo_tool_20261111-132030_0123456789.zip").ShouldNotBeNull();

        parsed.ShouldBe(new ParsedArchiveName(
            BackupTarget.Repository, new DateTimeOffset(2026, 11, 11, 13, 20, 30, TimeSpan.FromHours(3)), "0123456789"));
    }

    [Fact]
    public void TryParse_WikiName_ReturnsWikiTarget() =>
        TryParse("octo_tool.wiki_20261111-132030_0123456789.zip").ShouldNotBeNull().Target.ShouldBe(BackupTarget.Wiki);

    [Fact]
    public void TryParse_DifferentCase_MatchesAndReturnsLowerCaseHash()
    {
        var parsed = TryParse("OCTO_Tool.WIKI_20261111-132030_ABCDEF0123.ZIP").ShouldNotBeNull();

        parsed.Target.ShouldBe(BackupTarget.Wiki);
        parsed.ShortHash.ShouldBe("abcdef0123");
    }

    [Theory]
    [InlineData(BackupTarget.Repository)]
    [InlineData(BackupTarget.Wiki)]
    public void TryParse_BuiltName_ReturnsValuesOfBuild(BackupTarget target)
    {
        var builder = CreateBuilder(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero));
        var name = builder.Build("octo", "my_tool.js", target, _commit);

        builder.TryParse(name.FileName, "octo", "my_tool.js", out var parsed).ShouldBeTrue();

        parsed.ShouldBe(new ParsedArchiveName(target, name.CreatedAt, "0123456789"));
    }

    [Theory]
    [InlineData(3, "octo_tool_00010101-000000_0123456789.zip")]
    [InlineData(-3, "octo_tool_99991231-235959_0123456789.zip")]
    public void TryParse_DateOutsideUtcRange_ReturnsFalse(int offsetHours, string fileName)
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Edge", TimeSpan.FromHours(offsetHours), "Edge", "Edge");
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        clock.SetLocalTimeZone(zone);
        var builder = new ArchiveNameBuilder(clock, Options.Create(new BackupOptions { ShortHashLength = 10 }));

        var matched = builder.TryParse(fileName, "octo", "tool", out var parsed);

        matched.ShouldBeFalse();
        parsed.ShouldBeNull();
    }

    [Theory]
    [InlineData("octo_tool_20261111-132030_0123456789.7z")]
    [InlineData("octo_tool_20261111-132030_0123456789.zip.tmp")]
    [InlineData("octo_tool_20261111-132030_0123456789.zipx")]
    [InlineData("other_tool_20261111-132030_0123456789.zip")]
    [InlineData("octo_tools_20261111-132030_0123456789.zip")]
    [InlineData("octo_tool_extra_20261111-132030_0123456789.zip")]
    [InlineData("octo_tool.wik_20261111-132030_0123456789.zip")]
    [InlineData("octotool_20261111-132030_0123456789.zip")]
    [InlineData("octo_tool_20261111132030_0123456789.zip")]
    [InlineData("octo_tool_20261111-1320300123456789.zip")]
    [InlineData("octo_tool_2026111a-132030_0123456789.zip")]
    [InlineData("octo_tool_20261311-132030_0123456789.zip")]
    [InlineData("octo_tool_20260230-132030_0123456789.zip")]
    [InlineData("octo_tool_20261111-246030_0123456789.zip")]
    [InlineData("octo_tool_20261111-132030_012345678.zip")]
    [InlineData("octo_tool_20261111-132030_01234567890.zip")]
    [InlineData("octo_tool_20261111-132030_012345678g.zip")]
    [InlineData("octo_tool_20261111-132030_0123456789")]
    [InlineData("readme.txt")]
    [InlineData("")]
    public void TryParse_NotAnArchiveNameOfRepository_ReturnsFalse(string fileName)
    {
        var matched = CreateBuilder(DateTimeOffset.UnixEpoch).TryParse(fileName, "octo", "tool", out var parsed);

        matched.ShouldBeFalse();
        parsed.ShouldBeNull();
    }

    [Fact]
    public void TryParse_RepositoryNamedLikeWiki_WikiSuffixBelongsToRepositoryName()
    {
        var builder = CreateBuilder(DateTimeOffset.UnixEpoch);

        builder.TryParse("octo_tool.wiki_20261111-132030_0123456789.zip", "octo", "tool.wiki", out var parsed).ShouldBeTrue();

        parsed.ShouldNotBeNull().Target.ShouldBe(BackupTarget.Repository);
    }

    [Theory]
    [InlineData(4, "octo_tool_20261111-132030_abcd.zip", true)]
    [InlineData(4, "octo_tool_20261111-132030_0123456789.zip", false)]
    [InlineData(12, "octo_tool_20261111-132030_0123456789ab.zip", true)]
    public void TryParse_HashLength_FollowsOption(int length, string fileName, bool expected) =>
        CreateBuilder(DateTimeOffset.UnixEpoch, length).TryParse(fileName, "octo", "tool", out _).ShouldBe(expected);

    /// <summary>
    /// Parses <paramref name="fileName"/> as an archive name of <c>octo/tool</c> with the default hash length.
    /// </summary>
    /// <param name="fileName">The file name.</param>
    /// <returns>The parsed values, or <see langword="null"/> when the name does not match.</returns>
    private static ParsedArchiveName? TryParse(string fileName) =>
        CreateBuilder(DateTimeOffset.UnixEpoch).TryParse(fileName, "octo", "tool", out var parsed) ? parsed : null;

    /// <summary>
    /// Creates a builder whose clock shows <paramref name="utcNow"/> in a zone three hours ahead of UTC.
    /// </summary>
    /// <param name="utcNow">The instant of the clock.</param>
    /// <param name="hashLength">The configured hash length.</param>
    /// <returns>The builder under test.</returns>
    private static ArchiveNameBuilder CreateBuilder(DateTimeOffset utcNow, int hashLength = 10)
    {
        var clock = new FakeTimeProvider(utcNow);
        clock.SetLocalTimeZone(_zone);
        return new ArchiveNameBuilder(clock, Options.Create(new BackupOptions { ShortHashLength = hashLength }));
    }
}
