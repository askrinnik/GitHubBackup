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
