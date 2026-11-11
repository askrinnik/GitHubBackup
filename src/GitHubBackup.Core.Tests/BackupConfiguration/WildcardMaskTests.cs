using System.Diagnostics;
using GitHubBackup.Core.BackupConfiguration;

namespace GitHubBackup.Core.Tests.BackupConfiguration;

/// <summary>
/// Tests <see cref="WildcardMask"/>.
/// </summary>
public sealed class WildcardMaskTests
{
    [Theory]
    [InlineData("repo", "repo", true)]
    [InlineData("REPO", "repo", true)]
    [InlineData("repo", "Repo", true)]
    [InlineData("repo", "repo2", false)]
    [InlineData("repo2", "repo", false)]
    [InlineData("", "", true)]
    [InlineData("", "a", false)]
    [InlineData("a", "", false)]
    public void IsMatch_NoWildcards_ComparesWholeTextIgnoringCase(string mask, string text, bool expected) =>
        WildcardMask.IsMatch(mask, text).ShouldBe(expected);

    [Theory]
    [InlineData("test-*", "test-app", true)]
    [InlineData("test-*", "test-", true)]
    [InlineData("test-*", "test", false)]
    [InlineData("*-archive", "old-archive", true)]
    [InlineData("*-archive", "archive", false)]
    [InlineData("*", "", true)]
    [InlineData("*", "anything", true)]
    [InlineData("**", "anything", true)]
    [InlineData("a*b*c", "abc", true)]
    [InlineData("a*b*c", "aXXbYYc", true)]
    [InlineData("a*b*c", "aXXbYY", false)]
    [InlineData("*a", "banana", true)]
    [InlineData("*a", "bananas", false)]
    [InlineData("*Test*", "my-test-repo", true)]
    public void IsMatch_Star_MatchesAnySequence(string mask, string text, bool expected) =>
        WildcardMask.IsMatch(mask, text).ShouldBe(expected);

    [Theory]
    [InlineData("re?o", "repo", true)]
    [InlineData("re?o", "reo", false)]
    [InlineData("re?o", "reppo", false)]
    [InlineData("?", "", false)]
    [InlineData("?", "x", true)]
    [InlineData("??", "x", false)]
    [InlineData("a?*", "ab", true)]
    public void IsMatch_QuestionMark_MatchesExactlyOneCharacter(string mask, string text, bool expected) =>
        WildcardMask.IsMatch(mask, text).ShouldBe(expected);

    [Theory]
    [InlineData("my.repo", "my.repo", true)]
    [InlineData("my.repo", "myXrepo", false)]
    [InlineData("a+b", "a+b", true)]
    [InlineData("a+b", "aab", false)]
    [InlineData("[ab]", "[ab]", true)]
    [InlineData("[ab]", "a", false)]
    [InlineData("a_b-c", "a_b-c", true)]
    [InlineData("^a$", "^a$", true)]
    [InlineData("^a$", "a", false)]
    [InlineData(@"a\*", @"a\xyz", true)]
    [InlineData(@"a\*", "axyz", false)]
    [InlineData("(a|b)", "(a|b)", true)]
    public void IsMatch_RegexMetacharacters_AreLiteral(string mask, string text, bool expected) =>
        WildcardMask.IsMatch(mask, text).ShouldBe(expected);

    [Theory]
    [InlineData("someone/*", "someone/repo", true)]
    [InlineData("someone/*", "other/repo", false)]
    [InlineData("*/repo", "any/repo", true)]
    [InlineData("*/repo", "any/other", false)]
    [InlineData("repo", "someone/repo", false)]
    public void IsMatch_FullName_MatchesAcrossSlash(string mask, string text, bool expected) =>
        WildcardMask.IsMatch(mask, text).ShouldBe(expected);

    [Fact]
    public void IsMatch_ManyStarsAndLongText_FinishesQuickly()
    {
        var text = new string('a', 20_000);
        var stopwatch = Stopwatch.StartNew();

        var matched = WildcardMask.IsMatch("*a*a*a*a*b", text);

        matched.ShouldBeFalse();
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }
}
