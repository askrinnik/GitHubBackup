using GitHubBackup.Infrastructure.Git;

namespace GitHubBackup.Infrastructure.Tests.Git;

/// <summary>
/// Tests <see cref="GitStatusParser"/>.
/// </summary>
public sealed class GitStatusParserTests
{
    private const string _oid = "# branch.oid 1111111111111111111111111111111111111111";

    [Fact]
    public void Parse_CleanBranch_ReportsBranchWithoutChanges() =>
        GitStatusParser.Parse([_oid, "# branch.head main", "# branch.upstream origin/main", "# branch.ab +0 -0"])
            .ShouldBe(new GitStatusSummary("main", true, false, false));

    [Theory]
    [InlineData("1 .M N... 100644 100644 100644 a b file.txt")]
    [InlineData("1 M. N... 100644 100644 100644 a b file.txt")]
    [InlineData("2 R. N... 100644 100644 100644 a b R100 new.txt\told.txt")]
    [InlineData("u UU N... 100644 100644 100644 100644 a b c conflict.txt")]
    [InlineData("1 .M S.M. 160000 160000 160000 a b libs/core")]
    [InlineData("1 .M S..U 160000 160000 160000 a a libs/core")]
    public void Parse_ChangedEntry_ReportsChanges(string entry) =>
        GitStatusParser.Parse([_oid, "# branch.head main", entry]).HasChanges.ShouldBeTrue();

    [Fact]
    public void Parse_UntrackedEntry_ReportsUntrackedFilesOnly()
    {
        var summary = GitStatusParser.Parse([_oid, "# branch.head main", "? notes.txt"]);

        summary.HasUntrackedFiles.ShouldBeTrue();
        summary.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public void Parse_IgnoredEntry_IsSkipped() =>
        GitStatusParser.Parse([_oid, "# branch.head main", "! bin/"])
            .ShouldBe(new GitStatusSummary("main", true, false, false));

    [Fact]
    public void Parse_DetachedHead_ReportsNoBranch() =>
        GitStatusParser.Parse([_oid, "# branch.head (detached)"]).Branch.ShouldBeNull();

    [Fact]
    public void Parse_InitialBranch_ReportsNoCommits() =>
        GitStatusParser.Parse(["# branch.oid (initial)", "# branch.head main"]).HasCommits.ShouldBeFalse();

    [Theory]
    [InlineData("# branch.head main")]
    [InlineData(_oid)]
    [InlineData("? file.txt")]
    public void Parse_HeadersMissing_ThrowsFormatException(string line) =>
        Should.Throw<FormatException>(() => GitStatusParser.Parse([line]));
}
