using GitHubBackup.Core.Git;
using GitHubBackup.Core.State;

namespace GitHubBackup.Core.Tests.State;

/// <summary>
/// Tests <see cref="ArchiveRecord"/>.
/// </summary>
public sealed class ArchiveRecordTests
{
    /// <summary>The creation time used by every record.</summary>
    private static readonly DateTimeOffset _createdAt = new(2026, 10, 2, 14, 30, 15, TimeSpan.FromHours(3));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(@"a\b.zip")]
    [InlineData("a/b.zip")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData(@"C:\archives\a.zip")]
    [InlineData(@"\a.zip")]
    [InlineData("a<b.zip")]
    [InlineData("a>b.zip")]
    [InlineData("a\"b.zip")]
    [InlineData("a|b.zip")]
    [InlineData("a?b.zip")]
    [InlineData("a*b.zip")]
    [InlineData("a\tb.zip")]
    [InlineData("a\u0001b.zip")]
    [InlineData("NUL")]
    [InlineData("nul.zip")]
    [InlineData("CON.zip")]
    [InlineData("aux.tar.zip")]
    [InlineData("COM1.zip")]
    [InlineData("lpt9.zip")]
    [InlineData("a.zip.")]
    [InlineData("a.zip ")]
    public void Constructor_InvalidFile_ThrowsArgumentException(string file)
    {
        var exception = Should.Throw<ArgumentException>(() => Create(file: file));

        exception.ParamName.ShouldBe("file");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_EmptyBranch_ThrowsArgumentException(string branch)
    {
        var exception = Should.Throw<ArgumentException>(() => Create(branch: branch));

        exception.ParamName.ShouldBe("branch");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_EmptyHeadCommit_ThrowsArgumentException(string headCommit)
    {
        var exception = Should.Throw<ArgumentException>(() => Create(headCommit: headCommit));

        exception.ParamName.ShouldBe("headCommit");
    }

    [Fact]
    public void Constructor_NegativeSize_ThrowsArgumentOutOfRangeException()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(() => Create(sizeBytes: -1));

        exception.ParamName.ShouldBe("sizeBytes");
    }

    [Fact]
    public void Constructor_ZeroSize_IsAccepted() => Create(sizeBytes: 0).SizeBytes.ShouldBe(0);

    [Fact]
    public void Constructor_ValidValues_KeepsEveryValue()
    {
        var refs = new RefSnapshot([new("heads/main", "a1b2c3")]);

        var record = new ArchiveRecord("owner_repo.zip", _createdAt, "main", "a1b2c3", 42, refs);

        record.File.ShouldBe("owner_repo.zip");
        record.CreatedAt.ShouldBe(_createdAt);
        record.Branch.ShouldBe("main");
        record.HeadCommit.ShouldBe("a1b2c3");
        record.SizeBytes.ShouldBe(42);
        record.Refs.ShouldBe(refs);
    }

    /// <summary>
    /// Creates a record with valid values except for the ones given.
    /// </summary>
    /// <param name="file">The file name.</param>
    /// <param name="branch">The branch.</param>
    /// <param name="headCommit">The head commit.</param>
    /// <param name="sizeBytes">The size.</param>
    /// <returns>The record.</returns>
    private static ArchiveRecord Create(
        string file = "owner_repo.zip", string branch = "main", string headCommit = "a1b2c3", long sizeBytes = 10) =>
        new(file, _createdAt, branch, headCommit, sizeBytes, RefSnapshot.Empty);
}
