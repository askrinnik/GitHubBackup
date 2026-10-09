using GitHubBackup.Core.GitHub;

namespace GitHubBackup.Core.Tests.GitHub;

/// <summary>
/// Tests <see cref="GitHubUrl"/>.
/// </summary>
public sealed class GitHubUrlTests
{
    [Theory]
    [InlineData("https://github.com/askrinnik", "askrinnik")]
    [InlineData("https://github.com/AMTOSS/", "AMTOSS")]
    [InlineData("HTTPS://GitHub.COM/get-the-manual", "get-the-manual")]
    [InlineData("https://github.com/a", "a")]
    public void TryParseAccount_ValidUrl_ReturnsLogin(string url, string expectedLogin)
    {
        var parsed = GitHubUrl.TryParseAccount(url, out var login);

        parsed.ShouldBeTrue();
        login.ShouldBe(expectedLogin);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("askrinnik")]
    [InlineData("/askrinnik")]
    [InlineData("github.com/askrinnik")]
    [InlineData("http://github.com/askrinnik")]
    [InlineData("ssh://github.com/askrinnik")]
    [InlineData("https://www.github.com/askrinnik")]
    [InlineData("https://github.com.evil.example/askrinnik")]
    [InlineData("https://gist.github.com/askrinnik")]
    [InlineData("https://token@github.com/askrinnik")]
    [InlineData("https://user:token@github.com/askrinnik")]
    [InlineData("https://github.com:443/askrinnik")]
    [InlineData("https://github.com/askrinnik?tab=repositories")]
    [InlineData("https://github.com/askrinnik#readme")]
    [InlineData("https://github.com/")]
    [InlineData("https://github.com")]
    [InlineData("https://github.com//")]
    [InlineData("https://github.com/askrinnik//")]
    [InlineData("https://github.com/askrinnik/repo")]
    [InlineData("https://github.com/-askrinnik")]
    [InlineData("https://github.com/askrinnik-")]
    [InlineData("https://github.com/ask_rinnik")]
    [InlineData("https://github.com/ask%20rinnik")]
    [InlineData("https://github.com/ask rinnik")]
    [InlineData("https://github.com/..")]
    [InlineData("https://github.com/a234567890123456789012345678901234567890")]
    public void TryParseAccount_InvalidUrl_ReturnsFalse(string? url)
    {
        var parsed = GitHubUrl.TryParseAccount(url, out var login);

        parsed.ShouldBeFalse();
        login.ShouldBeNull();
    }

    [Theory]
    [InlineData("https://github.com/someone-else/library", "someone-else", "library")]
    [InlineData("https://github.com/askrinnik/GitHubBackup/", "askrinnik", "GitHubBackup")]
    [InlineData("https://GITHUB.com/o/my.repo_name-2", "o", "my.repo_name-2")]
    [InlineData("https://github.com/o/.github", "o", ".github")]
    public void TryParseRepository_ValidUrl_ReturnsOwnerAndName(string url, string expectedOwner, string expectedName)
    {
        var parsed = GitHubUrl.TryParseRepository(url, out var owner, out var name);

        parsed.ShouldBeTrue();
        owner.ShouldBe(expectedOwner);
        name.ShouldBe(expectedName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("someone/library")]
    [InlineData("http://github.com/someone/library")]
    [InlineData("https://www.github.com/someone/library")]
    [InlineData("https://ghp_secret@github.com/someone/library")]
    [InlineData("https://github.com:8443/someone/library")]
    [InlineData("https://github.com/someone/library.git")]
    [InlineData("https://github.com/someone/library.GIT")]
    [InlineData("https://github.com/someone/library?ref=main")]
    [InlineData("https://github.com/someone/library#readme")]
    [InlineData("https://github.com/someone")]
    [InlineData("https://github.com/someone/")]
    [InlineData("https://github.com/someone/library/tree/main")]
    [InlineData("https://github.com/someone/library//")]
    [InlineData("https://github.com/someone/.")]
    [InlineData("https://github.com/someone/..")]
    [InlineData("https://github.com/someone/lib%2Frary")]
    [InlineData("https://github.com/someone/lib rary")]
    [InlineData("https://github.com/some_one/library")]
    [InlineData("https://github.com/someone\\library")]
    public void TryParseRepository_InvalidUrl_ReturnsFalse(string? url)
    {
        var parsed = GitHubUrl.TryParseRepository(url, out var owner, out var name);

        parsed.ShouldBeFalse();
        owner.ShouldBeNull();
        name.ShouldBeNull();
    }

    [Theory]
    [InlineData("my-repo", true)]
    [InlineData("old.tool_2", true)]
    [InlineData(".github", true)]
    [InlineData("", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("repo.git", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("C:", false)]
    [InlineData("repo name", false)]
    [InlineData("repo.", false)]
    [InlineData("...", false)]
    [InlineData("CON", false)]
    [InlineData("con", false)]
    [InlineData("Nul", false)]
    [InlineData("prn", false)]
    [InlineData("aux", false)]
    [InlineData("com1", false)]
    [InlineData("COM9", false)]
    [InlineData("lpt1", false)]
    [InlineData("LPT9", false)]
    [InlineData("con.txt", false)]
    [InlineData("nul.tar.gz", false)]
    [InlineData("com0", true)]
    [InlineData("com10", true)]
    [InlineData("console", true)]
    [InlineData("nul-tool", true)]
    [InlineData("my.con", true)]
    public void IsValidRepositoryName_Name_ReturnsExpected(string name, bool expected) =>
        GitHubUrl.IsValidRepositoryName(name).ShouldBe(expected);

    [Theory]
    [InlineData("askrinnik", true)]
    [InlineData("con", false)]
    [InlineData("AUX", false)]
    [InlineData("lpt3", false)]
    [InlineData("com1x", true)]
    public void IsValidLogin_Login_ReturnsExpected(string login, bool expected) =>
        GitHubUrl.IsValidLogin(login).ShouldBe(expected);

    [Theory]
    [InlineData("https://github.com/con")]
    [InlineData("https://github.com/someone/NUL")]
    [InlineData("https://github.com/someone/con.txt")]
    [InlineData("https://github.com/someone/library.")]
    public void TryParse_DeviceNameOrTrailingDot_ReturnsFalse(string url)
    {
        GitHubUrl.TryParseAccount(url, out _).ShouldBeFalse();
        GitHubUrl.TryParseRepository(url, out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void IsValidRepositoryName_LongerThanGitHubAllows_ReturnsFalse()
    {
        GitHubUrl.IsValidRepositoryName(new string('a', 100)).ShouldBeTrue();
        GitHubUrl.IsValidRepositoryName(new string('a', 101)).ShouldBeFalse();
    }
}
