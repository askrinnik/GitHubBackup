using GitHubBackup.Core.Authentication;

namespace GitHubBackup.Core.Tests.Authentication;

/// <summary>
/// Tests <see cref="GitHubToken"/>.
/// </summary>
public sealed class GitHubTokenTests
{
    [Fact]
    public void ToString_WithToken_OmitsValue()
    {
        var text = new GitHubToken("secret-token-value", "some source").ToString();

        text.ShouldNotContain("secret-token-value");
        text.ShouldContain("some source");
    }

    [Fact]
    public void Anonymous_HasNoValue()
    {
        GitHubToken.Anonymous.IsAnonymous.ShouldBeTrue();
        GitHubToken.Anonymous.Value.ShouldBeNull();
    }

    [Fact]
    public void Constructor_WithToken_IsNotAnonymous()
    {
        var token = new GitHubToken("value", "source");

        token.IsAnonymous.ShouldBeFalse();
        token.Value.ShouldBe("value");
        token.SourceName.ShouldBe("source");
    }
}
