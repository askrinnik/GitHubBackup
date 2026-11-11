using GitHubBackup.Core.Git;
using GitHubBackup.Infrastructure.Git;

namespace GitHubBackup.Infrastructure.Tests.Git;

/// <summary>
/// Tests <see cref="GitRefParser"/>.
/// </summary>
public sealed class GitRefParserTests
{
    private const string _commit = "1111111111111111111111111111111111111111";
    private const string _tagObject = "2222222222222222222222222222222222222222";
    private const string _otherCommit = "3333333333333333333333333333333333333333";

    [Fact]
    public void ParseRemoteRefs_FullListing_KeepsOnlyBranchesAndTags()
    {
        string[] lines =
        [
            $"{_commit}\tHEAD",
            $"{_commit}\trefs/heads/main",
            $"{_otherCommit}\trefs/heads/feature/x",
            $"{_otherCommit}\trefs/pull/1/head",
            $"{_otherCommit}\trefs/pull/1/merge",
            $"{_otherCommit}\trefs/notes/commits",
            $"{_tagObject}\trefs/tags/v1",
            $"{_commit}\trefs/tags/v1^{{}}",
            $"{_commit}\trefs/tags/light",
            string.Empty,
        ];

        var snapshot = GitRefParser.ParseRemoteRefs(lines);

        snapshot.ShouldBe(Snapshot(
            ("heads/main", _commit),
            ("heads/feature/x", _otherCommit),
            ("tags/v1", _tagObject),
            ("tags/light", _commit)));
    }

    [Fact]
    public void ParseRemoteRefs_NoOutput_ReturnsEmptySnapshot() =>
        GitRefParser.ParseRemoteRefs([]).ShouldBe(RefSnapshot.Empty);

    [Fact]
    public void ParseLocalRefs_TrackingBranchesAndTags_MapsToServerNamesWithoutOriginHead()
    {
        string[] lines =
        [
            $"{_commit}\trefs/remotes/origin/HEAD",
            $"{_commit}\trefs/remotes/origin/main",
            $"{_otherCommit}\trefs/remotes/origin/feature/x",
            $"{_otherCommit}\trefs/remotes/upstream/main",
            $"{_commit}\trefs/heads/main",
            $"{_tagObject}\trefs/tags/v1",
        ];

        var snapshot = GitRefParser.ParseLocalRefs(lines);

        snapshot.ShouldBe(Snapshot(
            ("heads/main", _commit),
            ("heads/feature/x", _otherCommit),
            ("tags/v1", _tagObject)));
    }

    [Fact]
    public void ParseLocalRefs_BlankLines_SkipsThem() =>
        GitRefParser.ParseLocalRefs(["", "  "]).ShouldBe(RefSnapshot.Empty);

    [Theory]
    [InlineData("garbage")]
    [InlineData($"{_commit}\t")]
    [InlineData("\trefs/tags/v1")]
    public void ParseLocalRefs_LineWithoutTabOrParts_ThrowsFormatException(string line) =>
        Should.Throw<FormatException>(() => GitRefParser.ParseLocalRefs([line]));

    [Theory]
    [InlineData("garbage")]
    [InlineData($"{_commit}\t")]
    public void ParseRemoteRefs_LineWithoutTabOrParts_ThrowsFormatException(string line) =>
        Should.Throw<FormatException>(() => GitRefParser.ParseRemoteRefs([line]));

    [Fact]
    public void ParseRemoteRefs_DuplicateRef_ThrowsFormatException() =>
        Should.Throw<FormatException>(
            () => GitRefParser.ParseRemoteRefs([$"{_commit}\trefs/heads/main", $"{_otherCommit}\trefs/heads/main"]));

    [Theory]
    [InlineData("refs/heads/")]
    [InlineData("refs/tags/")]
    public void ParseRemoteRefs_EmptyName_ThrowsFormatException(string name) =>
        Should.Throw<FormatException>(() => GitRefParser.ParseRemoteRefs([$"{_commit}\t{name}"]));

    [Theory]
    [InlineData("refs/remotes/origin/")]
    [InlineData("refs/tags/")]
    public void ParseLocalRefs_EmptyName_ThrowsFormatException(string name) =>
        Should.Throw<FormatException>(() => GitRefParser.ParseLocalRefs([$"{_commit}\t{name}"]));

    [Fact]
    public void ParseRemoteRefs_LongMalformedLine_QuotesTruncatedLine()
    {
        var line = new string('x', 1000);

        var exception = Should.Throw<FormatException>(() => GitRefParser.ParseRemoteRefs([line]));

        exception.Message.ShouldContain(new string('x', 200) + "...");
        exception.Message.ShouldNotContain(new string('x', 201));
    }

    [Fact]
    public void ParseLocalRefs_MalformedLineWithControlAndFormatCharacters_QuotesThemAsQuestionMarks()
    {
        var exception = Should.Throw<FormatException>(() => GitRefParser.ParseLocalRefs(["a[31mb‮c"]));

        exception.Message.ShouldContain("a?[31mb?c");
        exception.Message.ShouldNotContain("");
        exception.Message.ShouldNotContain("‮");
    }

    [Fact]
    public void ParseRemoteRefs_DuplicateRefWithControlCharacter_QuotesSanitizedName() =>
        Should.Throw<FormatException>(
                () => GitRefParser.ParseRemoteRefs([$"{_commit}	refs/heads/ab", $"{_otherCommit}	refs/heads/ab"]))
            .Message.ShouldNotContain("");

    [Fact]
    public void ParseLocalRefs_DuplicateRef_ThrowsFormatException() =>
        Should.Throw<FormatException>(
            () => GitRefParser.ParseLocalRefs([$"{_commit}\trefs/tags/v1", $"{_otherCommit}\trefs/tags/v1"]));

    [Fact]
    public void ParseDefaultBranch_SymrefAndId_ReturnsBranch() =>
        GitRefParser.ParseDefaultBranch(["ref: refs/heads/develop\tHEAD", $"{_commit}\tHEAD"]).ShouldBe("develop");

    [Fact]
    public void ParseDefaultBranch_UnbornHead_ReturnsNull() =>
        GitRefParser.ParseDefaultBranch(["ref: refs/heads/main\tHEAD"]).ShouldBeNull();

    [Theory]
    [InlineData]
    [InlineData($"{_commit}\tHEAD")]
    [InlineData("ref: refs/tags/v1\tHEAD", $"{_commit}\tHEAD")]
    [InlineData("ref: refs/heads/\tHEAD", $"{_commit}\tHEAD")]
    [InlineData("ref: refs/heads/main\trefs/remotes/origin/HEAD", $"{_commit}\trefs/remotes/origin/HEAD")]
    public void ParseDefaultBranch_HeadNamesNoBranch_ReturnsNull(params string[] lines) =>
        GitRefParser.ParseDefaultBranch(lines).ShouldBeNull();

    [Theory]
    [InlineData("ref: refs/heads/-b")]
    [InlineData("ref: refs/heads/--upload-pack=evil")]
    [InlineData("ref: refs/heads/ma\tin")]
    [InlineData("ref: refs/heads/ma\nin")]
    [InlineData("ref: refs/heads/ma\u001bin")]
    [InlineData("ref: refs/heads/ma\u007fin")]
    public void ParseDefaultBranch_InvalidServerBranchName_ReturnsNull(string symref) =>
        GitRefParser.ParseDefaultBranch([$"{symref}\tHEAD", $"{_commit}\tHEAD"]).ShouldBeNull();

    /// <summary>
    /// Creates a snapshot of the given name and object id pairs.
    /// </summary>
    /// <param name="refs">The refs.</param>
    /// <returns>The snapshot.</returns>
    private static RefSnapshot Snapshot(params (string Name, string ObjectId)[] refs) =>
        new(refs.Select(entry => KeyValuePair.Create(entry.Name, entry.ObjectId)));
}
